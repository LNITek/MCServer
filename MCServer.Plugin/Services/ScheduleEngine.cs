using System.Collections.Specialized;
using System.ComponentModel;
using Cronos;

namespace MCServer.Plugins;

/// <summary>
/// Runs a server's schedules. One instance per <see cref="IGameServer"/>,
/// created by the server and disposed with it — no static timer tables and
/// no arm/disarm bookkeeping anywhere else.
///
/// Instead of one <see cref="System.Threading.Timer"/> per schedule, a single
/// worker loop owns a min-heap of due instants (UTC). Any change to the
/// schedule collection or to an item's editable fields just marks a rebuild
/// and wakes the loop, so edits take effect on the next pass without the
/// double-arming/drift problems of per-schedule timers. Recurring fires
/// compute their next occurrence from the scheduled instant, so a slow pass
/// cannot silently skip an occurrence; overdue occurrences are caught up,
/// not dropped.
///
/// Runtime state (<see cref="Schedule.Status"/>, <see cref="Schedule.NextRun"/>,
/// <see cref="Schedule.LastRun"/>) is owned here and persisted through
/// <see cref="ScheduleStore"/>, so the UI can always answer "why didn't it run?".
/// </summary>
public sealed class ScheduleEngine : IDisposable
{
    private readonly IGameServer _server;
    private readonly TimeProvider _time;

    private readonly PriorityQueue<Schedule, DateTime> _queue = new();
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Dictionary<string, Schedule> _subscribed = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile int _rebuildNeeded = 1;
    // >0 while the engine itself is writing schedule runtime state, so the
    // item PropertyChanged hook does not trigger pointless rebuild loops.
    private volatile int _suppress;
    private bool _disposed;

    private Timer? _saveTimer;
    private volatile bool _savePending;

    /// <summary>Raised whenever schedule runtime state changed — UI hook to refresh.</summary>
    public event Action? Changed;

    public ScheduleEngine(IGameServer server, TimeProvider? timeProvider = null)
    {
        _server = server;
        _time = timeProvider ?? TimeProvider.System;

        _server.ServerSchedules.CollectionChanged += OnCollectionChanged;
        SyncSubscriptions();
        Reconcile();
        ReArm();
        RequestSave();

        _cts = new();
        _loop = Task.Run(RunLoopAsync, _cts.Token);
    }

    // ---------------------------------------------------------------- arm

    /// <summary>
    /// Startup reconciliation against state persisted by a previous session.
    /// Events that fell due while MCServer was shut down are NOT replayed:
    /// one-shot triggers become <see cref="ScheduleStatus.Missed"/> and keep
    /// their original due time for reporting. Recurring schedules simply
    /// reschedule; cron skips during downtime are inherent to cron.
    /// Never marks a missed event that already has a <see cref="Schedule.LastRun"/>.
    /// </summary>
    private void Reconcile()
    {
        _suppress = 1;
        try
        {
            var nowUtc = NowUtc;
            foreach (var schedule in Snapshot())
            {
                if (!schedule.Enabled) { SetState(schedule, ScheduleStatus.Disabled, null, schedule.NextRun); continue; }
                if (schedule.Status is ScheduleStatus.Completed or ScheduleStatus.Missed) continue;

                if (schedule.Trigger is TriggerKind.Once && schedule.LastRun is null &&
                    schedule.NextRun is { } persisted && persisted <= nowUtc)
                {
                    SetState(schedule, ScheduleStatus.Missed,
                        $"Was due {schedule.NextRunFormatted} while MCServer was shut down; not replayed. Change the date to schedule it again.",
                        schedule.NextRun);
                }
                // everything else is (re)armed by ReArm() below
            }
            Changed?.Invoke();
        }
        finally { _suppress = 0; }
    }

    /// <summary>Recomputes next due instants and statuses for every schedule
    /// and replaces the pending queue. Call context: worker loop thread.</summary>
    private void ReArm()
    {
        _suppress = 1;
        try
        {
            var entries = new List<(Schedule schedule, DateTime fireUtc)>();
            foreach (var schedule in Snapshot())
            {
                // Terminal states persist across rebuilds until the schedule's
                // inputs change so that a Completed fire or a Missed report is
                // never silently overwritten while idle.
                if (StaysTerminal(schedule)) continue;

                var (status, reason, nextUtc) = Evaluate(schedule);
                SetState(schedule, status, reason, nextUtc);
                if (status is ScheduleStatus.Armed && nextUtc is { } fireUtc)
                    entries.Add((schedule, fireUtc));
            }

            lock (_lock)
            {
                _queue.Clear();
                foreach (var (schedule, fireUtc) in entries)
                    _queue.Enqueue(schedule, fireUtc);
            }

            Changed?.Invoke();
        }
        finally { _suppress = 0; }
        // Add/edit/migration all funnel through ReArm; persist the new state.
        // Without this, new and edited schedules were never written to disk
        // until an actual command fired (Flush() from the UI raced the engine
        // and usually hit `_savePending == false`).
        RequestSave();
    }

    /// <summary>True while a terminal state should keep overriding re-evaluation.
    /// Moving a Once schedule's due date into the future takes it out of the
    /// terminal state so edits (e.g. changing a missed date) take effect.</summary>
    private bool StaysTerminal(Schedule schedule)
    {
        if (schedule.Trigger is not TriggerKind.Once) return false;
        if (ToUtc(schedule.OneShotDue) > NowUtc) return false;
        if (schedule.Status is ScheduleStatus.Completed) return schedule.LastRun is not null;
        if (schedule.Status is ScheduleStatus.Missed) return schedule.LastRun is null;
        return false;
    }

    /// <summary>Evaluate one schedule (status / reason / next fire) at the current time.</summary>
    private (ScheduleStatus Status, string? Reason, DateTime? NextUtc) Evaluate(Schedule schedule)
    {
        if (!schedule.Enabled)
            return (ScheduleStatus.Disabled, null, null);
        if (string.IsNullOrWhiteSpace(schedule.Command))
            return (ScheduleStatus.Invalid, "Command is empty.", null);

        if (schedule.Trigger is TriggerKind.Once)
        {
            var dueUtc = ToUtc(schedule.OneShotDue);
            if (dueUtc <= NowUtc)
                return schedule.LastRun is not null
                    ? (ScheduleStatus.Completed, null, null)
                    : (ScheduleStatus.Invalid, "Due date is in the past. Change the date to schedule again.", null);
            return (ScheduleStatus.Armed, null, dueUtc);
        }

        return NextCronNow(schedule);
    }

    /// <summary>Cron evaluation anchored at "now", honoring the optional start/end window.</summary>
    private (ScheduleStatus Status, string? Reason, DateTime? NextUtc) NextCronNow(Schedule schedule)
    {
        if (!CronExpression.TryParse(schedule.Cron, out var cron) || cron is null)
            return (ScheduleStatus.Invalid, "Invalid cron expression.", null);

        var nowLocal = _time.GetLocalNow().LocalDateTime;
        if (schedule.EndDate is { } end && nowLocal.Date > end.Date)
            return (ScheduleStatus.Disabled, $"Window ended {end:d}.", null);

        var fromLocal = schedule.StartDate is { } start && nowLocal < start.Date
            ? new DateTimeOffset(start.Date, TimeZoneInfo.Local.GetUtcOffset(start.Date))
            : new DateTimeOffset(nowLocal, TimeZoneInfo.Local.GetUtcOffset(nowLocal));

        var occurrence = cron.GetNextOccurrence(fromLocal, TimeZoneInfo.Local);
        if (occurrence is null)
            return (ScheduleStatus.Invalid, "Cron expression has no future occurrence.", null);
        if (schedule.EndDate is { } windowEnd && occurrence.Value.LocalDateTime.Date > windowEnd.Date)
            return (ScheduleStatus.Disabled, $"Window ended {windowEnd:d}.", null);

        return (ScheduleStatus.Armed, null, occurrence.Value.UtcDateTime);
    }

    // ---------------------------------------------------------------- run

    private async Task RunLoopAsync()
    {
        var token = _cts!.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (Interlocked.Exchange(ref _rebuildNeeded, 0) == 1)
                    ReArm();

                var fires = new List<(Schedule schedule, DateTime fireUtc)>();
                var nowUtc = NowUtc;
                lock (_lock)
                {
                    while (_queue.TryPeek(out _, out var when) && when <= nowUtc)
                        fires.Add((_queue.Dequeue(), when));
                }

                foreach (var (schedule, fireUtc) in fires)
                    Fire(schedule, fireUtc);

                DateTime? next = null;
                lock (_lock)
                {
                    if (_queue.TryPeek(out _, out var when)) next = when;
                }

                if (next is null)
                {
                    await _wake.WaitAsync(token);
                    continue;
                }

                var delayTask = Task.Delay(next.Value - NowUtc, token);
                await Task.WhenAny(delayTask, _wake.WaitAsync(token));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine("ScheduleEngine loop: {0}", ex); }
                catch { /* logging must never break scheduling. */ }
                try { await Task.Delay(1000, token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Runs the command and updates the schedule's runtime state.
    /// Commands run even when the game server process is offline (they are
    /// sent to the process wrapper regardless); a throwing command fails
    /// visibly on the schedule instead of being silently swallowed.</summary>
    private void Fire(Schedule schedule, DateTime scheduledUtc)
    {
        string? failure = null;
        try { _server.RunCommand(schedule.Command.Trim()); }
        catch (Exception ex) { failure = ex.Message; }

        _suppress = 1;
        try
        {
            schedule.LastRun = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(scheduledUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
            if (failure is not null)
                schedule.StatusReason = $"Run failed: {failure}";

            if (schedule.Trigger is TriggerKind.Once)
            {
                SetState(schedule, ScheduleStatus.Completed, failure is null ? null : schedule.StatusReason, null);
            }
            else
            {
                // Anchor the next occurrence at the scheduled instant, not
                // "now": a garbage-collector pause or slow work between ticks
                // must not skip or drift an occurrence. If the next
                // occurrence is already overdue (e.g. every-minute cron
                // during a long pause) it becomes due and runs on the next
                // loop pass — caught up, not dropped.
                var scheduledLocalTime = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(scheduledUtc, DateTimeKind.Utc), TimeZoneInfo.Local);
                var fromLocal = new DateTimeOffset(
                    scheduledLocalTime,
                    TimeZoneInfo.Local.GetUtcOffset(scheduledLocalTime));

                if (CronExpression.TryParse(schedule.Cron, out var cron) && cron is not null)
                {
                    var next = cron.GetNextOccurrence(fromLocal, TimeZoneInfo.Local);
                    if (next is null)
                        SetState(schedule, ScheduleStatus.Invalid, "Cron expression has no future occurrence.", null);
                    else if (schedule.EndDate is { } end && next.Value.LocalDateTime.Date > end.Date)
                        SetState(schedule, ScheduleStatus.Disabled, $"Window ended {end:d}.", null);
                    else
                    {
                        var fireUtc = next.Value.UtcDateTime;
                        Enqueue(schedule, DateTime.SpecifyKind(fireUtc, DateTimeKind.Utc));
                        SetState(schedule, ScheduleStatus.Armed, failure is null ? null : schedule.StatusReason, fireUtc);
                    }
                }
                else
                    SetState(schedule, ScheduleStatus.Invalid, "Invalid cron expression.", null);
            }

            Changed?.Invoke();
        }
        finally { _suppress = 0; }
        RequestSave();
    }

    // ---------------------------------------------------------------- edits

    private void SetState(Schedule schedule, ScheduleStatus status, string? reason, DateTime? nextUtc)
    {
        schedule.Status = status;
        schedule.StatusReason = reason;
        schedule.NextRun = nextUtc is null ? null : DateTime.SpecifyKind(nextUtc.Value, DateTimeKind.Utc);
    }

    private void Enqueue(Schedule schedule, DateTime fireUtc)
    {
        lock (_lock)
        {
            if (IsTerminal(schedule.Status)) return;
            _queue.Enqueue(schedule, fireUtc);
        }
    }

    // ---------------------------------------------------------------- plumbing

    private IReadOnlyList<Schedule> Snapshot()
    {
        try { return [.. _server.ServerSchedules]; }
        catch { return []; }
    }

    private static bool IsTerminal(ScheduleStatus status) =>
        status is ScheduleStatus.Completed or ScheduleStatus.Missed;

    private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

    private static DateTime ToUtc(DateTime localWallTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localWallTime, DateTimeKind.Unspecified), TimeZoneInfo.Local);

    private void RequestRebuild()
    {
        _rebuildNeeded = 1;
        Wake();
    }

    private void Wake()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { /* already pending; the loop will see it. */ }
        catch (ObjectDisposedException) { }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppress != 0) return;
        SyncSubscriptions();
        RequestRebuild();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppress != 0) return;
        if (e.PropertyName is nameof(Schedule.LastRun) or nameof(Schedule.NextRun) or nameof(Schedule.Status)) return;
        RequestRebuild();
    }

    private void SyncSubscriptions()
    {
        var present = new HashSet<string>();
        foreach (var schedule in Snapshot())
        {
            present.Add(schedule.ID);
            if (_subscribed.ContainsKey(schedule.ID)) continue;
            schedule.PropertyChanged += OnItemChanged;
            _subscribed.Add(schedule.ID, schedule);
        }

        foreach (var (id, schedule) in _subscribed.Where(pair => !present.Contains(pair.Key)).ToList())
        {
            schedule.PropertyChanged -= OnItemChanged;
            _subscribed.Remove(id);
        }
    }

    // ---------------------------------------------------------------- saving

    public void Flush()
    {
        var timer = _saveTimer;
        timer?.Change(Timeout.Infinite, Timeout.Infinite);
        if (!_savePending) return;
        _savePending = false;
        SaveInternal();
    }

    /// <summary>Debounced persistence of runtime + user state.</summary>
    private void RequestSave()
    {
        if (_disposed) return;
        if (_savePending)
        {
            _saveTimer!.Change(500, Timeout.Infinite);
            return;
        }

        if (_saveTimer is null)
        {
            _saveTimer = new Timer(_ =>
            {
                if (!_savePending) return;
                _savePending = false;
                SaveInternal();
            }, null, Timeout.Infinite, Timeout.Infinite);
        }
        _savePending = true;
        _saveTimer.Change(500, Timeout.Infinite);
    }

    private void SaveInternal()
    {
        try { _server.SaveSchedules(Snapshot()); }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine("ScheduleEngine save: {0}", ex); }
            catch { }
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _server.ServerSchedules.CollectionChanged -= OnCollectionChanged;
        foreach (var schedule in _subscribed.Values)
            schedule.PropertyChanged -= OnItemChanged;
        _subscribed.Clear();

        try { Flush(); } catch { }

        _cts?.Cancel();
        try { _loop?.Wait(500); } catch { }
        _cts?.Dispose();
        _wake.Dispose();
        _saveTimer?.Dispose();
    }
}
