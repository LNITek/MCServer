using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MCServer.Services;

/// <summary>
/// Owns the live <see cref="AppSettings"/> and writes changes (e.g. server renames)
/// back to the settings JSON files they were loaded from.
/// </summary>
public sealed class SettingsService
{
    private readonly string _contentRoot;
    private readonly string _environment;

    public AppSettings Settings { get; }

    public SettingsService(IOptions<AppSettings> options, IHostEnvironment environment)
    {
        Settings = options.Value;
        _contentRoot = environment.ContentRootPath;
        _environment = environment.EnvironmentName;
    }

    /// <summary>
    /// Removes the server settings with the given ID and persists.
    /// Returns false when no entry with that ID existed.
    /// </summary>
    public bool RemoveServer(string serverId)
    {
        var removed = Settings.ServerSettings.RemoveAll(x => x.ID == serverId) > 0;
        if (removed) Save();
        return removed;
    }

    /// <summary>
    /// Persists the current settings into every settings file under the content root
    /// that already contains an <c>AppSettings</c> section, leaving all
    /// other sections (Logging, ...) untouched. Never throws.
    /// </summary>
    public void Save()
    {
        #if DEBUG
        var file = $"appsettings.{_environment}.json";
        #else
        var file = "appsettings.json";
        #endif

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
            if (root is null || !root.ContainsKey(AppSettings.SettingName))
                return;

            var section = JsonNode.Parse(JsonSerializer.Serialize(Settings));
            root[AppSettings.SettingName] = section;
            File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // A failed save must never take the UI down.
        }
    }
}
