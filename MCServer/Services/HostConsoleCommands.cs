namespace MCServer.Services;

/// <summary>
/// Live host-console commands. Type them into the server terminal while the app is running.
/// Attaches itself to the <see cref="ConsoleInputService"/> bus on construction.
/// </summary>
public sealed class HostConsoleCommands
{
    private readonly AuthService _auth;

    public HostConsoleCommands(ConsoleInputService bus, AuthService auth)
    {
        _auth = auth;
        bus.OnInput += Handle;
    }

    private void Handle(string? line)
    {
        var cmd = (line ?? "").Trim();
        if (cmd.Length == 0)
            return;

        if (cmd.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
        }
        else if (cmd.Equals("reset-auth", StringComparison.OrdinalIgnoreCase))
        {
            _auth.ResetToDefault();
            Console.WriteLine($"Admin login has been reset to {AuthService.DefaultUsername}/{AuthService.DefaultPassword}.");
            Console.WriteLine("Change it in Settings after logging in.");
        }
        else
        {
            Console.WriteLine($"Unknown command '{cmd}'. Type 'help' for a list of commands.");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Available commands:");
        Console.WriteLine("  help        Show this list.");
        Console.WriteLine("  reset-auth  Reset the admin login to Admin/Admin.");
    }
}
