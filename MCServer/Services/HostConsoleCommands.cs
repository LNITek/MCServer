using System.Text;
using MCServer.Cli;
using MCServer.Plugins;
using MCServer.Server;

namespace MCServer.Services;

/// <summary>
/// Live host-console commands. Type them into the server terminal while the app is running.
/// Attaches itself to the <see cref="ConsoleInputService"/> bus on construction.
/// </summary>
public sealed class HostConsoleCommands
{
    private readonly AuthService _auth;
    private readonly GameServers _servers;

    public HostConsoleCommands(ConsoleInputService bus, AuthService auth, GameServers servers)
    {
        _auth = auth;
        _servers = servers;
        bus.OnInput += Handle;
    }

    private void Handle(string? line)
    {
        var cmdLine = (line ?? string.Empty).Trim();
        if (cmdLine.Length == 0)
            return;

        var parts = SplitLine(cmdLine);
        var cmd = parts.ElementAtOrDefault(0) ?? string.Empty;

        if (cmd.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return;
        }

        if (cmd.Equals("reset-auth", StringComparison.OrdinalIgnoreCase))
        {
            _auth.ResetToDefault();
            Console.WriteLine($"Admin login has been reset to {AuthService.DefaultUsername}/{AuthService.DefaultPassword}.");
            Console.WriteLine("Change it in Settings after logging in.");
            return;
        }

        if (cmd.Equals("servers", StringComparison.OrdinalIgnoreCase) || cmd.Equals("list-servers", StringComparison.OrdinalIgnoreCase))
        {
            PrintServers();
            return;
        }

        if (cmd.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            PrintStatus(parts.ElementAtOrDefault(1));
            return;
        }

        if (cmd.Equals("schedules", StringComparison.OrdinalIgnoreCase))
        {
            CliInfoHandlers.PrintSchedules(parts.ElementAtOrDefault(1), json: false);
            return;
        }

        if (cmd.Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            RunOnServer(parts.Skip(1).ToArray());
            return;
        }

        if (cmd.Equals("start", StringComparison.OrdinalIgnoreCase) ||
            cmd.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
            cmd.Equals("restart", StringComparison.OrdinalIgnoreCase))
        {
            Power(cmd.ToLowerInvariant(), parts.ElementAtOrDefault(1));
            return;
        }

        Console.WriteLine($"Unknown command '{cmd}'. Type 'help' for a list of commands.");
    }

    private void PrintServers()
    {
        if (_servers.Count == 0)
        {
            Console.WriteLine("No servers registered.");
            return;
        }

        foreach (var server in _servers.OrderBy(s => s.Settings.Name, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"  {server.Settings.Name} ({server.Settings.ID}) [{server.Settings.Type}] — {server.ServerPath}");
    }

    private void PrintStatus(string? filter)
    {
        var selected = Resolve(filter);
        if (selected is null)
            return;

        Console.WriteLine("Name                 Status      Players  Startup");
        Console.WriteLine("-------------------- ----------- ------- ------------------");

        foreach (var server in selected.OrderBy(s => s.Settings.Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = Truncate(server.Settings.Name, 20).PadRight(20);
            var status = (server.ServerStatus.Status ?? "Unknown").PadRight(11);
            var players = server.PlayerCount.ToString().PadRight(7);
            var startup = server.StartupDate?.ToString("g") ?? "—";
            Console.WriteLine($"{name} {status} {players} {startup}");
        }
    }

    private void RunOnServer(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            Console.WriteLine("Usage: run <server> <command...>");
            return;
        }

        var server = Find(args[0]);
        if (server is null)
            return;

        var command = string.Join(' ', args.Skip(1));
        server.RunCommand(command);
        Console.WriteLine($"Command '{command}' sent to '{server.Settings.Name}'.");
    }

    private void Power(string action, string? name)
    {
        var server = Find(name);
        if (server is null)
            return;

        switch (action)
        {
            case "start":
                server.StartServer();
                Console.WriteLine($"Starting '{server.Settings.Name}'...");
                break;
            case "stop":
                server.StopServer(TimeSpan.FromSeconds(10));
                Console.WriteLine($"Stopping '{server.Settings.Name}'...");
                break;
            case "restart":
                server.RestartServer(TimeSpan.FromSeconds(10));
                Console.WriteLine($"Restarting '{server.Settings.Name}'...");
                break;
        }
    }

    private List<IGameServer>? Resolve(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || filter.Equals("all", StringComparison.OrdinalIgnoreCase))
            return [.. _servers];

        var server = Find(filter);
        return server is null ? null : [server];
    }

    private IGameServer? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("A server name or ID is required.");
            return null;
        }

        var needle = name.Trim();
        var server = _servers.FirstOrDefault(s =>
            string.Equals(s.Settings.ID, needle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Settings.Name, needle, StringComparison.OrdinalIgnoreCase));

        if (server is null)
            Console.WriteLine($"Server '{needle}' not found.");

        return server;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..(max - 1)] + "…";

    private static List<string> SplitLine(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Available commands (type in the MCServer terminal):");
        Console.WriteLine("  help                    Show this list.");
        Console.WriteLine("  servers                 List registered servers.");
        Console.WriteLine("  status [name|all]       Show live server status.");
        Console.WriteLine("  schedules [name|all]    Show schedules and their next run.");
        Console.WriteLine("  run <server> <cmd...>   Send a command to a server (e.g. run test list).");
        Console.WriteLine("  start|stop|restart <server>");
        Console.WriteLine("  reset-auth              Reset the admin login to Admin/Admin.");
        Console.WriteLine("For one-off commands outside the host, run 'MCServer --help'.");
    }
}
