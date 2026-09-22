using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using MCServer.Plugins;

namespace MCServer.Services;

/// <summary>
/// Discovers server plugins and exposes their metadata + UI pages to the host.
/// External plugins are drop-in: copy a plugin <c>.dll</c> (or <c>.zip</c> package)
/// into <see cref="PluginsPath"/> — via the Plugins UI page or manually —
/// then restart the app (restart-to-load, no hot unload).
/// </summary>
public sealed class PluginService
{
    public string PluginsPath { get; }

    private readonly List<IGameServerPlugin> _serverPlugins = [];
    private readonly List<Assembly> _componentAssemblies = [];
    private readonly Lock _lock = new();

    /// <summary>Set after a plugin file was installed and a restart is needed to load it.</summary>
    public bool RestartRequired { get; private set; }

    public PluginService(string pluginsPath)
    {
        PluginsPath = pluginsPath;
        Directory.CreateDirectory(pluginsPath);
    }

    public IReadOnlyList<IGameServerPlugin> ServerPlugins
    {
        get
        {
            lock (_lock)
            {
                return [.. _serverPlugins];
            }
        }
    }

    /// <summary>Assemblies handed to the Blazor Router so plugin components render.</summary>
    public IReadOnlyList<Assembly> ComponentAssemblies
    {
        get
        {
            lock (_lock)
            {
                return [.. _componentAssemblies];
            }
        }
    }

    /// <summary>Registers a built-in plugin (shipped with the app, e.g. BDS).</summary>
    public void RegisterBuiltIn(IGameServerPlugin plugin)
    {
        lock (_lock)
        {
            AddLocked(plugin);
        }
    }

    /// <summary>Loads external plugin assemblies from <see cref="PluginsPath"/>.</summary>
    public void LoadFromFolder()
    {
        if (!Directory.Exists(PluginsPath)) return;

        foreach (var file in Directory.GetFiles(PluginsPath, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(file));
                ScanAssembly(assembly);
            }
            catch
            {
                // A plugin must never take the host down. Bad files are skipped.
            }
        }
    }

    private void ScanAssembly(Assembly assembly)
    {
        lock (_lock)
        {
            var hasComponents = false;
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;

                    if (typeof(IGameServerPlugin).IsAssignableFrom(type) &&
                        type.GetConstructor(Type.EmptyTypes) is not null)
                    {
                        try
                        {
                            var plugin = (IGameServerPlugin)Activator.CreateInstance(type)!;
                            AddLocked(plugin);
                        }
                        catch { }
                    }

                    if (!hasComponents && typeof(Microsoft.AspNetCore.Components.ComponentBase).IsAssignableFrom(type))
                        hasComponents = true;
                }

                if (hasComponents && !_componentAssemblies.Contains(assembly))
                    _componentAssemblies.Add(assembly);
            }
            catch (ReflectionTypeLoadException) { }
            catch { }
        }
    }

    private void AddLocked(IGameServerPlugin plugin)
    {
        if (_serverPlugins.Any(p => p.ServerType.Equals(plugin.ServerType, StringComparison.OrdinalIgnoreCase)))
            return;
        _serverPlugins.Add(plugin);

        var assembly = plugin.GetType().Assembly;
        if (!_componentAssemblies.Contains(assembly))
            _componentAssemblies.Add(assembly);
    }

    public IGameServerPlugin? FindPlugin(string serverType)
    {
        lock (_lock)
        {
            return _serverPlugins.FirstOrDefault(p =>
                p.ServerType.Equals(serverType, StringComparison.OrdinalIgnoreCase) ||
                p.Aliases.Any(a => a.Equals(serverType, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public IEnumerable<ServerPageDefinition> GetPages(IGameServer server)
    {
        var plugin = FindPlugin(server.Settings.Type);
        if (plugin is IServerPagesProvider pages)
            return pages.GetPages();
        return [];
    }

    public ServerPageDefinition? FindPage(IGameServer server, string slug)
    {
        return GetPages(server).FirstOrDefault(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Installs a plugin file (.dll or .zip package) into <see cref="PluginsPath"/>.
    /// Takes effect after restart.
    /// </summary>
    public async Task<string> InstallAsync(Stream content, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".dll" or ".zip"))
            throw new InvalidOperationException("Only .dll or .zip plugin files are supported.");

        if (ext == ".dll")
        {
            var target = Path.Combine(PluginsPath, Path.GetFileName(fileName));
            await using var file = File.Create(target);
            await content.CopyToAsync(file);
        }
        else
        {
            var packageName = Path.GetFileNameWithoutExtension(fileName);
            var targetDir = Path.Combine(PluginsPath, packageName);
            Directory.CreateDirectory(targetDir);
            await Task.Run(() =>
            {
                using var archive = new System.IO.Compression.ZipArchive(content, System.IO.Compression.ZipArchiveMode.Read);
                archive.ExtractToDirectory(targetDir, overwriteFiles: true);
            });
        }

        RestartRequired = true;
        return $"Plugin '{fileName}' installed. Restart the app to load it.";
    }

    public void MarkRestarted() => RestartRequired = false;
}
