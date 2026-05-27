using System.ComponentModel;
using ExtraFunctions.ExGenerators;
using MudBlazor;

namespace MCServer.Helpers
{
    public enum PlayerPermission
    {
        Default = -1,
        Operator,
        Member,
        Visitor,
    }

    public partial class Player : INotifyPropertyChanged
    {
        [NotifyChanged]
        string name { get; set; }
        [NotifyChanged([nameof(IsVerified)])]
        string xuid { get; set; }
        [NotifyChanged([nameof(PlayerLimitColour)])]
        bool whiteList { get; set; } = false;
        [NotifyChanged([nameof(PlayerLimitColour)])]
        bool ignoresPlayerLimit { get; set; } = false;
        [NotifyChanged]
        PlayerPermission permissions { get; set; }

        public bool Ban { get; set; } = false;
        public DateTime? BanTime { get; set; } = null;
        public string BanResion { get; set; } = string.Empty;
        public DateTime? LastLogin { get; set; } = null;
        public TimeSpan TotalPlayTime { get; set; } = TimeSpan.Zero;
        
        public bool IsVerified => !string.IsNullOrWhiteSpace(xuid);
        public Color PlayerLimitColour
        {
            get
            {
                if (Ban) return Color.Error;
                if (!WhiteList) return Color.Secondary;
                if (IgnoresPlayerLimit) return Color.Success;
                return Color.Info;
            }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(xuid)) return Xuid;
                if (!string.IsNullOrWhiteSpace(Name)) return Name;
                return "";
            }
        }

        public Player(string Name, string Xuid, PlayerPermission Permission = PlayerPermission.Default)
        {
            this.Name = Name;
            this.Xuid = Xuid;
            this.Permissions = Permission;
        }

        public Player()
        {
            Name = "NewPlayer"; 
            Xuid = string.Empty;
            Permissions = PlayerPermission.Default;
        }

        public AllowList AsAllowList() => new() { name = name, xuid = xuid, ignoresPlayerLimit = ignoresPlayerLimit };
        public Permission AsPermission() => new() { permission = Permissions.ToString().ToLower(), name = name, xuid = xuid };
        public Config AsConfig() => new()
        {
            Ban = Ban, 
            BanTime = BanTime, 
            BanResion = BanResion, 
            LastLogin = LastLogin, 
            TotalPlayTime = TotalPlayTime, 
            name = name, 
            xuid = xuid
        };
        
        public class AllowList()
        {
            public bool ignoresPlayerLimit { get; set; }
            public string name { get; set; }
            public string xuid { get; set; }
        }

        public class Permission
        {
            public string permission { get; set; }
            public string name { get; set; }
            public string xuid { get; set; }
        }
        
        public class Config
        {
            public bool Ban { get; set; }
            public DateTime? BanTime { get; set; }
            public string BanResion { get; set; }
            
            public DateTime? LastLogin { get; set; }
            public TimeSpan TotalPlayTime { get; set; }
            
            public string name { get; set; }
            public string xuid { get; set; }
        }
    }
}
