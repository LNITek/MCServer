namespace MCServer.Plugins;

using MudBlazor;

/// <summary>
/// Entry point a server plugin assembly must expose.
/// A plugin creates <see cref="IGameServer"/> instances for its <see cref="ServerType"/>.
/// </summary>
public interface IGameServerPlugin
{
    /// <summary>Matches <see cref="ServerSettings.Type"/> from configuration.</summary>
    string ServerType { get; }

    /// <summary>Extra type names accepted for this plugin (backwards compatibility).</summary>
    IEnumerable<string> Aliases => [];

    string DisplayName { get; }
    string Author => "Unknown";
    string Version => "1.0.0";

    /// <summary>
    /// Legacy compat flag. The host owns built-in status via
    /// <c>PluginService.RegisterBuiltIn</c> / <c>PluginService.IsBuiltIn</c>
    /// and ignores values supplied by external plugins, so a plugin
    /// cannot mark itself built-in to avoid uninstall.
    /// </summary>
    bool BuildIn => false;

    IGameServer Create(ServerSettings settings, IServerHost host);

    /// <summary>
    /// Plugin-driven server creation, called by the host's Add Server flow
    /// after the user selected this plugin. The plugin collects whatever
    /// input it needs (e.g. its own dialog, downloading server files) and
    /// returns the <see cref="ServerSettings"/> for the host to register
    /// and save. Return null when the user cancels.
    /// The default implementation returns blank settings for this
    /// <see cref="ServerType"/>; the host then falls back to asking for a name.
    /// </summary>
    Task<ServerSettings?> CreateServerAsync(IServerHost host, IDialogService dialogs) =>
        Task.FromResult<ServerSettings?>(new ServerSettings { Type = ServerType });
}

/// <summary>
/// Optional plugin capability: contribute per-server UI pages
/// (e.g. Properties, Player settings, Packet configs).
/// Rendered by the host through a generic plugin page route.
/// </summary>
public interface IServerPagesProvider
{
    IEnumerable<ServerPageDefinition> GetPages();
}

/// <summary>
/// Describes one plugin-owned page for a server.
/// <see cref="ComponentType"/> must be a Blazor component with a
/// <c>[Parameter] IGameServer Server</c> property.
/// </summary>
public sealed record ServerPageDefinition(string Slug, string Title, string Icon, Type ComponentType);
