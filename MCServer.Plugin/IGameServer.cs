using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using MudBlazor;

namespace MCServer.Plugins;

/// <summary>
/// Host-side services a server plugin may use.
/// Implemented by the MCServer host and passed to <see cref="IGameServerPlugin.Create"/>.
/// </summary>
public interface IServerHost
{
    string ServerFilesRoot { get; }
    string BackupPath { get; }
    bool IsWindows { get; }
    void NotifyUser(string message, Severity severity);

    /// <summary>
    /// Persists mutated settings (e.g. <see cref="ServerSettings.Version"/> after an update).
    /// </summary>
    void SaveSettings();
}

/// <summary>
/// Common abstraction for every game server. Core UI pages
/// (Overview, Console, Schedules) only ever talk to this interface,
/// never to a concrete server type.
/// </summary>
public interface IGameServer : INotifyPropertyChanged, IDisposable
{
    ServerSettings Settings { get; }
    IServerHost Host { get; }

    /// <summary>Resolved files directory for this server.</summary>
    string ServerPath { get; }

    bool ServerRunning { get; set; }
    bool CommandRunning { get; set; }
    bool ServerRunningStatus { get; }
    bool ServerExitedStatus { get; }
    Color RunningStatus { get; }
    (Color Indicator, string Status) ServerStatus { get; }

    DateTime? StartupDate { get; }
    double? Progress { get; set; }

    /// <summary>Underlying OS process, if this server runs as one. Null otherwise.</summary>
    Process? ServerProcess { get; }

    /// <summary>Generic player count for the overview charts. 0 when not applicable.</summary>
    int PlayerCount { get; }

    StackList OutputList { get; set; }
    ObservableCollection<Schedule> ServerSchedules { get; }

    /// <summary>All parseable commands. Only commands with <c>Display != null</c> are shown in UI.</summary>
    ServerCommandList Commands { get; }

    /// <summary>Documentation files this server registers, served at <c>/docs/{serverId}/{slug}</c>.</summary>
    IReadOnlyList<ServerDocFile> DocFiles { get; }

    void WriteLine(string line);
    void RunCommand(string command);

    void StartServer();
    void StopServer(TimeSpan delay);
    void RestartServer(TimeSpan delay);
    Task<bool> StopAsync(string message, TimeSpan delay);

    /// <summary>
    /// Deletes everything under <see cref="ServerPath"/> from disk.
    /// Refuses to run while the server is running and refuses to delete
    /// the shared files root or a filesystem root. Schedule timers are
    /// released so nothing fires after the files are gone.
    /// Removing the server from the registry and from the settings files
    /// is a host responsibility, not the plugin's.
    /// </summary>
    /// <exception cref="InvalidOperationException">Server is running, or the resolved path is unsafe.</exception>
    void Delete()
    {
        if (ServerRunningStatus)
            throw new InvalidOperationException($"Stop server '{Settings.Name}' before deleting it.");

        // Release schedule timers so nothing fires after the files are gone.
        this.RemoveSchedules(ServerSchedules);

        if (string.IsNullOrWhiteSpace(ServerPath))
            throw new InvalidOperationException("Server path is empty; refusing to delete.");

        var path = Path.GetFullPath(ServerPath);
        var root = Path.GetFullPath(Host.ServerFilesRoot);

        if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete the shared server files root.");

        if (Path.GetPathRoot(path)?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException($"Refusing to delete filesystem root '{path}'.");

        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    /// <summary>Optional hook after schedules change. Default is a no-op.</summary>
    void RefreshSchedules() { }
}
