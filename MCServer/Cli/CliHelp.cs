using System.Reflection;
using CommandLine;

namespace MCServer.Cli;

/// <summary>
/// Dynamic help generated from CliOptions. Adding a new option shows up in
/// --help automatically; use [CliSection] to place it in a section.
/// </summary>
public static class CliHelp
{
    private static readonly IReadOnlyList<string> Usage =
    [
        "MCServer — command-line interface",
        "",
        "USAGE",
        "  MCServer [host options]                 Start the web host (default)",
        "  MCServer <command> [args...]            Run one command and exit",
        "  MCServer help | --help | -h             Show this help",
        "",
        "HOST ARGUMENTS",
        "  Arguments not listed below are passed to the ASP.NET Core host unchanged",
        "  (e.g. --urls, --environment, --contentRoot). MCServer will not start a",
        "  second instance: if the host is already running, extra instances exit",
        "  with code 1 and tell you how to contact the running host.",
        "",
        "LIVE vs OFFLINE",
        "  Most read-only commands work with the host stopped (they read files",
        "  directly). Commands that need a live process forward to the running host",
        "  over a local named pipe. If the host is not running they print",
        "  'MCServer is not running' and exit with code 1.",
        "",
        "  Password hashes are never printed; settings output is always masked.",
        "",
    ];

    private static readonly IReadOnlyList<string> Footer =
    [
        "",
        "OUTPUT",
        "  Add --json to machine-readable commands for JSON output.",
        "",
        "EXIT CODES",
        "  0   Success",
        "  1   Runtime failure (host not running, IO error, server not found, ...)",
        "  2   Usage error (unknown option, missing argument, parse failure)",
        "",
        "EXAMPLES",
        "  MCServer --list-servers",
        "  nano \"$(MCServer --path-settings)\"",
        "  MCServer --show-config test --file server.properties",
        "  MCServer --schedules test --json",
        "  MCServer --exec test Backup -world Bedrock level",
        "  MCServer --run test say 'Server restarting soon'",
        "  MCServer --create-server --name \"My World\" --type BDS --auto-start",
        "  MCServer --delete-server \"My World\"",
        "",
        "NOTES",
        "  * 'MCServer is not running' means the web host is not reachable; start",
        "    MCServer (or open the web UI) and try again.",
        "  * A second MCServer process will not start if one is already running.",
        "  * Server folders are always deleted entirely; there is no recycle bin.",
        "  * When the host is running, --create-server/--delete-server are forwarded",
        "    to it (with a type-the-ID confirmation) so memory and disk stay in sync.",
    ];

    public static void Print()
    {
        foreach (var line in Usage)
            Console.WriteLine(line);

        PrintCommands();

        foreach (var line in Footer)
            Console.WriteLine(line);
    }

    private static void PrintCommands()
    {
        var entries = DiscoverEntries();

        //Console.WriteLine("COMMANDS");
        //Console.WriteLine("  (Generated from the registered CLI options; new options appear here automatically.)");

        foreach (var (section, sectionEntries) in entries)
        {
            Console.WriteLine();
            Console.WriteLine(section.ToUpperInvariant());

            foreach (var entry in sectionEntries.OrderBy(e => e.LongName, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {entry.Display}");
                Console.WriteLine($"        {entry.HelpText}");
            }
        }
    }

    private static List<(string Section, List<CliHelpEntry> Entries)> DiscoverEntries()
    {
        var groups = new Dictionary<string, (int Order, List<CliHelpEntry> Entries)>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in typeof(CliOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var option = property.GetCustomAttribute<OptionAttribute>();
            if (option is null || option.Hidden)
                continue;

            var sectionAttr = property.GetCustomAttribute<CliSectionAttribute>();
            var section = sectionAttr?.Section ?? "Other";
            var sectionOrder = sectionAttr?.Order ?? int.MaxValue;

            var entry = new CliHelpEntry(
                option.LongName,
                FormatOption(property, option),
                string.IsNullOrWhiteSpace(option.HelpText) ? "(no description)" : option.HelpText);

            if (!groups.TryGetValue(section, out var group))
                group = (sectionOrder, []);

            group.Entries.Add(entry);
            groups[section] = group;
        }

        return groups
            .OrderBy(pair => pair.Value.Order)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (pair.Key, pair.Value.Entries))
            .ToList();
    }

    private static string FormatOption(PropertyInfo property, OptionAttribute option)
    {
        var names = new List<string>();

        if (!string.IsNullOrWhiteSpace(option.ShortName))
            names.Add($"-{option.ShortName}");

        names.Add($"--{option.LongName}");

        var display = string.Join(" | ", names);
        if (!IsFlag(property.PropertyType))
            display += $" <{ToValueName(property.Name)}>";

        return display;
    }

    private static bool IsFlag(Type type)
    {
        if (type == typeof(bool)) return true;
        if (type == typeof(bool?)) return true;
        if (Nullable.GetUnderlyingType(type) == typeof(bool)) return true;
        return false;
    }

    private static string ToValueName(string propertyName)
    {
        var chars = new List<char>(propertyName.Length + 4);
        foreach (char c in propertyName)
        {
            if (char.IsUpper(c) && chars.Count > 0)
                chars.Add('_');
            chars.Add(char.ToUpperInvariant(c));
        }

        return new string(chars.ToArray());
    }

    private sealed record CliHelpEntry(string LongName, string Display, string HelpText);
}
