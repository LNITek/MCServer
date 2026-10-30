using System.ComponentModel;
using Newtonsoft.Json;
using ExtraFunctions.ExGenerators;
using MudBlazor;

namespace MCServer.Plugins;

/// <summary>What makes a schedule fire.</summary>
public enum TriggerKind
{
    /// <summary>Fires exactly once at <see cref="Schedule.OneShotDue"/>.</summary>
    Once,
    /// <summary>Fires whenever the cron expression occurs (optionally inside the start/end window).</summary>
    Cron,
}

/// <summary>Runtime state the engine reports so nothing fails silently.</summary>
public enum ScheduleStatus
{
    /// <summary>Enabled switch is off, or a recurring schedule's window has ended.</summary>
    Disabled,
    /// <summary>Scheduled and waiting; <see cref="Schedule.NextRun"/> says when.</summary>
    Armed,
    /// <summary>A one-shot trigger has fired; terminal until the due date is changed.</summary>
    Completed,
    /// <summary>Came due while MCServer itself was shut down; reported, not replayed.</summary>
    Missed,
    /// <summary>Cannot run at all (bad cron, empty command, due date in the past). See <see cref="Schedule.StatusReason"/>.</summary>
    Invalid,
}

public partial class Schedule : INotifyPropertyChanged
{
    public string ID { get; init; } = Guid.NewGuid().ToString();
    [NotifyChanged]
    string name { get; set; } = string.Empty;
    [NotifyChanged]
    string description { get; set; } = string.Empty;
    [NotifyChanged([nameof(StatusBrush), nameof(StateBrush)])]
    bool enabled { get; set; } = false;
    // NOTE: fields are lowercase by convention — the NotifyChanged generator
    // creates the public PascalCase property (e.g. Trigger) with change
    // notification. Do NOT hand-write a PascalCase property here: it
    // collides with the generated lowercase alias, producing duplicate
    // JSON keys and losing the value on load (internal setter
    // is ignored by the deserializer).
    [NotifyChanged([nameof(StateBrush), nameof(StatusBrush)])]
    TriggerKind trigger { get; set; } = TriggerKind.Once;
    [NotifyChanged([nameof(CronFormatted)])]
    string cron { get; set; } = "0 12 * * *";

    [NotifyChanged]
    string command { get; set; } = string.Empty;

    // ---- user-editable schedule inputs ----
    /// <summary>Exact local wall-clock instant a <see cref="TriggerKind.Once"/> trigger fires.</summary>
    [NotifyChanged([nameof(DueFormatted)])]
    DateTime oneShotDue { get; set; } = DateTime.Today.AddMinutes(30);

    /// <summary>Optional lower bound (local date, inclusive) for <see cref="TriggerKind.Cron"/> triggers.</summary>
    [NotifyChanged]
    DateTime? startDate { get; set; } = null;
    /// <summary>Optional upper bound (local date, inclusive) for <see cref="TriggerKind.Cron"/> triggers.</summary>
    [NotifyChanged]
    DateTime? endDate { get; set; } = null;

    // ---- runtime state, owned by the ScheduleEngine, persisted ----
    /// <summary>Next armed fire instant in UTC, or the original due time of a missed run.</summary>
    [NotifyChanged([nameof(NextRunFormatted)])]
    DateTime? nextRun { get; set; } = null;
    /// <summary>Local wall-clock time of the last successful fire.</summary>
    [NotifyChanged([nameof(LastRunFormatted)])]
    DateTime? lastRun { get; set; } = null;
    [NotifyChanged([nameof(StatusBrush), nameof(StateBrush)])]
    ScheduleStatus status { get; set; } = ScheduleStatus.Disabled;
    /// <summary>Human-readable reason for <see cref="ScheduleStatus.Invalid"/>/<see cref="ScheduleStatus.Disabled"/> states.</summary>
    [NotifyChanged]
    string? statusReason { get; set; } = null;

    public Schedule() { }

    public Schedule(string name, string command, TriggerKind trigger = TriggerKind.Once)
    {
        Name = name;
        Command = command;
        Trigger = trigger;
    }

    // ---- display helpers ----

    /// <summary>MudBlazor chip color for the current state.</summary>
    [JsonIgnore]
    public Color StateBrush => StatusBrush;
    [JsonIgnore]
    public Color StatusBrush => !Enabled ? Color.Error : Status switch
    {
        ScheduleStatus.Armed => Color.Success,
        ScheduleStatus.Completed => Color.Info,
        ScheduleStatus.Missed => Color.Warning,
        ScheduleStatus.Invalid => Color.Error,
        _ => Color.Default,
    };
    [JsonIgnore]
    public string StatusLabel => !Enabled ? "Disabled" : Status switch
    {
        ScheduleStatus.Armed => $"Runs {NextRunFormatted}",
        ScheduleStatus.Completed => "Completed",
        ScheduleStatus.Missed => $"Missed {NextRunFormatted}",
        ScheduleStatus.Invalid => "Invalid",
        ScheduleStatus.Disabled => string.IsNullOrWhiteSpace(StatusReason) ? "Disabled" : StatusReason,
        _ => Status.ToString(),
    };
    [JsonIgnore]
    public string CronFormatted => Cron;
    [JsonIgnore]
    public string DueFormatted => OneShotDue.ToString("g");
    [JsonIgnore]
    public string NextRunFormatted => NextRun is null ? "—" : TimeZoneInfo.ConvertTimeFromUtc(NextRun.Value, TimeZoneInfo.Local).ToString("g");
    [JsonIgnore]
    public string LastRunFormatted => "Last execution: " + (LastRun?.ToString("g") ?? "never");
    [JsonIgnore]
    public string WindowFormatted => $"{StartDate?.ToString("d") ?? "now"} → {EndDate?.ToString("d") ?? "forever"}";
}
