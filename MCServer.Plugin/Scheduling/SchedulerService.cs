using System.Text.Json;
using Cronos;

namespace MCServer.Plugins;

/// <summary>
/// Core schedule persistence + timers. Works against <see cref="IGameServer"/>
/// so every server plugin shares the same schedule system.
/// </summary>
public static class SchedulerService
{
    private static readonly Dictionary<string, Timer> GlobalTimers = [];

    public static string SchedulesPath(this IGameServer server) =>
        Path.Combine(server.ServerPath, "schedules.json");

    public static List<Schedule> GetSchedules(this IGameServer server)
    {
        if (!File.Exists(server.SchedulesPath()))
            return [];

        var Schedules = JsonSerializer.Deserialize<Schedule[]>
            (File.Open(server.SchedulesPath(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        return [.. Schedules ?? []];
    }

    public static void SetSchedules(this IGameServer server, IEnumerable<Schedule> schedules)
    {
        var json = JsonSerializer.Serialize(schedules);
        File.WriteAllText(server.SchedulesPath(), json);
    }

    public static void AddSchedules(this IGameServer server, IEnumerable<Schedule> schedules)
    {
        foreach (var item in schedules)
            AddSchedule(server, item);
    }

    public static void AddSchedule(this IGameServer server, Schedule Schedule)
    {
        if (!Schedule.Enabled) return;

        TimeSpan? delay = CalculateDelay(Schedule);

        if (delay is null) return;

        Timer GTimer = null!;
        GTimer = new Timer(args =>
        {
            if (args is not (Timer timer, IGameServer server, Schedule schedule))
            {
                try
                {
                    GTimer.Dispose();
                    GlobalTimers.Remove(Schedule.ID);
                }
                catch { }
                return;
            }

            server.RunCommand(schedule.Command);
            schedule.LastRun = DateTime.Now;

            TimeSpan? delay = CalculateDelay(schedule);
            if (delay is null)
                stop();
            else
                timer.Change(delay ?? new(), Timeout.InfiniteTimeSpan);

            void stop()
            {
                schedule.Enabled = false;
                timer.Dispose();
                GlobalTimers.Remove(schedule.ID);
            }

        }, (GTimer, server, Schedule), delay.Value, Timeout.InfiniteTimeSpan);

        GlobalTimers.Add(Schedule.ID, GTimer);
    }

    public static void RemoveSchedules(this IGameServer server, IEnumerable<Schedule> schedules)
    {
        foreach (var item in schedules)
            RemoveSchedule(server, item);
    }

    public static void RemoveSchedule(this IGameServer server, Schedule Schedule)
    {
        if (GlobalTimers.TryGetValue(Schedule.ID, out var timer))
        {
            timer.Dispose();
            GlobalTimers.Remove(Schedule.ID);
        }
    }

    private static TimeSpan? CalculateDelay(Schedule schedule)
    {
        if (schedule.StartDate.Date < DateTime.Today) return null;
        var now = DateTime.Now;
        TimeSpan? delay = null;

        switch (schedule.Mode)
        {
            case ScheduleMode.Once:
                var scheduledTime = now + schedule.Time;
                if (scheduledTime <= now)
                    scheduledTime = scheduledTime.AddDays(1);
                delay = scheduledTime - now;
                break;

            case ScheduleMode.Range:
                if (now < schedule.EndDate)
                    delay = CalculateCronDelay(schedule.Cron, now);
                break;

            case ScheduleMode.Indefinite:
                delay = CalculateCronDelay(schedule.Cron, now);
                break;
        }

        if (delay <= TimeSpan.Zero) return null;
        return delay;
    }

    private static TimeSpan? CalculateCronDelay(CronExpression cron, DateTime now)
    {
        var next = cron.GetNextOccurrence(now);
        if (next is null) return null;
        return next.Value - now;
    }
}
