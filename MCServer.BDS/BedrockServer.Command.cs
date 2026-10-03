using MCServer.BDS.Models;
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
            new ServerCommand("Trim", "Trims chunks outside a keep-area for one dimension (server must be stopped; backs up first).", [
                new ServerCommand.ArgumentInfo("-world", "World folder name. Defaults to the active world.", typeof(string)),
                new ServerCommand.ArgumentInfo("-dimension", "Overworld, Nether or End.", typeof(string)),
                new ServerCommand.ArgumentInfo("-mode", "Rect or Radius.", typeof(string)),
                new ServerCommand.ArgumentInfo("-minx", "Rect: min chunk X.", typeof(int)),
                new ServerCommand.ArgumentInfo("-minz", "Rect: min chunk Z.", typeof(int)),
                new ServerCommand.ArgumentInfo("-maxx", "Rect: max chunk X.", typeof(int)),
                new ServerCommand.ArgumentInfo("-maxz", "Rect: max chunk Z.", typeof(int)),
                new ServerCommand.ArgumentInfo("-cx", "Radius: center chunk X.", typeof(int)),
                new ServerCommand.ArgumentInfo("-cz", "Radius: center chunk Z.", typeof(int)),
                new ServerCommand.ArgumentInfo("-radius", "Radius: keep radius in chunks.", typeof(int)),
                new ServerCommand.ArgumentInfo("-circular", "Radius: keep a circle instead of a square."),
                new ServerCommand.ArgumentInfo("-dryrun", "Count only; delete nothing."),
                new ServerCommand.ArgumentInfo("-nobackup", "Skip the pre-trim backup.")])
            {
                OnExecution = (Args) => this.TrimServer(ParseTrimArgs(Args)),
                Display = new CommandDisplayInfo
                {
                    Icon = MudBlazor.FontIcons.MaterialIcons.Rounded.Crop,
                    Group = "Maintenance",
                    Order = 2,
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

    private TrimOptions ParseTrimArgs(ServerCommandList.Arguments Args)
    {
        string? Str(string name) => Args[name]?.Value.ToString();
        int? Num(string name) => Args[name] is null ? null : Convert.ToInt32(Args[name]!.Value);

        var world = Str("-world");
        if (string.IsNullOrWhiteSpace(world))
            world = this.GetProperties().FirstOrDefault(x => x.Name == "level-name")?.Value;
        if (string.IsNullOrWhiteSpace(world))
            throw new Exception("Trim: specify -world <name> (no active world found).");

        var dimRaw = Str("-dimension") ?? "overworld";
        if (!TrimOptions.TryParseDimension(dimRaw, out var dimension))
            throw new Exception("Trim: -dimension must be Overworld, Nether or End.");

        var modeRaw = (Str("-mode") ?? "").Trim().ToLowerInvariant();
        var hasRect = Num("-minx") is not null || Num("-minz") is not null || Num("-maxx") is not null || Num("-maxz") is not null;
        var hasRadius = Num("-cx") is not null || Num("-cz") is not null || Num("-radius") is not null;
        var mode = modeRaw switch
        {
            "rect" or "rectangle" => TrimMode.Rect,
            "radius" or "radial" => TrimMode.Radius,
            "" => hasRadius && !hasRect ? TrimMode.Radius : TrimMode.Rect,
            _ => throw new Exception("Trim: -mode must be Rect or Radius."),
        };

        var dryRun = Args["-dryrun"] is not null;
        var createBackup = Args["-nobackup"] is null;

        if (mode == TrimMode.Rect)
        {
            var minx = Num("-minx");
            var minz = Num("-minz");
            var maxx = Num("-maxx");
            var maxz = Num("-maxz");
            if (minx is null || minz is null || maxx is null || maxz is null)
                throw new Exception("Trim rect: specify -minx -minz -maxx -maxz (chunk coordinates).");
            return TrimOptions.FromRect(world, dimension, minx.Value, minz.Value, maxx.Value, maxz.Value, dryRun, createBackup);
        }

        var cx = Num("-cx");
        var cz = Num("-cz");
        var radius = Num("-radius");
        if (cx is null || cz is null || radius is null)
            throw new Exception("Trim radius: specify -cx -cz -radius (chunk coordinates).");
        if (radius < 0)
            throw new Exception("Trim radius: -radius must be >= 0.");
        return TrimOptions.FromRadius(world, dimension, cx.Value, cz.Value, radius.Value,
            Args["-circular"] is not null, dryRun, createBackup);
    }
}