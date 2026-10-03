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
    private readonly HashSet<string> _builtInTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps plugin <c>ServerType</c> to the file (single .dll install) or
    /// top-level package directory (.zip install) it was loaded from.
    /// Built-in plugins have no entry.
    /// </summary>
    private readonly Dictionary<string, string> _pluginPaths = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// Registers a built-in plugin (shipped with the app, e.g. BDS).
    /// Only this method can mark a plugin built-in; the <c>BuildIn</c>
    /// property on the plugin itself is ignored so external plugins
    /// cannot protect themselves from uninstall.
    /// </summary>
    public void RegisterBuiltIn(IGameServerPlugin plugin)
    {
        lock (_lock)
        {
            _builtInTypes.Add(plugin.ServerType);
            foreach (var alias in plugin.Aliases)
                _builtInTypes.Add(alias);
            AddLocked(plugin);
        }
    }

    /// <summary>Host-owned built-in check. Returns true only for plugins registered via <see cref="RegisterBuiltIn"/>.</summary>
    public bool IsBuiltIn(IGameServerPlugin plugin) => IsBuiltIn(plugin.ServerType);

    /// <summary>Host-owned built-in check by server type (or alias).</summary>
    public bool IsBuiltIn(string serverType)
    {
        lock (_lock)
        {
            return _builtInTypes.Contains(serverType);
        }
    }

    /// <summary>Source file or package directory the plugin was loaded from, if any.</summary>
    public bool TryGetPluginPath(string serverType, out string? path)
    {
        lock (_lock)
        {
            var plugin = FindPluginLocked(serverType);
            if (plugin is not null && _pluginPaths.TryGetValue(plugin.ServerType, out path))
                return true;
            path = null;
            return false;
        }
    }

    /// <summary>Loads external plugin assemblies from <see cref="PluginsPath"/>.</summary>
    public void LoadFromFolder()
    {
        if (!Directory.Exists(PluginsPath)) return;

        foreach (var file in Directory.GetFiles(PluginsPath, "*.dll", SearchOption.AllDirectories))
        {
            // Skip marker files and anything outside the plugins root.
            if (file.EndsWith(".delete_pending", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var fullPath = Path.GetFullPath(file);
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                ScanAssembly(assembly, fullPath);
            }
            catch
            {
                // A plugin must never take the host down. Bad files are skipped.
            }
        }
    }

    private void ScanAssembly(Assembly assembly, string? sourceFile = null)
    {
        List<IGameServerPlugin> discovered = [];
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
                        discovered.Add(plugin);
                    }
                    catch { }
                }

                if (!hasComponents && typeof(Microsoft.AspNetCore.Components.ComponentBase).IsAssignableFrom(type))
                    hasComponents = true;
            }
        }
        catch (ReflectionTypeLoadException) { return; }
        catch { return; }

        lock (_lock)
        {
            foreach (var plugin in discovered)
            {
                if (AddLocked(plugin) && sourceFile is not null)
                    MapPluginPathLocked(plugin, sourceFile);
            }

            if (hasComponents && !_componentAssemblies.Contains(assembly))
                _componentAssemblies.Add(assembly);
        }
    }

    /// <summary>
    /// Maps a plugin to its uninstall target: the .dll file itself for
    /// single-file installs, or the top-level package directory for
    /// .zip installs (Plugins/&lt;package&gt;/...).
    /// </summary>
    private void MapPluginPathLocked(IGameServerPlugin plugin, string sourceFile)
    {
        if (_builtInTypes.Contains(plugin.ServerType)) return;
        try
        {
            var pluginsRoot = Path.GetFullPath(PluginsPath);
            var full = Path.GetFullPath(sourceFile);
            if (!full.StartsWith(pluginsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !full.Equals(pluginsRoot, StringComparison.OrdinalIgnoreCase))
                return;

            var relative = Path.GetRelativePath(pluginsRoot, full);
            var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            var target = relative.Contains(Path.DirectorySeparatorChar) || relative.Contains(Path.AltDirectorySeparatorChar)
                ? Path.Combine(pluginsRoot, firstSegment)
                : full;

            _pluginPaths[plugin.ServerType] = target;
        }
        catch { }
    }

    private bool AddLocked(IGameServerPlugin plugin)
    {
        if (_serverPlugins.Any(p => p.ServerType.Equals(plugin.ServerType, StringComparison.OrdinalIgnoreCase)))
            return false;
        _serverPlugins.Add(plugin);

        var assembly = plugin.GetType().Assembly;
        if (!_componentAssemblies.Contains(assembly))
            _componentAssemblies.Add(assembly);
        return true;
    }

    private IGameServerPlugin? FindPluginLocked(string serverType)
    {
        return _serverPlugins.FirstOrDefault(p =>
            p.ServerType.Equals(serverType, StringComparison.OrdinalIgnoreCase) ||
            p.Aliases.Any(a => a.Equals(serverType, StringComparison.OrdinalIgnoreCase)));
    }

    public IGameServerPlugin? FindPlugin(string serverType)
    {
        lock (_lock)
        {
            return FindPluginLocked(serverType);
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

    /// <summary>
    /// Uninstalls an external plugin by server type (or alias).
    /// Deletes the installed .dll file or .zip package directory now;
    /// when the assembly file is locked (loaded into the runtime),
    /// a <c>.delete_pending</c> marker is written and the delete
    /// completes on next startup via <see cref="ProcessPendingDeletes"/>.
    /// Always requires a restart to unload. Built-ins are refused.
    /// </summary>
    /// <param name="serverType">Plugin ServerType or alias.</param>
    /// <param name="isInUse">Return true when servers of the given type still exist; uninstall is then blocked.</param>
    public Task<string> UninstallAsync(string serverType, Func<string, bool>? isInUse = null)
    {
        IGameServerPlugin plugin;
        string? target;
        lock (_lock)
        {
            plugin = FindPluginLocked(serverType)
                ?? throw new InvalidOperationException($"Plugin '{serverType}' is not installed.");

            if (_builtInTypes.Contains(plugin.ServerType) || _builtInTypes.Contains(serverType))
                throw new InvalidOperationException($"Built-in plugin '{plugin.DisplayName}' cannot be uninstalled.");

            if (isInUse is not null &&
                (isInUse(plugin.ServerType) || plugin.Aliases.Any(isInUse)))
                throw new InvalidOperationException(
                    $"Plugin '{plugin.DisplayName}' is still used by existing servers. Delete those servers first.");

            _pluginPaths.TryGetValue(plugin.ServerType, out target);
        }

        return UninstallCoreAsync(plugin, target);
    }

    /// <inheritdoc cref="UninstallAsync(string, Func{string, bool}?)"/>
    public Task<string> UninstallAsync(IGameServerPlugin plugin, Func<string, bool>? isInUse = null) =>
        UninstallAsync(plugin.ServerType, isInUse);

    private async Task<string> UninstallCoreAsync(IGameServerPlugin plugin, string? target)
    {
        if (target is null)
        {
            // No tracked file (e.g. manually copied without restart info):
            // drop from memory so it no longer resolves, still needs restart.
            RemoveFromMemory(plugin);
            RestartRequired = true;
            return $"Plugin '{plugin.DisplayName}' removed from registry. Restart the app to finish; delete its files in '{PluginsPath}' manually if present.";
        }

        var pluginsRoot = Path.GetFullPath(PluginsPath);
        var fullTarget = Path.GetFullPath(target);
        if (!fullTarget.StartsWith(pluginsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete a path outside the plugins folder.");

        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(fullTarget))
                    Directory.Delete(fullTarget, recursive: true);
                else if (File.Exists(fullTarget))
                    File.Delete(fullTarget);
            });
        }
        catch (IOException)
        {
            // File locked by the loaded AssemblyLoadContext: defer to next startup.
            await File.WriteAllTextAsync(fullTarget + ".delete_pending", DateTimeOffset.UtcNow.ToString("o"));
            RemoveFromMemory(plugin);
            RestartRequired = true;
            return $"Plugin '{plugin.DisplayName}' marked for removal. Restart the app to finish uninstall.";
        }
        catch (UnauthorizedAccessException)
        {
            await File.WriteAllTextAsync(fullTarget + ".delete_pending", DateTimeOffset.UtcNow.ToString("o"));
            RemoveFromMemory(plugin);
            RestartRequired = true;
            return $"Plugin '{plugin.DisplayName}' marked for removal. Restart the app to finish uninstall.";
        }

        RemoveFromMemory(plugin);
        RestartRequired = true;
        return $"Plugin '{plugin.DisplayName}' uninstalled. Restart the app to unload it.";
    }

    private void RemoveFromMemory(IGameServerPlugin plugin)
    {
        lock (_lock)
        {
            _serverPlugins.RemoveAll(p =>
                p.ServerType.Equals(plugin.ServerType, StringComparison.OrdinalIgnoreCase));
            _pluginPaths.Remove(plugin.ServerType);
            var assembly = plugin.GetType().Assembly;
            if (!_serverPlugins.Any(p => p.GetType().Assembly == assembly))
                _componentAssemblies.Remove(assembly);
        }
    }

    /// <summary>
    /// Completes deferred uninstalls: deletes targets left behind with a
    /// <c>.delete_pending</c> marker (locked files). Call on startup
    /// before <see cref="LoadFromFolder"/>. Never throws.
    /// </summary>
    public void ProcessPendingDeletes()
    {
        try
        {
            if (!Directory.Exists(PluginsPath)) return;
            foreach (var marker in Directory.GetFiles(PluginsPath, "*.delete_pending", SearchOption.AllDirectories))
            {
                try
                {
                    var target = marker[..^".delete_pending".Length];
                    if (Directory.Exists(target))
                        Directory.Delete(target, recursive: true);
                    else if (File.Exists(target))
                        File.Delete(target);
                    File.Delete(marker);
                }
                catch { /* retry next startup */ }
            }
        }
        catch { }
    }

    public void MarkRestarted() => RestartRequired = false;
}
