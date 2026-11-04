using Newtonsoft.Json.Linq;

namespace MCServer.Cli;

/// <summary>Shared console formatting, masking and file helpers for the CLI.</summary>
public static class CliOutput
{
    // ---- settings-file handling -----------------------------------------

    public static JObject LoadSettingsRoot()
    {
        var file = Program.SettingsFilePath;
        if (!File.Exists(file))
            return new JObject();

        try
        {
            var text = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(text))
                return new JObject();
            return JObject.Parse(text) as JObject
                   ?? throw new InvalidDataException("The settings file does not contain a JSON object.");
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new CliUsageException($"MCServer settings could not be read ({ex.Message}). Fix or remove '{file}' and try again.");
        }
    }

    public static JObject GetSection(JObject root) =>
        root[AppSettings.SettingName] as JObject ?? new JObject();

    public static void SaveSettingsRoot(JObject root)
    {
        var file = Program.SettingsFilePath;
        var dir = Path.GetDirectoryName(file) ?? ".";
        Directory.CreateDirectory(dir);

        var temp = Path.Combine(dir, $".{Path.GetFileName(file)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temp, root.ToString(Newtonsoft.Json.Formatting.Indented));
        File.Move(temp, file, overwrite: true);
    }

    public static JObject MaskSettings(JObject root)
    {
        var clone = (JObject)root.DeepClone();
        var users = clone[AppSettings.SettingName]?["Users"] as JArray;
        if (users is null) return clone;

        foreach (var entry in users.OfType<JObject>())
        {
            if (entry["PasswordHash"] is not null)
                entry["PasswordHash"] = new JValue("********");
        }

        return clone;
    }

    public static List<CliServerEntry> ReadServerEntries()
    {
        var root = LoadSettingsRoot();
        var servers = GetSection(root)["ServerSettings"] as JArray;
        if (servers is null) return [];

        var results = new List<CliServerEntry>();
        foreach (var item in servers.OfType<JObject>())
        {
            var id = item["ID"]?.Value<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) continue;

            results.Add(new CliServerEntry(
                id,
                item["Name"]?.Value<string>() ?? string.Empty,
                item["Type"]?.Value<string>() ?? string.Empty,
                item["Path"]?.Value<string>() ?? string.Empty,
                item["Version"]?.Value<string>() ?? "0.0.0.0",
                item["Preview"]?.Value<bool>() ?? false,
                item["AutoStart"]?.Value<bool>() ?? false,
                ResolveServerPath(item["Path"]?.Value<string>() ?? string.Empty, id)));
        }

        return results;
    }

    public static CliServerEntry? FindServerEntry(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        var needle = idOrName.Trim();

        return ReadServerEntries().FirstOrDefault(e =>
            string.Equals(e.Id, needle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Name, needle, StringComparison.OrdinalIgnoreCase));
    }

    public static string ResolveServerPath(string configuredPath, string id)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(Path.Combine(Program.ServerPath, id));

        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        // Existing settings may hold paths relative to the old project directory
        // (usually "./../Data/..."). Resolve relative paths against the data root.
        return Path.GetFullPath(Path.Combine(Program.RootPath, configuredPath));
    }

    public static void RemoveServerEntry(string id)
    {
        var root = LoadSettingsRoot();
        var section = GetSection(root);
        var servers = section["ServerSettings"] as JArray;
        if (servers is null) return;

        var kept = new JArray(servers.Where(x =>
            !string.Equals(x?["ID"]?.Value<string>(), id, StringComparison.OrdinalIgnoreCase)));

        section["ServerSettings"] = kept;
        root[AppSettings.SettingName] = section;
        SaveSettingsRoot(root);
    }

    public static void AddServerEntry(JObject entry)
    {
        var root = LoadSettingsRoot();
        var section = GetSection(root);
        var servers = section["ServerSettings"] as JArray ?? new JArray();
        servers.Add(entry);
        section["ServerSettings"] = servers;
        root[AppSettings.SettingName] = section;
        SaveSettingsRoot(root);
    }

    // ---- output helpers -------------------------------------------------

    public static void PrintTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (headers.Count == 0) return;

        var widths = new int[headers.Count];
        for (int i = 0; i < headers.Count; i++)
            widths[i] = headers[i].Length;

        foreach (var row in rows)
        {
            for (int i = 0; i < row.Count && i < widths.Length; i++)
                widths[i] = Math.Max(widths[i], row[i]?.Length ?? 0);
        }

        Console.WriteLine(JoinRow(headers, widths));
        Console.WriteLine(string.Join("-+-", widths.Select(w => new string('-', w))));

        foreach (var row in rows)
            Console.WriteLine(JoinRow(row, widths));
    }

    public static void PrintJson(object value)
    {
        Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
            value, Newtonsoft.Json.Formatting.Indented,
            new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Include }));
    }

    public static string? MaskToken(string? value) =>
        string.IsNullOrEmpty(value) ? value : "********";

    private static string JoinRow(IReadOnlyList<string> row, int[] widths) =>
        string.Join(" | ", row.Select((cell, i) => (cell ?? string.Empty).PadRight(widths[i])));
}

/// <summary>Summary of a server stored in settings.json.</summary>
public sealed record CliServerEntry(
    string Id,
    string Name,
    string Type,
    string ConfiguredPath,
    string Version,
    bool Preview,
    bool AutoStart,
    string Path);

/// <summary>Usage error that should exit with code 2.</summary>
public sealed class CliUsageException : Exception
{
    public CliUsageException(string message) : base(message) { }
}
