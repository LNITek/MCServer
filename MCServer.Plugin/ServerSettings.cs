using System.ComponentModel;
using System.Text.Json.Serialization;
using ExtraFunctions.ExGenerators;

namespace MCServer.Plugins;

public partial class ServerSettings : INotifyPropertyChanged
{
    public const string SettingName = nameof(ServerSettings);

    public string ID { get; init; } = Guid.NewGuid().ToString();
    [NotifyChanged]
    string path { get; set; } = "";
    [NotifyChanged]
    string name { get; set; } = string.Empty;
    [NotifyChanged]
    string version { get; set; } = "0.0.0.0";
    [NotifyChanged]
    bool preview { get; set; } = false;

    [NotifyChanged]
    string type { get; set; } = nameof(IGameServer);
    [NotifyChanged]
    bool autoStart { get; set; } = false;

    public string GetFullPath(string filesRoot)
    {
        if (string.IsNullOrEmpty(Path))
            return System.IO.Path.Combine(filesRoot, Name);
        return System.IO.Path.GetFullPath(Path);
    }

    [JsonIgnore]
    [Obsolete("Use GetFullPath(filesRoot) or IGameServer.ServerPath instead.")]
    public string FullPath => System.IO.Path.GetFullPath(Path);
}
