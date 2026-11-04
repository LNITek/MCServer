using System.Reflection;
using CommandLine;
using Serilog;
using MCServer.Plugins;
using Newtonsoft.Json.Linq;

namespace MCServer.Cli;

/// <summary>
/// Entry point for one-off command-line invocations. Returns true when the
/// args were handled by the CLI and the web host must not be built.
/// </summary>
public static class CliDispatcher
{
    private static readonly HashSet<string> KnownOptions = BuildKnownOptions();

    private static readonly HashSet<string> GenericHostOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "urls", "environment", "configuration", "contentroot", "pathbase",
        "applicationname", "shutdowntimeout", "detailedsanitization",
    };

    public static async Task<bool> TryHandleAsync(string[] args)
    {
        if (args.Length == 0)
            return false;

        if (IsHelpRequest(args))
        {
            CliHelp.Print();
            Environment.Exit(0);
            return true;
        }

        if (IsVersionRequest(args))
        {
            PrintVersion();
            Environment.Exit(0);
            return true;
        }

        if (!HasCliOption(args))
        {
            if (UsesOnlyGenericHostOptions(args))
                return false;

            Console.Error.WriteLine("Unknown command or option. Run 'MCServer --help' for the list of commands.");
            Environment.Exit(2);
            return true;
        }

        var parser = new Parser(settings =>
        {
            settings.CaseInsensitiveEnumValues = true;
            settings.IgnoreUnknownArguments = false;
            settings.HelpWriter = null; // We print our own help.
        });

        var parsed = parser.ParseArguments<CliOptions>(args);
        if (parsed.Errors.Any(IsUnknownOption))
        {
            // A known MCServer flag was combined with a host flag; the host
            // cannot both run a one-shot command and start the web server.
            Console.Error.WriteLine("Unknown host option. Run 'MCServer --help' for valid commands.");
            foreach (var error in parsed.Errors.Where(IsUnknownOption))
                Console.Error.WriteLine($"  {error}");
            Environment.Exit(2);
            return true;
        }

        if (parsed.Errors.Any())
        {
            foreach (var error in parsed.Errors)
                Console.Error.WriteLine($"  {error}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Run 'MCServer --help' for usage.");
            Environment.Exit(2);
            return true;
        }

        var options = ((Parsed<CliOptions>)parsed).Value;

        try
        {
            var exitCode = await DispatchAsync(options, args);
            Environment.Exit(exitCode);
        }
        catch (CliUsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.Exit(2);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MCServer failed: {ex.Message}");
            Log.Error(ex, "CLI command failed");
            Environment.Exit(1);
            return true;
        }

        return true;
    }

    private static async Task<int> DispatchAsync(CliOptions o, string[] args)
    {
        var selected = SelectedCommands(o);
        if (selected.Count > 1)
            return Usage($"Only one command can be used at a time. Found: {string.Join(", ", selected)}.");

        var cmdArgs = o.Arguments?.ToList() ?? [];
        var json = o.Json;

        // ---- help / version --------------------------------------------
        if (o.Help) { CliHelp.Print(); return 0; }
        if (o.Version) { PrintVersion(); return 0; }

        // ---- compatibility ------------------------------------------------
        if (o.ResetAdmin) return Program.ResetAdminViaCli();

        // ---- paths --------------------------------------------------------
        if (o.PathData || o.DataDir || o.PathServers || o.PathPlugins || o.PathLogs || o.PathSettings)
        {
            CliInfoHandlers.PrintPaths(o);
            return 0;
        }

        if (o.ShowSettings) { Console.WriteLine(CliInfoHandlers.MaskedSettings()); return 0; }
        if (o.EditSettings) return EditSettings();
        if (o.Users) { CliInfoHandlers.PrintUsers(json); return 0; }
        if (o.Health) return CliInfoHandlers.Health();
        if (o.Plugins) { CliInfoHandlers.PrintPlugins(json); return 0; }
        if (o.Logs)
        {
            int? count = null;
            if (cmdArgs.Count > 0)
            {
                if (!int.TryParse(cmdArgs[0], out var parsed) || parsed <= 0)
                    return Usage("--logs expects a positive line count, e.g. --logs 100");
                count = parsed;
            }

            if (cmdArgs.Count > 1) return Usage("--logs accepts at most one line count.");
            return CliInfoHandlers.PrintLogTail(count);
        }

        // ---- discovery / inspection ----------------------------------------
        if (o.ListServers || o.Servers) { CliInfoHandlers.PrintServerList(json); return 0; }

        if (o.ServerStatus || o.Status)
        {
            RequireArgCount(cmdArgs, 0, 1, "--server-status [name]");
            var name = cmdArgs.FirstOrDefault();

            // Live host? Forward over the pipe; otherwise explain the values are unknown.
            var live = await CliLiveCommands.StatusAsync(name, json, CancellationToken.None);
            return live; // StatusAsync handles both cases internally.
        }

        if (o.ServerPath)
        {
            RequireArgCount(cmdArgs, 1, 1, "--server-path <server>");
            return CliInfoHandlers.PrintServerPath(cmdArgs[0]);
        }

        if (o.ServerConfig)
        {
            RequireArgCount(cmdArgs, 1, 1, "--server-config <server> [--file name]");
            return CliInfoHandlers.PrintServerConfig(cmdArgs[0], show: false, o.File);
        }

        if (o.ShowConfig)
        {
            RequireArgCount(cmdArgs, 1, 1, "--show-config <server> [--file name]");
            return CliInfoHandlers.PrintServerConfig(cmdArgs[0], show: true, o.File);
        }

        if (o.Doc)
        {
            RequireArgCount(cmdArgs, 1, 2, "--doc <server> [slug]");
            return CliInfoHandlers.PrintDoc(cmdArgs[0], cmdArgs.Count > 1 ? cmdArgs[1] : null);
        }

        if (o.ListFiles)
        {
            RequireArgCount(cmdArgs, 1, 2, "--list-files <server> [pattern]");
            return CliInfoHandlers.ListServerFiles(cmdArgs[0], cmdArgs.Count > 1 ? cmdArgs[1] : null);
        }

        if (o.Schedules)
        {
            RequireArgCount(cmdArgs, 0, 1, "--schedules [server]");
            return CliInfoHandlers.PrintSchedules(cmdArgs.FirstOrDefault(), json);
        }

        // ---- live server actions ------------------------------------------
        if (o.Run)
        {
            RequireArgCount(cmdArgs, 2, int.MaxValue, "--run <server> <command...>");
            return await CliLiveCommands.RunAsync(o, cmdArgs[0], cmdArgs.Skip(1).ToArray(), CancellationToken.None);
        }

        if (o.Exec)
        {
            RequireArgCount(cmdArgs, 2, int.MaxValue, "--exec <server> <command...>");
            return await CliLiveCommands.ExecAsync(o, cmdArgs[0], cmdArgs.Skip(1).ToArray(), CancellationToken.None);
        }

        if (o.Start) { RequireArgCount(cmdArgs, 1, 1, "--start <server>"); return await CliLiveCommands.StartAsync(cmdArgs[0], CancellationToken.None); }
        if (o.Stop) { RequireArgCount(cmdArgs, 1, 1, "--stop <server>"); return await CliLiveCommands.StopAsync(cmdArgs[0], CancellationToken.None); }
        if (o.Restart) { RequireArgCount(cmdArgs, 1, 1, "--restart <server>"); return await CliLiveCommands.RestartAsync(cmdArgs[0], CancellationToken.None); }

        // ---- create / delete ----------------------------------------------
        if (o.CreateServer) return await CliLiveCommands.CreateServerAsync(o, CancellationToken.None);

        if (o.DeleteServer)
        {
            RequireArgCount(cmdArgs, 1, 1, "--delete-server <server>");
            return await CliLiveCommands.DeleteServerAsync(o, cmdArgs[0], CancellationToken.None);
        }

        return Usage("No command was provided. Run 'MCServer --help' for usage.");
    }

    private static void PrintVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        Console.WriteLine(version);
    }

    private static int EditSettings()
    {
        var file = Program.SettingsFilePath;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (!File.Exists(file)) File.WriteAllText(file, "{\n  \"AppSettings\": {\n    \"ServerSettings\": [],\n    \"Users\": []\n  }\n}\n");

        var editor = Environment.GetEnvironmentVariable("MCS_EDITOR")
                     ?? Environment.GetEnvironmentVariable("VISUAL")
                     ?? Environment.GetEnvironmentVariable("EDITOR")
                     ?? (OperatingSystem.IsWindows() ? "notepad" : "nano");

        var args = SplitCommandLine(editor).Concat([file]).ToList();
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = args[0],
                UseShellExecute = false,
            };
            foreach (var a in args.Skip(1)) psi.ArgumentList.Add(a);

            using var process = System.Diagnostics.Process.Start(psi)
                              ?? throw new InvalidOperationException("Failed to start the editor.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"Failed to start '{editor}'. Open this path manually: {file}");
            return 1;
        }
    }

    private static List<string> SplitCommandLine(string commandLine)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;

        foreach (char c in commandLine)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }

        if (current.Length > 0) parts.Add(current.ToString());
        return parts.Count > 0 ? parts : [commandLine];
    }

    private static void RequireArgCount(IReadOnlyList<string> args, int min, int max, string usage)
    {
        if (args.Count < min || args.Count > max)
            throw new CliUsageException($"Usage: {usage}");
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run 'MCServer --help' for usage.");
        return 2;
    }

    private static List<string> SelectedCommands(CliOptions o)
    {
        var selected = new List<string>();
        void Add(bool flag, string name) { if (flag) selected.Add(name); }

        Add(o.ResetAdmin, "--reset-admin");
        Add(o.PathData || o.DataDir, "--path-data");
        Add(o.PathServers, "--path-servers");
        Add(o.PathPlugins, "--path-plugins");
        Add(o.PathLogs, "--path-logs");
        Add(o.PathSettings, "--path-settings");
        Add(o.ShowSettings, "--show-settings");
        Add(o.EditSettings, "--edit-settings");
        Add(o.Users, "--users");
        Add(o.Health, "--health");
        Add(o.Plugins, "--plugins");
        Add(o.Logs, "--logs");
        Add(o.ListServers || o.Servers, "--list-servers");
        Add(o.ServerStatus || o.Status, "--server-status");
        Add(o.ServerPath, "--server-path");
        Add(o.ServerConfig, "--server-config");
        Add(o.ShowConfig, "--show-config");
        Add(o.Doc, "--doc");
        Add(o.ListFiles, "--list-files");
        Add(o.Schedules, "--schedules");
        Add(o.Run, "--run");
        Add(o.Exec, "--exec");
        Add(o.Start, "--start");
        Add(o.Stop, "--stop");
        Add(o.Restart, "--restart");
        Add(o.CreateServer, "--create-server");
        Add(o.DeleteServer, "--delete-server");

        return selected;
    }

    private static HashSet<string> BuildKnownOptions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in typeof(CliOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var attribute in property.GetCustomAttributes<OptionAttribute>())
            {
                if (!string.IsNullOrWhiteSpace(attribute.LongName))
                    set.Add(attribute.LongName);
                if (!string.IsNullOrWhiteSpace(attribute.ShortName))
                    set.Add(attribute.ShortName);
            }
        }

        return set;
    }

    private static bool HasCliOption(string[] args)
    {
        foreach (var token in args)
        {
            if (TrySplitOption(token, out var key) && KnownOptions.Contains(key))
                return true;
        }

        return false;
    }

    private static bool IsVersionRequest(string[] args) =>
        args.Any(a => string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase));

    private static bool IsHelpRequest(string[] args) =>
        args.Any(a =>
            string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "help", StringComparison.OrdinalIgnoreCase));

    private static bool UsesOnlyGenericHostOptions(string[] args)
    {
        bool expectValue = false;

        foreach (var token in args)
        {
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var key = token[2..].Split('=', 2)[0].Trim();
                if (!IsGenericHostOption(key))
                    return false;

                // '--urls value' consumes the next token; '--urls=value' does not.
                expectValue = !token.Contains('=');
                continue;
            }

            if (expectValue)
            {
                expectValue = false;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsGenericHostOption(string key)
    {
        if (GenericHostOptions.Contains(key))
            return true;

        // App configuration supplied as dotted or double-underscore keys.
        return key.Contains(':', StringComparison.Ordinal) ||
               key.Contains("__", StringComparison.Ordinal) ||
               key.Contains('.', StringComparison.Ordinal);
    }

    private static bool TrySplitOption(string token, out string key)
    {
        key = "";

        if (token.StartsWith("--", StringComparison.Ordinal))
        {
            key = token[2..].Split('=', 2)[0].Trim();
            return key.Length > 0;
        }

        if (token.StartsWith('-') && token.Length > 1)
        {
            key = token[1..].Split('=', 2)[0].Trim();
            return key.Length > 0;
        }

        return false;
    }

    private static bool IsUnknownOption(Error error) =>
        error is UnknownOptionError or MissingValueOptionError;
}
