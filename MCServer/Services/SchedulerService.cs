using System.Text.Json;
using Cronos;
using MCServer.Helpers;
using MCServer.Server;

namespace MCServer.Services;

public static class SchedulerService
{
    private static readonly Dictionary<string, Timer> GlobalTimers = [];

    public static List<Schedule> GetSchedules(this MCBedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/schedules.json"))
            return [];
        //Program.NotifyUser("Server Schedules: could not find server schedules file!", MudBlazor.Severity.Error);

        var Schedules = JsonSerializer.Deserialize<Schedule[]>
            (File.Open(server.ServerPath + "/schedules.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        return [.. Schedules ?? []];
    }

    public static void SetSchedules(this MCBedrockServer server, IEnumerable<Schedule> schedules)
    {
        var json = JsonSerializer.Serialize(schedules);
        File.WriteAllText(server.ServerPath + "/schedules.json",json);
    }
    
    public static void AddSchedules(this MCBedrockServer server, IEnumerable<Schedule> schedules)
    {
        foreach (var item in schedules)
            AddSchedule(server, item);
    }

    public static void AddSchedule(this MCBedrockServer server, Schedule Schedule)
    {
        if (!Schedule.Enabled) return;

        TimeSpan? delay = CalculateDelay(Schedule);

        if (delay is null) return;

        Timer GTimer = null!;
        GTimer = new Timer(args =>
        {
            if(args is not (Timer timer, MCBedrockServer server, Schedule schedule))
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

    public static void RemoveSchedules(this MCBedrockServer server, IEnumerable<Schedule> schedules)
    {
        foreach (var item in schedules)
            RemoveSchedule(server, item);
    }

    public static void RemoveSchedule(this MCBedrockServer server, Schedule Schedule)
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