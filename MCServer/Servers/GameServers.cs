using MCServer.Helpers;

namespace MCServer.Server;

public class GameServers : List<IGameServer>, IDisposable
{
    public void Register(IEnumerable<ServerSettings> settings)
    {
        this.AddRange(settings.Select(FindGameServer).OfType<IGameServer>());
    }

    public void Register(ServerSettings setting)
    {
        if (FindGameServer(setting) is { } s)
            this.Add(s);
    }

    private IGameServer? FindGameServer(ServerSettings setting)
    {
        if (FindByID(setting.ID) is not null) return null;
        return setting.Type switch
        {
            nameof(MCBedrockServer) => new MCBedrockServer(setting),
            _ => null //throw new ArgumentOutOfRangeException(nameof(setting), setting, "Game server not supported."),
        };
    }

    public IGameServer? FindByID(string id)
    {
        return this.Find(x => x.Settings.ID == id);
    }
    public IGameServer? FindByName(string name)
    {
        return this.Find(x => x.Settings.Name == name);
    }

    public void Dispose()
    {
        this.ForEach(server => server.Dispose());
    }
}

public interface IGameServer
{
    ServerSettings Settings { get; }
    bool ServerRunning { get; set; }
    bool CommandRunning { get; set; }
    public bool ServerRunningStatus  { get; }
    public bool ServerExitedStatus  { get; }
    StackList OutputList { get; set; }
    
    void Dispose();
}