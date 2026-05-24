using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Cronos;
using ExtraFunctions.ExGenerators;
using MudBlazor;

namespace MCServer.Helpers;

public enum ScheduleMode
{
    Once,
    Range,
    Indefinite,
}

//public interface ISchedule
//{
//    string Name { get; set; }
//    string Description { get; set; }
//    bool Enabled { get; set; }
//    ScheduleMode Mode { get; }
//    Color StateBrush { get; }

//    string Command { get; set; }

//    DateTime? LastRun { get; set; }
//    string LastRunFormatted { get; }
//    DateTime StartDate { get; set; }
//    string StartDateFormatted { get; }

//    ISchedule ChangeMode(ScheduleMode mode);
//}

public partial class Schedule : INotifyPropertyChanged
{
    [NotifyChanged]
    string name { get; set; }
    [NotifyChanged]
    string description { get; set; } = string.Empty;
    [NotifyChanged([nameof(StateBrush)])]
    bool enabled { get; set; } = false;
    [NotifyChanged([nameof(StateBrush)])]
    public ScheduleMode Mode { get; internal set; } = ScheduleMode.Once;

    [NotifyChanged]
    string command { get; set; } = string.Empty;
    [NotifyChanged([nameof(StartDateFormatted)])]
    DateTime startDate { get; set; } = DateTime.Today;

    [NotifyChanged([nameof(EndDateFormatted)])]
    DateTime endDate { get; set; } = DateTime.Today;
    [NotifyChanged]
    CronExpression cron { get; set; } = CronExpression.Daily;

    [NotifyChanged]
    TimeSpan time { get; set; } = TimeSpan.Zero;

    [NotifyChanged([nameof(LastRunFormatted)])]
    DateTime? lastRun { get; set; } = null;

    [JsonIgnore]
    public Color StateBrush
    {
        get
        {
            if (!Enabled) return Color.Error;
            return Mode switch
            {
                ScheduleMode.Once => Color.Warning,
                ScheduleMode.Range => Color.Success,
                ScheduleMode.Indefinite => Color.Info,
                _ => Color.Default,
            };
        }
    }

    [JsonIgnore]
    public string LastRunFormatted => "Last execution: " + LastRun?.ToString("d") ?? "-Unknown";
    [JsonIgnore]
    public string StartDateFormatted => StartDate.ToString("d") ?? "Pick a starting date";
    [JsonIgnore]
    public string EndDateFormatted => EndDate.ToString("d") ?? "Pick a end date";


    //public ISchedule ChangeMode(ScheduleMode mode)
    //{
    //    if (Mode == mode) return null!;

    //    CronExpression cronExpression = CronExpression.Daily;
    //    if (this is ScheduleRange range)
    //        cronExpression = range.Cron;
    //    else if (this is ScheduleCron cron)
    //        cronExpression = cron.Cron;

    //    ISchedule schedule = mode switch
    //    {
    //        ScheduleMode.Once => new ScheduleOnce(),
    //        ScheduleMode.Range => new ScheduleRange() { Cron = cronExpression },
    //        ScheduleMode.Indefinite => new ScheduleCron() { Cron = cronExpression },
    //        _ => new Schedule()
    //    };

    //    schedule.Name = name;
    //    schedule.Description = description;
    //    schedule.Enabled = enabled;
    //    schedule.Command = command;
    //    schedule.StartDate = startDate;
    //    schedule.LastRun = lastRun;

    //    return schedule;
    //}

    public override string ToString() => $"{Command.Trim().ToLower()}";
}

/*public partial class ScheduleOnce : Schedule, ISchedule, INotifyPropertyChanged
{
    [NotifyChanged]
    TimeSpan time { get; set; } = TimeSpan.Zero;

    [JsonConstructor]
    public ScheduleOnce()
    {
        Mode = ScheduleMode.Once;
    }

    public override string ToString() => base.ToString();
}

public partial class ScheduleRange : Schedule, ISchedule, INotifyPropertyChanged
{
    [NotifyChanged([nameof(EndDateFormatted)])]
    DateTime endDate { get; set; } = DateTime.Today;
    [NotifyChanged]
    CronExpression cron { get; set; } = CronExpression.Daily;

    [JsonIgnore]
    public string EndDateFormatted => EndDate.ToString("d") ?? "Pick a end date";

    [JsonConstructor]
    public ScheduleRange()
    {
        Mode = ScheduleMode.Range;
    }

    public override string ToString() => base.ToString();
}

public partial class ScheduleCron : Schedule, ISchedule, INotifyPropertyChanged
{
    [NotifyChanged]
    CronExpression cron { get; set; } = CronExpression.Daily;

    [JsonConstructor]
    public ScheduleCron()
    {
        Mode = ScheduleMode.Indefinite;
    }

    public override string ToString() => base.ToString();
}*/
