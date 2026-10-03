using MCServer.Plugins;

namespace MCServer.Server;

/// <summary>
/// Registry of running servers. Server instances are created through
/// registered <see cref="IGameServerPlugin"/> factories keyed by
/// <see cref="ServerSettings.Type"/> (plus plugin aliases).
/// </summary>
public class GameServers : List<IGameServer>, IDisposable
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IGameServerPlugin> _factories = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<IGameServerPlugin> Plugins
    {
        get
        {
            lock (_lock)
            {
                return [.. _factories.Values.Distinct()];
            }
        }
    }

    public void RegisterPlugin(IGameServerPlugin plugin)
    {
        lock (_lock)
        {
            _factories[plugin.ServerType] = plugin;
            foreach (var alias in plugin.Aliases)
                _factories.TryAdd(alias, plugin);
        }
    }

    public void Register(IEnumerable<ServerSettings> settings, IServerHost host)
    {
        lock (_lock)
        {
            foreach (var setting in settings)
                RegisterLocked(setting, host);
        }
    }

    public void Register(ServerSettings setting, IServerHost host)
    {
        lock (_lock)
        {
            RegisterLocked(setting, host);
        }
    }

    private void RegisterLocked(ServerSettings setting, IServerHost host)
    {
        if (FindByID(setting.ID) is not null) return;
        if (!_factories.TryGetValue(setting.Type, out var plugin)) return;
        Add(plugin.Create(setting, host));
    }

    public IGameServer? FindByID(string id)
    {
        return this.Find(x => x.Settings.ID == id);
    }
    public IGameServer? FindByName(string name)
    {
        return this.Find(x => x.Settings.Name == name);
    }

    /// <summary>
    /// Releases schedule timers, disposes the server and removes it from
    /// the registry. Returns false when no server with that ID was registered.
    /// </summary>
    public bool Unregister(IGameServer server)
    {
        lock (_lock)
        {
            var existing = FindByID(server.Settings.ID);
            if (existing is null) return false;
            existing.RemoveSchedules(existing.ServerSchedules);
            try { existing.Dispose(); } catch { }
            return Remove(existing);
        }
    }

    public void Dispose()
    {
        this.ForEach(server => server.Dispose());
    }
}
