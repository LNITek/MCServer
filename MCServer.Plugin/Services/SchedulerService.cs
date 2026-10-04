using Cronos;
using Newtonsoft.Json;

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

        // Never let a corrupt schedules.json take down server startup.
        try
        {
            using var stream = File.Open(server.SchedulesPath(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(reader);
            var serializer = JsonSerializer.Create(JsonOptions.SerializerSettings);
            var schedules = serializer.Deserialize<Schedule[]>(jsonReader);

            return [.. schedules ?? []];
        }
        catch
        {
            return [];
        }
    }

    public static void SetSchedules(this IGameServer server, IEnumerable<Schedule> schedules)
    {
        var dir = Path.GetDirectoryName(server.SchedulesPath());
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonConvert.SerializeObject(schedules, JsonOptions.SerializerSettings);
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
        var now = DateTime.Now;
        TimeSpan? delay = null;

        switch (schedule.Mode)
        {
            case ScheduleMode.Once:
                // Fire at StartDate + Time. If today's time already passed,
                // roll to tomorrow; a date in the past never fires.
                var target = schedule.StartDate.Date + schedule.Time;
                if (target <= now)
                {
                    if (schedule.StartDate.Date >= DateTime.Today)
                        target = target.AddDays(1);
                    else
                        return null;
                }
                delay = target - now;
                break;

            case ScheduleMode.Range:
                if (schedule.Cron is null || now.Date > schedule.EndDate.Date) return null;
                delay = CalculateCronDelay(schedule.Cron, now < schedule.StartDate ? schedule.StartDate.Date : now);
                break;

            case ScheduleMode.Indefinite:
                if (schedule.Cron is null) return null;
                delay = CalculateCronDelay(schedule.Cron, now < schedule.StartDate ? schedule.StartDate.Date : now);
                break;
        }

        if (delay <= TimeSpan.Zero) return null;
        return delay;
    }

    private static TimeSpan? CalculateCronDelay(CronExpression cron, DateTime now)
    {
        // Both DateTime overloads demand Kind=Utc and throw otherwise (this crashed
        // schedule saves). The DateTimeOffset overload accepts our local wall-time
        // inputs (DateTime.Now / StartDate.Date) and interprets the expression in
        // server-local time, so "daily at noon" means local noon.
        var from = new DateTimeOffset(now);
        var next = cron.GetNextOccurrence(from, TimeZoneInfo.Local);
        if (next is null) return null;
        return next.Value - from;
    }
}
