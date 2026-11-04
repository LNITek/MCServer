using System.Text.RegularExpressions;
using MCServer.BDS;
using MCServer.Plugins;
using Newtonsoft.Json.Linq;

namespace MCServer.Cli;

/// <summary>Read-only / file-backed CLI commands.</summary>
public static class CliInfoHandlers
{
    public static void PrintPaths(CliOptions o)
    {
        if (o.PathData || o.DataDir) Console.WriteLine(Program.RootPath);
        else if (o.PathServers) Console.WriteLine(Program.ServerPath);
        else if (o.PathPlugins) Console.WriteLine(Program.PluginsPath);
        else if (o.PathLogs) Console.WriteLine(Program.LogsPath);
        else if (o.PathSettings) Console.WriteLine(Program.SettingsFilePath);
        else throw new CliUsageException("No path option was selected.");
    }

    public static string? MaskedSettings()
    {
        var root = CliOutput.LoadSettingsRoot();
        return CliOutput.MaskSettings(root).ToString(Newtonsoft.Json.Formatting.Indented);
    }

    public static int Health()
    {
        var checks = new List<(string Name, bool Ok, string Detail)>
        {
            WritableDirectoryCheck("Data root", Program.RootPath),
            WritableDirectoryCheck("Servers", Program.ServerPath),
            WritableDirectoryCheck("Plugins", Program.PluginsPath),
            WritableDirectoryCheck("Logs", Program.LogsPath),
        };

        var settings = CheckSettingsFile();
        checks.Add(settings);

        foreach (var server in CliOutput.ReadServerEntries())
        {
            bool exists = Directory.Exists(server.Path);
            checks.Add(($"Server folder '{server.Name}'", exists,
                exists ? server.Path : $"Missing: {server.Path}"));
        }

        bool allOk = checks.All(c => c.Ok);

        Console.WriteLine(allOk
            ? "MCServer health check passed."
            : "MCServer health check found problems.");

        foreach (var (name, ok, detail) in checks)
            Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name,-32} {detail}");

        return allOk ? 0 : 1;
    }

    public static void PrintPlugins(bool json)
    {
        var plugin = new BedrockPlugin();
        var plugins = new List<PluginLine>
        {
            new("Bedrock Dedicated Server", plugin.ServerType, plugin.Version, true, "(built-in)"),
        };

        if (Directory.Exists(Program.PluginsPath))
        {
            foreach (var file in Directory.EnumerateFiles(Program.PluginsPath, "*.dll", SearchOption.AllDirectories))
            {
                plugins.Add(new PluginLine(
                    Path.GetFileNameWithoutExtension(file),
                    "(external)",
                    "(unknown; restart to load)",
                    false,
                    Path.GetFullPath(file)));
            }
        }

        if (json)
        {
            CliOutput.PrintJson(plugins.Select(p => new
            {
                name = p.Name,
                type = p.Type,
                version = p.Version,
                builtIn = p.BuiltIn,
                path = p.Path,
            }));
            return;
        }

        CliOutput.PrintTable(
            ["Name", "Type", "Version", "Built-in", "Path"],
            plugins.Select(p => (IReadOnlyList<string>)[p.Name, p.Type, p.Version, p.BuiltIn ? "yes" : "no", p.Path]).ToList());
    }

    public static int PrintLogTail(int? lineCount)
    {
        if (!Directory.Exists(Program.LogsPath))
        {
            Console.Error.WriteLine($"No logs folder found at '{Program.LogsPath}'.");
            return 1;
        }

        var file = Directory.EnumerateFiles(Program.LogsPath, "mcserver-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (file is null)
        {
            Console.Error.WriteLine($"No MCServer log files found in '{Program.LogsPath}'.");
            return 1;
        }

        int count = lineCount ?? 50;
        if (count <= 0) count = 50;

        Console.WriteLine($"Log: {file}");
        Console.WriteLine();

        var lines = new Queue<string>(count + 1);
        using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            lines.Enqueue(line);
            if (lines.Count > count) lines.Dequeue();
        }

        foreach (var line in lines)
            Console.WriteLine(line);

        return 0;
    }

    public static void PrintUsers(bool json)
    {
        var root = CliOutput.LoadSettingsRoot();
        var users = CliOutput.GetSection(root)["Users"] as JArray;
        var rows = users?.OfType<JObject>()
            .Select(u => new
            {
                username = u["Username"]?.Value<string>() ?? "(unnamed)",
                role = u["Role"]?.Value<string>() ?? "Unknown",
            })
            .ToList() ?? [];

        if (json)
        {
            CliOutput.PrintJson(rows);
            return;
        }

        if (rows.Count == 0)
        {
            Console.WriteLine("No users configured.");
            return;
        }

        CliOutput.PrintTable(
            ["Username", "Role"],
            rows.Select(u => (IReadOnlyList<string>)[u.username, u.role]).ToList());
    }

    public static void PrintServerList(bool json)
    {
        var entries = CliOutput.ReadServerEntries();
        if (json)
        {
            CliOutput.PrintJson(entries.Select(e => new
            {
                e.Id,
                e.Name,
                e.Type,
                e.Version,
                e.AutoStart,
                e.Preview,
                e.Path,
                folderExists = Directory.Exists(e.Path),
            }));
            return;
        }

        if (entries.Count == 0)
        {
            Console.WriteLine("No servers configured. Use --create-server --name \"My World\" to add one.");
            return;
        }

        CliOutput.PrintTable(
            ["Name", "ID", "Type", "Version", "Auto-start", "Folder", "Path"],
            entries.Select(e =>
            {
                var state = Directory.Exists(e.Path) ? "OK" : "MISSING";
                return (IReadOnlyList<string>)[e.Name, e.Id, e.Type, e.Version, e.AutoStart ? "yes" : "no", state, e.Path];
            }).ToList());
    }

    public static int PrintServerStatusUnknown(string? serverName, bool json)
    {
        var selected = SelectServers(serverName);
        if (selected is null) return 1;

        if (json)
        {
            CliOutput.PrintJson(selected.Select(e => new
            {
                e.Name,
                e.Id,
                e.Type,
                running = false,
                status = "Unknown (MCServer host not running)",
                e.Path,
            }));
            return 0;
        }

        Console.WriteLine("MCServer host is not running; status is unknown.");
        CliOutput.PrintTable(
            ["Name", "ID", "Type", "Running", "Status", "Path"],
            selected.Select(e => (IReadOnlyList<string>)[
                e.Name, e.Id, e.Type, "?", "Unknown (host not running)", e.Path,
            ]).ToList());

        return 0;
    }

    public static int PrintServerPath(string? serverName)
    {
        var found = FindRequiredServer(serverName);
        if (found is null) return 1;
        Console.WriteLine(found.Path);
        return 0;
    }

    public static int PrintServerConfig(string? serverName, bool show, string? fileName)
    {
        var found = FindRequiredServer(serverName);
        if (found is null) return 1;

        var target = ResolveServerFile(found, fileName ?? "server.properties");
        if (target is null) return 1;

        if (!show)
        {
            Console.WriteLine(target);
            return 0;
        }

        if (!File.Exists(target))
        {
            Console.Error.WriteLine($"Config file not found: {target}");
            return 1;
        }

        var text = File.ReadAllText(target);
        Console.Write(text);
        if (!text.EndsWith('\n'))
            Console.WriteLine();
        return 0;
    }

    public static int ListServerFiles(string? serverName, string? pattern)
    {
        var found = FindRequiredServer(serverName);
        if (found is null) return 1;

        if (!Directory.Exists(found.Path))
        {
            Console.Error.WriteLine($"Server folder does not exist: {found.Path}");
            return 1;
        }

        var entries = Directory.EnumerateFileSystemEntries(found.Path, "*", SearchOption.AllDirectories)
            .Select(path => (full: path, relative: Path.GetRelativePath(found.Path, path)))
            .Where(x => MatchesWildcard(Path.GetFileName(x.relative), pattern))
            .OrderBy(x => x.relative, StringComparer.OrdinalIgnoreCase)
            .ToList();

        const int limit = 200;
        foreach (var (full, relative) in entries.Take(limit))
        {
            var kind = Directory.Exists(full) ? "D" : "F";
            var size = Directory.Exists(full) ? "" : FileLength(full);
            Console.WriteLine($"{kind} {size,10} {relative}");
        }

        if (entries.Count > limit)
            Console.WriteLine($"... and {entries.Count - limit} more (showing first {limit}).");

        return 0;
    }

    public static int PrintDoc(string? serverName, string? slug)
    {
        var found = FindRequiredServer(serverName);
        if (found is null) return 1;

        var docs = DiscoverDocs(found);
        if (slug is null)
        {
            if (docs.Count == 0)
            {
                Console.WriteLine("No documents are registered for this server.");
                return 0;
            }

            CliOutput.PrintTable(
                ["Slug", "Title", "Path", "Exists"],
                docs.Select(d => (IReadOnlyList<string>)[d.Slug, d.Title, d.Path, File.Exists(d.Path) ? "yes" : "MISSING"]).ToList());
            return 0;
        }

        var doc = docs.FirstOrDefault(d => d.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (doc is null)
        {
            Console.Error.WriteLine($"No document '{slug}' registered for server '{found.Name}'. Run --doc {found.Name} to list available docs.");
            return 1;
        }

        if (!File.Exists(doc.Path))
        {
            Console.Error.WriteLine($"Document file missing: {doc.Path}");
            return 1;
        }

        var ext = Path.GetExtension(doc.Path).ToLowerInvariant();
        if (ext is ".html" or ".md" or ".txt" or "")
        {
            var text = File.ReadAllText(doc.Path);
            Console.Write(text);
            if (!text.EndsWith('\n'))
                Console.WriteLine();
            return 0;
        }

        Console.Error.WriteLine($"Unsupported document type '{ext}'. File: {doc.Path}");
        return 1;
    }

    public static int PrintSchedules(string? serverName, bool json)
    {
        var selected = SelectServers(serverName);
        if (selected is null) return 1;

        var output = new List<object>();
        foreach (var server in selected)
        {
            var path = Path.Combine(server.Path, "schedules.json");
            var schedules = ScheduleStore.LoadSchedulesFromFile(path, out var error);
            if (error is ScheduleLoadError.CorruptFile)
                Console.Error.WriteLine($"Warning: '{path}' could not be parsed. A backup was written next to it.");

            foreach (var schedule in schedules)
            {
                output.Add(new
                {
                    server = server.Name,
                    serverId = server.Id,
                    name = schedule.Name,
                    command = schedule.Command,
                    trigger = schedule.Trigger.ToString(),
                    cron = schedule.Trigger == TriggerKind.Cron ? schedule.Cron : null,
                    enabled = schedule.Enabled,
                    status = schedule.Status.ToString(),
                    reason = schedule.StatusReason,
                    lastRun = schedule.LastRun?.ToString("g"),
                    nextRun = schedule.NextRun is { } next
                        ? TimeZoneInfo.ConvertTimeFromUtc(next, TimeZoneInfo.Local).ToString("g")
                        : null,
                });
            }
        }

        if (json)
        {
            CliOutput.PrintJson(output);
            return 0;
        }

        var rows = output.Select(item =>
        {
            dynamic d = item;
            return (IReadOnlyList<string>)[
                (string)d.server,
                (string)d.name,
                (string)d.command,
                (string)d.trigger,
                (bool)d.enabled ? "yes" : "no",
                (string)d.status,
                (string?)d.lastRun ?? "never",
                (string?)d.nextRun ?? "—",
            ];
        }).ToList();

        if (rows.Count == 0)
        {
            Console.WriteLine("No schedules found.");
            return 0;
        }

        CliOutput.PrintTable(
            ["Server", "Name", "Command", "Trigger", "Enabled", "Status", "Last run", "Next run"],
            rows);
        Console.WriteLine();
        Console.WriteLine("Note: Last/Next run come from schedules.json; cron values are refreshed only while MCServer is running.");

        return 0;
    }

    // ---- helpers --------------------------------------------------------

    public static List<CliServerEntry>? SelectServers(string? name)
    {
        var entries = CliOutput.ReadServerEntries();
        if (string.IsNullOrWhiteSpace(name)) return entries;

        var found = CliOutput.FindServerEntry(name);
        if (found is null)
        {
            Console.Error.WriteLine($"Server '{name}' not found. Run --list-servers to see configured servers.");
            return null;
        }

        return [found];
    }

    public static CliServerEntry? FindRequiredServer(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("A server name or ID is required.");
            return null;
        }

        var found = CliOutput.FindServerEntry(name);
        if (found is null)
            Console.Error.WriteLine($"Server '{name}' not found. Run --list-servers to see configured servers.");

        return found;
    }

    public static string? ResolveServerFile(CliServerEntry server, string relative)
    {
        relative = (relative ?? string.Empty).Trim();
        if (relative.Length == 0)
        {
            Console.Error.WriteLine("A file name is required.");
            return null;
        }

        if (Path.IsPathRooted(relative))
        {
            Console.Error.WriteLine("--file must be relative to the server folder.");
            return null;
        }

        var root = Path.GetFullPath(server.Path);
        var target = Path.GetFullPath(Path.Combine(root, relative));
        var boundary = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!target.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("--file must stay inside the server folder.");
            return null;
        }

        return target;
    }

    private static (string Name, bool Ok, string Detail) CheckSettingsFile()
    {
        var file = Program.SettingsFilePath;
        if (!File.Exists(file))
            return ("settings.json", false, $"Missing: {file} (will be created on first run)");

        try
        {
            var root = JObject.Parse(File.ReadAllText(file));
            return ("settings.json", true, $"Parsed {root[AppSettings.SettingName]?["ServerSettings"]?.Children<JObject>().Count() ?? 0} server(s) from {file}");
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            return ("settings.json", false, $"Invalid: {ex.Message}");
        }
    }

    private static (string Name, bool Ok, string Detail) WritableDirectoryCheck(string name, string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            var temp = Path.Combine(path, $".health-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temp, "ok");
            File.Delete(temp);
            return (name, true, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return (name, false, $"Not writable or cannot be created: {path} ({ex.Message})");
        }
    }

    private static List<DocEntry> DiscoverDocs(CliServerEntry server)
    {
        var docs = new List<DocEntry>();

        if (IsBds(server.Type))
        {
            docs.Add(new("readme", "Readme", Path.Combine(AppContext.BaseDirectory, "Assets", "README.BDS.md")));
            docs.Add(new("notes", "How To", Path.Combine(server.Path, "bedrock_server_how_to.html")));
            docs.Add(new("releases", "Release Notes", Path.Combine(server.Path, "release-notes.txt")));
            return docs;
        }

        if (Directory.Exists(server.Path))
        {
            docs.AddRange(Directory.EnumerateFiles(server.Path, "*.md", SearchOption.TopDirectoryOnly)
                .Take(10)
                .Select(path => new DocEntry(Path.GetFileNameWithoutExtension(path), Path.GetFileName(path), path)));
        }

        return docs;
    }

    private static bool IsBds(string? type) =>
        CliServerCreation.IsBedrockType(type);

    private static bool MatchesWildcard(string name, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return true;

        var regex = Regex.Escape(pattern.Trim())
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");

        return Regex.IsMatch(name, "^" + regex + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string FileLength(string path)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024) + " KiB";
            return (bytes / (1024 * 1024)) + " MiB";
        }
        catch
        {
            return "?";
        }
    }

    private sealed record PluginLine(string Name, string Type, string Version, bool BuiltIn, string Path);

    private sealed record DocEntry(string Slug, string Title, string Path);
}
