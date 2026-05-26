using System.Runtime.CompilerServices;
using System.Text.Json;
using MCServer.Components;
using MCServer.Helpers;
using MCServer.Server;

namespace MCServer.Services;

public static class PropertiesService
{
    #region Server Props
    public static List<Property> GetProperties(this MCBedrockServer server)
    {
        List<Property> Properties = [];
        double I = 0;

        if (!File.Exists(server.ServerPath + "/server.properties"))
            Program.NotifyUser("Server Properties: could not find server properties file!", MudBlazor.Severity.Error);
        else using (var Reader = new StreamReader(server.ServerPath + "/server.properties"))
            while (!Reader.EndOfStream)
            {
                var Line = Reader.ReadLine();
                while (string.IsNullOrWhiteSpace(Line))
                    Line = Reader.ReadLine();
                var Prop = Line.Split('=');
                var Com = new List<string>();
                Line = Reader.ReadLine();
                while (!string.IsNullOrWhiteSpace(Line) && Line.StartsWith('#'))
                {
                    Com.Add(Line);
                    Line = Reader.ReadLine();
                }
                Properties.Add(new Property(Prop[0].Trim(), Prop[1].Trim(), Com) { Order = I++ });
            }

        return Properties;
    }

    public static void SetProperties(this MCBedrockServer server, IEnumerable<Property> Properties)
    {
        if (!File.Exists(server.ServerPath + "/server.properties"))
            Program.NotifyUser("Server Properties: Could not find server properties file!", MudBlazor.Severity.Error);
        else using (var Writer = File.CreateText(server.ServerPath + "/server.properties"))
            foreach (var prop in Properties)
            {
                Writer.WriteLine("");
                Writer.WriteLine($"{prop.Name}={prop.Value}");
                prop.Comments.ForEach(Writer.WriteLine);
            }
    }
    #endregion

    #region Player Props
    public static List<Player> GetPlayers(this MCBedrockServer server)
    {
        List<Player> Players = [];

        foreach (var item in GetAllowLists(server))
        {
            var player = Players.Find(x => x.Xuid == item.xuid);
            if (player is null)
            {
                Players.Add(new(item.name, item.xuid) { AllowList = true, IgnoresPlayerLimit = item.ignoresPlayerLimit });
            }
            else
            {
                player.AllowList = true;
                player.IgnoresPlayerLimit = item.ignoresPlayerLimit;
            }
        }
        foreach (var item in GetPermissions(server))
        {
            var permission = Enum.Parse<PlayerPermission>(item.permission);
            var player = Players.Find(x => x.Xuid == item.xuid);
            if (player is null)
            {
                Players.Add(new(item.name, item.xuid, permission));
            }
            else
            {
                player.Permission = permission;
            }
        }

        return Players;
    }

    internal static List<AllowList> GetAllowLists(MCBedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/allowlist.json"))
        {
            Program.NotifyUser("Player Properties: Could not find player allow list file!", MudBlazor.Severity.Error);
            return [];
        }

        var allowLists = JsonSerializer.Deserialize<AllowList[]>
            (File.Open(server.ServerPath + "/allowlist.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        return [.. allowLists?.OfType<AllowList>() ?? []];
    }

    internal static List<Permission> GetPermissions(MCBedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/permissions.json"))
        {
            Program.NotifyUser("Player Properties: Could not find player permissions file!", MudBlazor.Severity.Error);
            return [];
        }
        var permissions = JsonSerializer.Deserialize<Permission[]>
            (File.Open(server.ServerPath + "/permissions.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        return [.. permissions?.OfType<Permission>() ?? []];
    }
    #endregion

    #region Packet Config

    public static PacketConfig GetPacketConfig(this MCBedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/packetlimitconfig.json"))
        {
            Program.NotifyUser("Packet Config: Could not find config file!", MudBlazor.Severity.Error);
            return new();
        }

        return JsonSerializer.Deserialize<PacketConfig>
            (File.Open(server.ServerPath + "/packetlimitconfig.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) ?? new();
    }
    #endregion
}
