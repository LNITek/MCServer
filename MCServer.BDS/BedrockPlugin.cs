using MCServer.BDS.Pages;
using MCServer.Plugins;
using MudBlazor;
using Icons = MudBlazor.FontIcons.MaterialIcons;

namespace MCServer.BDS;

public sealed class BedrockPlugin : IGameServerPlugin, IServerPagesProvider
{
    public string ServerType => "BDS";
    public IEnumerable<string> Aliases => ["MCBedrockServer", "BedrockServer"];
    public string DisplayName => "Bedrock Dedicated Server";
    public string Author => "LNI.Tek";
    public string Version => "1.0.0";

    public IGameServer Create(ServerSettings settings, IServerHost host) =>
        new BedrockServer(settings, host);

    public async Task<ServerSettings?> CreateServerAsync(IServerHost host, IDialogService dialogs)
    {
        var dialog = await dialogs.ShowAsync<Dialogs.NewBedrockServerDialog>(
            "Add Bedrock Server",
            new DialogParameters(),
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not ServerSettings settings)
            return null;
        return settings;
    }

    public IEnumerable<ServerPageDefinition> GetPages() =>
    [
        new ServerPageDefinition("props", "Properties", Icons.Rounded.HomeRepairService, typeof(PropertiesPage)),
        new ServerPageDefinition("player-settings", "Player Settings", Icons.Rounded.AdminPanelSettings, typeof(PlayersPage)),
        new ServerPageDefinition("packets", "Packet Configs", Icons.Rounded.Backpack, typeof(PacketsPage)),
        new ServerPageDefinition("resources", "Resources", Icons.Rounded.Storage, typeof(ResourcesPage)),
    ];
}
