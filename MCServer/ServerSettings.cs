using System.Text.Json.Serialization;
using MCServer.Server;

namespace MCServer;

public class ServerSettings
{
    public const string SettingName = nameof(ServerSettings);

    public string ID {get; init; } = Guid.NewGuid().ToString();
    public string Path { get; set; } = "";
    public string Name {get; set;} = string.Empty;
    public string Version { get; set; } = "0.0.0.0";
    public bool Preview { get; set; }  = false;
    
    public string Type { get; set; } = nameof(IGameServer);
    public bool AutoStart { get; set; } = false;
    
    [JsonIgnore]
    public string FullPath
    {
        get
        {
            if (string.IsNullOrEmpty(Path))
                return System.IO.Path.Combine(Program.ServerPath, Name);
            return System.IO.Path.GetFullPath(Path);
        }
    }
}