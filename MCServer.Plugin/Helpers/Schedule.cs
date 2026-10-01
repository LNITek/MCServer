using System.ComponentModel;
using System.Text.Json.Serialization;
using Cronos;
using ExtraFunctions.ExGenerators;
using MudBlazor;

namespace MCServer.Plugins;

public enum ScheduleMode
{
    Once,
    Range,
    Indefinite,
}

public partial class Schedule : INotifyPropertyChanged
{
    public string ID { get; init; } = Guid.NewGuid().ToString();
    [NotifyChanged]
    string name { get; set; } = string.Empty;
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

    public override string ToString() => $"{Command.Trim().ToLower()}";
}
