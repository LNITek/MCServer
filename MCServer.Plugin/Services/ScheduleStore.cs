using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServer.Plugins;

/// <summary>Outcome of <see cref="ScheduleStore.LoadSchedules"/>.</summary>
public enum ScheduleLoadError
{
    None,
    /// <summary>The file exists but could not be parsed. A `*.corrupt.bak` copy was kept.</summary>
    CorruptFile,
}

/// <summary>
/// Load/save for a server's schedules.json. Atomic writes and corrupt-file
/// recovery only; arming/runtime state lives in <see cref="ScheduleEngine"/>.
/// File layout (v2): {"Version":2,"Schedules":[…]} — the v1 layout was a bare
/// array of scheduling modes Once/Range/Indefinite and is migrated on load.
/// </summary>
public static class ScheduleStore
{
    public const int Version = 2;

    public static string SchedulesPath(this IGameServer server) =>
        Path.Combine(server.ServerPath, "schedules.json");

    /// <summary>
    /// Loads schedules, migrating the v1 schema when found. Never throws:
    /// unreadable files are preserved as `schedules.json.corrupt.bak` and the
    /// result is empty, so a later save cannot destroy the original data.
    /// </summary>
    public static List<Schedule> LoadSchedules(this IGameServer server, out ScheduleLoadError error)
    {
        error = ScheduleLoadError.None;
        var path = server.SchedulesPath();
        if (!File.Exists(path)) return [];

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(reader);
            var json = JToken.ReadFrom(jsonReader);

            // v1: a bare array of the old Once/Range/Indefinite model.
            if (json is JArray legacy)
                return [.. ReadLegacy(legacy)];

            if (json["Version"]?.Value<int>() is not 2)
                return [];

            var schedules = json["Schedules"]?.ToObject<List<Schedule>>(JsonSerializer.Create(JsonOptions.SerializerSettings));
            return schedules ?? [];
        }
        catch
        {
            BackupCorruptFile(path);
            error = ScheduleLoadError.CorruptFile;
            return [];
        }
    }

    /// <summary>Atomically writes the schedules. Write-to-temp + rename, so a
    /// crash mid-save can never leave a torn file behind.</summary>
    public static void SaveSchedules(this IGameServer server, IEnumerable<Schedule> schedules)
    {
        var path = server.SchedulesPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonConvert.SerializeObject(
            new StoreFile(Version, [.. schedules]),
            JsonOptions.SerializerSettings);

        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    private record StoreFile(int Version, List<Schedule> Schedules);

    private static void BackupCorruptFile(string path)
    {
        try { File.Copy(path, path + ".corrupt.bak", overwrite: true); }
        catch { /* nothing more we can do; load just returns empty. */ }
    }

    // ---- v1 migration ----

    private enum LegacyMode { Once = 0, Range = 1, Indefinite = 2 }

    /// <summary>Old v1 row shape. `Mode` accepts both the numeric form Newtonsoft
    /// produces by default and the enum-name form.</summary>
    private sealed class LegacySchedule
    {
        public string ID { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        [JsonConverter(typeof(FlexibleModeConverter))]
        public LegacyMode Mode { get; set; }
        public string Command { get; set; } = string.Empty;
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today;
        public string? Cron { get; set; }
        public TimeSpan Time { get; set; }
        public DateTime? LastRun { get; set; }

        public Schedule ToSchedule()
        {
            var schedule = new Schedule
            {
                ID = ID,
                Name = Name,
                Description = Description,
                Command = Command,
                Enabled = Enabled,
                LastRun = LastRun,
            };

            switch (Mode)
            {
                case LegacyMode.Once:
                    schedule.Trigger = TriggerKind.Once;
                    // +Days guards timespans that were entered beyond 24h.
                    schedule.OneShotDue = StartDate.Date + Time;
                    break;

                case LegacyMode.Indefinite:
                    schedule.Trigger = TriggerKind.Cron;
                    schedule.Cron = NormalizeCron(Cron);
                    schedule.StartDate = (StartDate == default ? null : StartDate);
                    break;

                case LegacyMode.Range:
                    schedule.Trigger = TriggerKind.Cron;
                    schedule.Cron = NormalizeCron(Cron);
                    schedule.StartDate = (StartDate == default ? null : StartDate);
                    schedule.EndDate = (EndDate == default ? null : EndDate);
                    break;
            }

            return schedule;
        }
    }

    private static string NormalizeCron(string? cron) =>
        string.IsNullOrWhiteSpace(cron) || !Cronos.CronExpression.TryParse(cron, out _)
            ? "0 12 * * *"
            : cron;

    private static IEnumerable<Schedule> ReadLegacy(JArray array)
    {
        var serializer = JsonSerializer.Create(JsonOptions.SerializerSettings);
        foreach (var item in array.Select(entry => entry.ToObject<LegacySchedule>(serializer)))
            if (item is not null) yield return item.ToSchedule();
    }

    /// <summary>Accepts v1 enum serialization as number or name.</summary>
    private sealed class FlexibleModeConverter : JsonConverter<LegacyMode>
    {
        public override LegacyMode ReadJson(JsonReader reader, Type objectType, LegacyMode existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            // Json.NET produces integer tokens as long, not int.
            if (reader.Value is long or int)
                return (LegacyMode)Convert.ToInt64(reader.Value);
            if (reader.Value is string text && Enum.TryParse<LegacyMode>(text, ignoreCase: true, out var mode)) return mode;
            return LegacyMode.Once;
        }
        public override void WriteJson(JsonWriter writer, LegacyMode value, JsonSerializer serializer)
            => writer.WriteValue((int)value);
    }
}
