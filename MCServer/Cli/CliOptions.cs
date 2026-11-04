using CommandLine;

namespace MCServer.Cli;

/// <summary>
/// All command-line options MCServer understands before the web host is
/// started. Unknown ASP.NET Core host arguments are passed through untouched.
/// </summary>
public sealed class CliOptions
{
    // ---- help / version -------------------------------------------------
    [CliSection("Help", 0)]
    [Option('h', "help", HelpText = "Show this help. Alias: -h, help")]
    public bool Help { get; set; }

    [CliSection("Help", 0)]
    [Option("version", HelpText = "Print the MCServer version and exit.")]
    public bool Version { get; set; }

    // ---- compatibility ---------------------------------------------------
    [CliSection("Compatibility", 5)]
    [Option("reset-admin", HelpText = "Reset the Admin login to Admin/Admin.")]
    public bool ResetAdmin { get; set; }

    // ---- paths & diagnostics --------------------------------------------
    [CliSection("Diagnostics", 10)]
    [Option("path-data", HelpText = "Print the data/root folder and exit.")]
    public bool PathData { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("data-dir", Hidden = true, HelpText = "Alias for --path-data.")]
    public bool DataDir { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("path-servers", HelpText = "Print the servers folder and exit.")]
    public bool PathServers { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("path-plugins", HelpText = "Print the plugins folder and exit.")]
    public bool PathPlugins { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("path-logs", HelpText = "Print the logs folder and exit.")]
    public bool PathLogs { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("path-settings", HelpText = "Print the path to settings.json and exit.")]
    public bool PathSettings { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("show-settings", HelpText = "Print settings.json with password hashes masked.")]
    public bool ShowSettings { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("edit-settings", HelpText = "Open settings.json in $EDITOR (falls back to nano/notepad).")]
    public bool EditSettings { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("users", HelpText = "List configured users (no password hashes).")]
    public bool Users { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("health", HelpText = "Sanity-check folders, settings.json and write access.")]
    public bool Health { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("plugins", HelpText = "List built-in and external plugins.")]
    public bool Plugins { get; set; }

    [CliSection("Diagnostics", 10)]
    [Option("logs", HelpText = "Print today's MCServer log. Usage: --logs [count]")]
    public bool Logs { get; set; }

    // ---- discovery & status ----------------------------------------------
    [CliSection("Discovery", 20)]
    [Option("list-servers", HelpText = "List all configured servers and their folders.")]
    public bool ListServers { get; set; }

    [CliSection("Discovery", 20)]
    [Option("servers", Hidden = true, HelpText = "Alias for --list-servers.")]
    public bool Servers { get; set; }

    [CliSection("Discovery", 20)]
    [Option("server-status", HelpText = "Show server status. Usage: --server-status [name]")]
    public bool ServerStatus { get; set; }

    [CliSection("Discovery", 20)]
    [Option("status", Hidden = true, HelpText = "Alias for --server-status.")]
    public bool Status { get; set; }

    // ---- per-server inspection -------------------------------------------
    [CliSection("Inspection", 30)]
    [Option("server-path", HelpText = "Print a server's folder path. Usage: --server-path <server>")]
    public bool ServerPath { get; set; }

    [CliSection("Inspection", 30)]
    [Option("server-config", HelpText = "Print the path to a server config. Usage: --server-config <server> [--file name]")]
    public bool ServerConfig { get; set; }

    [CliSection("Inspection", 30)]
    [Option("file", Hidden = true, HelpText = "Config file name relative to the server folder (used with --server-config).")]
    public string? File { get; set; }

    [CliSection("Inspection", 30)]
    [Option("show-config", HelpText = "Print a server config. Usage: --show-config <server> [--file name]")]
    public bool ShowConfig { get; set; }

    [CliSection("Inspection", 30)]
    [Option("doc", HelpText = "List or print a server document. Usage: --doc <server> [readme|notes|releases]")]
    public bool Doc { get; set; }

    [CliSection("Inspection", 30)]
    [Option("list-files", HelpText = "List a server's files. Usage: --list-files <server> [pattern]")]
    public bool ListFiles { get; set; }

    [CliSection("Inspection", 30)]
    [Option("schedules", HelpText = "Show schedules. Usage: --schedules [server]")]
    public bool Schedules { get; set; }

    // ---- running servers ---------------------------------------------------
    [CliSection("Running servers", 40)]
    [Option("run", HelpText = "Send a command to a running server. Usage: --run <server> <command...>")]
    public bool Run { get; set; }

    [CliSection("Running servers", 40)]
    [Option("exec", HelpText = "Run an MCServer command. Usage: --exec <server> <command...>")]
    public bool Exec { get; set; }

    [CliSection("Running servers", 40)]
    [Option("start", HelpText = "Start a running host's server. Usage: --start <server>")]
    public bool Start { get; set; }

    [CliSection("Running servers", 40)]
    [Option("stop", HelpText = "Stop a running host's server. Usage: --stop <server>")]
    public bool Stop { get; set; }

    [CliSection("Running servers", 40)]
    [Option("restart", HelpText = "Restart a running host's server. Usage: --restart <server>")]
    public bool Restart { get; set; }

    // ---- create / delete ---------------------------------------------------
    [CliSection("Create and delete", 50)]
    [Option("create-server", HelpText = "Create a server. Usage: --create-server --name <name> [--type BDS] [--preview] [--auto-start] [--no-download]")]
    public bool CreateServer { get; set; }

    [CliSection("Create and delete", 50)]
    [Option("name", Hidden = true, HelpText = "Name for a new server (used with --create-server).")]
    public string? Name { get; set; }

    [CliSection("Create and delete", 50)]
    [Option("type", Hidden = true, HelpText = "Server type/plugin to create (default: BDS).")]
    public string Type { get; set; } = "BDS";

    [CliSection("Create and delete", 50)]
    [Option("preview", Hidden = true, HelpText = "Create a preview (Bedrock Preview) server.")]
    public bool Preview { get; set; }

    [CliSection("Create and delete", 50)]
    [Option("auto-start", Hidden = true, HelpText = "Enable auto-start for the new server.")]
    public bool AutoStart { get; set; }

    [CliSection("Create and delete", 50)]
    [Option("no-download", Hidden = true, HelpText = "Create the folder and registry entry without downloading server files.")]
    public bool NoDownload { get; set; }

    [CliSection("Create and delete", 50)]
    [Option("delete-server", HelpText = "Delete a server and all of its files. Usage: --delete-server <server> [--yes]")]
    public bool DeleteServer { get; set; }

    [CliSection("Create and delete", 50)]
    [Option("yes", Hidden = true, HelpText = "Confirm destructive operations without prompting.")]
    public bool Yes { get; set; }

    // ---- output ------------------------------------------------------------
    [CliSection("Machine output", 60)]
    [Option("json", HelpText = "Print machine-readable JSON where supported.")]
    public bool Json { get; set; }

    // ---- positional args --------------------------------------------------
    [Value(0, MetaName = "args", Required = false, HelpText = "Command arguments (server name, command line, filter, ...)")]
    public IEnumerable<string>? Arguments { get; set; }
}
