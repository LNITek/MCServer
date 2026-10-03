using MCServer.Plugins;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServer.Services;

/// <summary>
/// Owns the live <see cref="AppSettings"/> and persists it to the data-folder
/// settings file (<see cref="Program.SettingsFilePath"/>). The shipped
/// appsettings.json next to the binaries only provides install-time defaults
/// and is never written to (it may live in read-only Program Files).
/// </summary>
public sealed class SettingsService
{
    public AppSettings Settings { get; }

    public SettingsService(IOptions<AppSettings> options)
    {
        Settings = options.Value;
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
    /// Persists the current settings to the data-folder settings file,
    /// creating it (and its directory) on first use. Never throws.
    /// </summary>
    public void Save()
    {
        try
        {
            var file = Program.SettingsFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            var root = new JObject
            {
                [AppSettings.SettingName] = JObject.FromObject(Settings, JsonSerializer.Create(JsonOptions.SerializerSettings))
            };
            File.WriteAllText(file, root.ToString(Formatting.Indented));
        }
        catch
        {
            // A failed save must never take the UI down.
        }
    }
}
