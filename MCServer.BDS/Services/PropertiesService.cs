using ExtraFunctions.Extras;
using MCServer.Plugins;
using MudBlazor;
using Newtonsoft.Json;

namespace MCServer.BDS.Services;

public static class BedrockPropertiesService
{
    #region Server Props
    public static List<Property> GetProperties(this BedrockServer server)
    {
        List<Property> Properties = [];

        if (!File.Exists(server.ServerPath + "/server.properties"))
            server.Host.NotifyUser("Server Properties: could not find server properties file!", Severity.Error);
        else using (var Reader = new StreamReader(server.ServerPath + "/server.properties"))
            Properties = ReadProperties(Reader);

        return Properties;
    }

    public static void SetProperties(this BedrockServer server, IEnumerable<Property> Properties)
    {
        if (!File.Exists(server?.ServerPath + "/server.properties"))
            server?.Host.NotifyUser("Server Properties: Could not find server properties file!", Severity.Error);
        else using (var Writer = File.CreateText(server.ServerPath + "/server.properties"))
            // Dedupe defensively (last value wins, matching BDS): a doubled file once made
            // BDS boot the wrong world because a stale level-name shadowed the active one.
            foreach (var prop in Properties
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .OrderBy(x => x.Order))
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

    public static List<Property> ReadProperties(StreamReader Reader)
    {
        List<Property> Properties = [];

        double I = 0;

        while (!Reader.EndOfStream)
        {
            var Line = Reader.ReadLine();
            while (string.IsNullOrWhiteSpace(Line))
                Line = Reader.ReadLine();
            var Prop = Line!.Split('=');
            var Com = new List<string>();
            var Mode = PropertyEditMode.None;
            Line = Reader.ReadLine();
            while (!string.IsNullOrWhiteSpace(Line) && Line.StartsWith('#'))
            {
                if (!(Line.StartsWith("#$") && Enum.TryParse<PropertyEditMode>(Line.Remove(0, 2).Trim(), out Mode)))
                    Com.Add(Line);

                Line = Reader.ReadLine();
            }
            // Collapse duplicate keys (last value wins, matching BDS) so callers
            // can never update one copy while a stale twin shadows it.
            var existing = Properties.FirstOrDefault(p => p.Name.Equals(Prop[0].Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                Properties.Add(new Property(Prop[0].Trim(), Prop[1].Trim(), Com) { Order = I++, Mode = Mode });
            else
            {
                existing.Value = Prop[1].Trim();
                existing.Mode = Mode;
                if (Com.Count > 0)
                    existing.Comments = Com;
            }
        }

        return Properties;
    }
    #endregion

    #region Player Props
    public static List<Player> GetPlayers(this BedrockServer server)
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
                    BanResin = item.BanResion,
                    BanTime = item.BanTime,
                    LastLogin = item.LastLogin,
                    TotalPlayTime = item.TotalPlayTime,
                });
            }
            else
            {
                player.Ban = item.Ban;
                player.BanResin = item.BanResion;
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
            var permission = Enum.Parse<PlayerPermission>(item.permission, true);
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

    public static void SetPlayers(this BedrockServer server, IEnumerable<Player> Players)
    {
        var json = JsonConvert.SerializeObject(Players.Select(x => x.AsConfig()), JsonOptions.SerializerSettings);
        File.WriteAllText(server.ServerPath + "/players.json", json);
        json = JsonConvert.SerializeObject(Players.Where(x => x.WhiteList).Select(x => x.AsAllowList()));
        File.WriteAllText(server.ServerPath + "/allowlist.json", json);
        json = JsonConvert.SerializeObject(Players.Select(x => x.AsPermission()));
        File.WriteAllText(server.ServerPath + "/permissions.json", json);
    }

    internal static List<Player.Config> GetConfigs(BedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "players.json"))
        {
            return [];
        }
        using var stream = File.Open(server.ServerPath + "/players.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        using var jsonReader = new JsonTextReader(reader);
        var serializer = JsonSerializer.Create(JsonOptions.SerializerSettings);
        var permissions = serializer.Deserialize<Player.Config[]>(jsonReader);

        return [.. permissions?.OfType<Player.Config>() ?? []];
    }

    internal static List<Player.AllowList> GetAllowLists(BedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/allowlist.json"))
        {
            return [];
        }

        var json = File.ReadAllText(server.ServerPath + "/allowlist.json");
        var allowLists = JsonConvert.DeserializeObject<Player.AllowList[]>(json);

        return [.. allowLists?.OfType<Player.AllowList>() ?? []];
    }

    internal static List<Player.Permission> GetPermissions(BedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/permissions.json"))
        {
            return [];
        }
        var json = File.ReadAllText(server.ServerPath + "/permissions.json");
        var permissions = JsonConvert.DeserializeObject<Player.Permission[]>(json);

        return [.. permissions?.OfType<Player.Permission>() ?? []];
    }
    #endregion

    #region Packet Config

    public static PacketConfig GetPacketConfig(this BedrockServer server)
    {
        if (!File.Exists(server.ServerPath + "/packetlimitconfig.json"))
        {
            server.Host.NotifyUser("Packet Config: Could not find config file!", Severity.Error);
            return new();
        }

        var json = File.ReadAllText(server.ServerPath + "/packetlimitconfig.json");
        return JsonConvert.DeserializeObject<PacketConfig>(json) ?? new();
    }

    public static void SetPacketConfig(this BedrockServer server, PacketConfig config)
    {
        if (server is null) return;
        var json = JsonConvert.SerializeObject(config);
        File.WriteAllText(server.ServerPath + "/packetlimitconfig.json", json);
    }

    #endregion
}
