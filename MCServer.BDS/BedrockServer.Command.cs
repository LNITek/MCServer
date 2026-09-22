using MCServer.BDS.Services;
using MCServer.Plugins;
using MudBlazor;

namespace MCServer.BDS;

public partial class BedrockServer
{
    public ServerCommandList Commands { get; } = [];
    
    public void RunCommand(string command)
    {
        Commands.Parse(command);
    }

    public void SetupCommands()
    {
        Commands.DeclareGroup("Server",
            icon: MudBlazor.FontIcons.MaterialIcons.Rounded.Settings,
            order: 0);
        Commands.DeclareGroup("Maintenance",
            icon: MudBlazor.FontIcons.MaterialIcons.Rounded.Build,
            order: 1);

        Commands.AddRange(
        [
            new ServerCommand("Backup", "Backups the active world.", [
                new ServerCommand.ArgumentInfo("-world", "The name of the world to backup.", typeof(uint))])
            {
                OnExecution = (Args) => this.BackupServer(Args["-world"]?.Value.ToString()),
                Display = new CommandDisplayInfo
                {
                    Icon = MudBlazor.FontIcons.MaterialIcons.Rounded.Backup,
                    Group = "Maintenance",
                    Order = 0,
                    IsDisabled = () => CommandRunning,
                },
            },
            new ServerCommand("Update", "Updates the server.", [])
            {
                OnExecution = (Args) => this.UpdateServer(),
                Display = new CommandDisplayInfo
                {
                    Icon = MudBlazor.FontIcons.MaterialIcons.Rounded.Update,
                    Group = "Maintenance",
                    Order = 1,
                    IsDisabled = () => CommandRunning,
                },
            },
            new ServerCommand("Start", "Starts the server if it's not running yet.", [])
            {
                OnExecution = (Args) => this.StartServer(),
                Display = new CommandDisplayInfo
                {
                    Icon = MudBlazor.FontIcons.MaterialIcons.Rounded.PlayArrow,
                    Group = "Server",
                    Order = 0,
                    IsDisabled = () => ServerRunningStatus,
                },
            },
            new ServerCommand("Restart", "Restarts the server.", [
                new ServerCommand.ArgumentInfo("-delay", "The delay before restarting in seconds.", typeof(uint))
            ])
            {
                OnExecution = (Args) => this.RestartServer(TimeSpan.FromSeconds(((int?)Args["-delay"]?.Value) ?? 10)),
                Display = new CommandDisplayInfo
                {
                    Icon = MudBlazor.FontIcons.MaterialIcons.Rounded.Replay,
                    Group = "Server",
                    Order = 1,
                    IsDisabled = () => ServerExitedStatus,
                },
            },
            new ServerCommand("Stop", "Stops the server.", [
                new ServerCommand.ArgumentInfo("-delay", "The delay before stoping in seconds.", typeof(uint))
            ])
            {
                OnExecution = (Args) => this.StopServer(TimeSpan.FromSeconds(((int?)Args["-delay"]?.Value) ?? 10)),
                Display = new CommandDisplayInfo
                {
                    Icon = MudBlazor.FontIcons.MaterialIcons.Rounded.Stop,
                    Group = "Server",
                    Order = 2,
                    IsDisabled = () => ServerExitedStatus,
                },
            },
        ]);
        Commands.OnCommandException += (command, ex) =>
        {
            if (ex.Message.StartsWith("C-01-"))
                WriteLine(command?.ToString() ?? "");
            else
                Host.NotifyUser(ex.Message, Severity.Error);
        };
    }
}