using System.Diagnostics;
using MCServer.Plugins;

namespace MCServer.Services;

public static class PowerService
{
    public static async void Exit(this IEnumerable<IGameServer> servers, TimeSpan Delay)
    {
        foreach (var server in servers.Where(x => x.CommandRunning))
        {
            await server.StopAsync("Exiting", Delay);
            server.CommandRunning = true;
        }
        Environment.Exit(0);
    }

    public static async void Power(this IEnumerable<IGameServer> servers, TimeSpan Delay, PowerMode Mode)
    {
        foreach (var server in servers.Where(x => x.CommandRunning))
        {
            await server.StopAsync("Powering", Delay);
            server.CommandRunning = true;
        }
        Shutdown(Mode);
        Environment.Exit(0);
    }

    public static void Shutdown(PowerMode Mode)
    {
        if (Mode == PowerMode.None) return;

        var (file, args) = Program.IsWin
            ? (@"shutdown", Mode switch
            {
                PowerMode.LogOff => "/l",
                PowerMode.Shutdown => "/s /t 0",
                PowerMode.Restart => "/r /t 0",
                _ => null,
            })
            : (@"shutdown", Mode switch
            {
                PowerMode.LogOff => "-h now",
                PowerMode.Shutdown => "-h now",
                PowerMode.Restart => "-r now",
                _ => null,
            });

        if (args is null) return;
        Process.Start(file, args);
    }

    public enum PowerMode
    {
        None = 0,

        LogOff = 1,
        Shutdown = 2,
        Restart = 3,
    }
}
