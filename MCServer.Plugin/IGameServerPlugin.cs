namespace MCServer.Plugins;

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

    IGameServer Create(ServerSettings settings, IServerHost host);
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
