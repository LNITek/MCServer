using System.Runtime.CompilerServices;
using System.Text.Json;
using ExtraFunctions.Extras;
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
                var Mode = PropertyEditMode.None;
                Line = Reader.ReadLine();
                while (!string.IsNullOrWhiteSpace(Line) && Line.StartsWith('#'))
                {
                    if (!(Line.StartsWith("#$") && Enum.TryParse<PropertyEditMode>(Line.Remove(0, 2).Trim(), out Mode)))
                        Com.Add(Line);
                    
                    Line = Reader.ReadLine();
                }
                Properties.Add(new Property(Prop[0].Trim(), Prop[1].Trim(), Com) { Order = I++, Mode = Mode });
            }

        return Properties;
    }

    public static void SetProperties(this MCBedrockServer server, IEnumerable<Property> Properties)
    {
        if (!File.Exists(server.ServerPath + "/server.properties"))
            Program.NotifyUser("Server Properties: Could not find server properties file!", MudBlazor.Severity.Error);
        else using (var Writer = File.CreateText(server.ServerPath + "/server.properties"))
            foreach (var prop in Properties.OrderBy(x => x.Order))
            {
                Writer.WriteLine("");
                Writer.WriteLine($"{prop.Name}={prop.Value}");
                Writer.WriteLine($"#$ {prop.Mode}");
                prop.Comments.ForEach(x =>
                {
                    if (!x.StartsWith('#'))
                        x = "# " + x.TrimStart();
                    Writer.WriteLine(x.Trim());
                });
            }
    }
    #endregion

    #region Player Props

    public static List<Player> GetBanList(this MCBedrockServer server, List<Player> BanList)
    {
        foreach (var item in GetConfigs(server).Where(x => x.Ban))
        {
            var player = BanList.Find(x => x.Xuid == item.xuid);
            if (player is null)
            {
                BanList.Add(new(item.name, item.xuid)
                {
                    Ban = item.Ban,
                    BanResion = item.BanResion,
                    BanTime = item.BanTime,
                    LastLogin = item.LastLogin,
                    TotalPlayTime = item.TotalPlayTime,
                });
            }
            else
            {
                player.Ban = item.Ban;
                player.BanResion = item.BanResion;
                player.BanTime = item.BanTime;
                player.LastLogin = item.LastLogin;
                player.TotalPlayTime = item.TotalPlayTime;
            }
        }

        return BanList;
    }
    
    public static List<Player> GetPlayers(this MCBedrockServer server)
    {
        List<Player> Players = [];

        foreach (var item in GetConfigs(server))
        {
            var player = Players.Find(x => x.Xuid == item.xuid);
            if (player is null)
            {
                Players.Add(new(item.name, item.xuid)
                {
                    Ban = item.Ban,
                    BanResion = item.BanResion,
                    BanTime = item.BanTime,
                    LastLogin = item.LastLogin,
                    TotalPlayTime = item.TotalPlayTime,
                });
            }
            else
            {
                player.Ban = item.Ban;
                player.BanResion = item.BanResion;
                player.BanTime = item.BanTime;
                player.LastLogin = item.LastLogin;
                player.TotalPlayTime = item.TotalPlayTime;
            }
        }
        
        foreach (var item in GetAllowLists(server))
        {
            var player = Players.Find(x => x.Xuid == item.xuid);
            if (player is null)
            {
                Players.Add(new(item.name, item.xuid) { WhiteList = true, IgnoresPlayerLimit = item.ignoresPlayerLimit });
            }
            else
            {
                player.WhiteList = true;
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
                player.Permissions = permission;
            }
        }

        return Players;
    }
    
    public static void SetPlayers(this MCBedrockServer server, IEnumerable<Player> Players)
    {
        var json = JsonSerializer.Serialize(Players.Select(x => x.AsConfig()));
        File.WriteAllText(server.ServerPath + "/player.json",json);
        json = JsonSerializer.Serialize(Players.Select(x => x.AsAllowList()));
        File.WriteAllText(server.ServerPath + "/allowlist.json",json);
        json = JsonSerializer.Serialize(Players.Select(x => x.AsPermission()));
        File.WriteAllText(server.ServerPath + "/permissions.json",json);
    }
    
    internal static List<Player.Config> GetConfigs(MCBedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/players.json"))
        {
            Program.NotifyUser("Player Properties: Could not find player config file!", MudBlazor.Severity.Error);
            return [];
        }
        var permissions = JsonSerializer.Deserialize<Player.Config[]>
            (File.Open(server.ServerPath + "/player.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        return [.. permissions?.OfType<Player.Config>() ?? []];
    }

    internal static List<Player.AllowList> GetAllowLists(MCBedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/allowlist.json"))
        {
            Program.NotifyUser("Player Properties: Could not find player allow list file!", MudBlazor.Severity.Error);
            return [];
        }

        var allowLists = JsonSerializer.Deserialize<Player.AllowList[]>
            (File.Open(server.ServerPath + "/allowlist.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        return [.. allowLists?.OfType<Player.AllowList>() ?? []];
    }

    internal static List<Player.Permission> GetPermissions(MCBedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/permissions.json"))
        {
            Program.NotifyUser("Player Properties: Could not find player permissions file!", MudBlazor.Severity.Error);
            return [];
        }
        var permissions = JsonSerializer.Deserialize<Player.Permission[]>
            (File.Open(server.ServerPath + "/permissions.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        return [.. permissions?.OfType<Player.Permission>() ?? []];
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
