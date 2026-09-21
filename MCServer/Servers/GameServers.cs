using MCServer.Helpers;
using MudBlazor;

namespace MCServer.Server;

public class GameServers : List<IGameServer>, IDisposable
{
    private readonly Lock _lock = new();

    public void Register(IEnumerable<ServerSettings> settings)
    {
        lock (_lock)
        {
            foreach (var setting in settings)
                RegisterLocked(setting);
        }
    }

    public void Register(ServerSettings setting)
    {
        lock (_lock)
        {
            RegisterLocked(setting);
        }
    }

    private void RegisterLocked(ServerSettings setting)
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
    bool ServerRunningStatus  { get; }
    bool ServerExitedStatus  { get; }
    Color RunningStatus { get; }
    StackList OutputList { get; set; }
    
    void Dispose();
}