namespace MCServer.Plugins;

/// <summary>
/// One documentation file registered by a server plugin.
/// Rendered by the host Docs page at <c>/docs/{serverId}/{slug}</c>.
/// </summary>
/// <param name="Slug">URL slug, e.g. <c>"notes"</c>.</param>
/// <param name="Title">Display title.</param>
/// <param name="FilePath">File name relative to <see cref="IGameServer.ServerPath"/>.</param>
public sealed record ServerDocFile(string Slug, string Title, string FilePath, string Icon);
