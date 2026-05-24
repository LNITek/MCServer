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

    public partial class Player : INotifyPropertyChanged
    {
        [NotifyChanged]
        string name { get; set; }
        [NotifyChanged([nameof(IsVerified)])]
        string xuid { get; set; }
        [NotifyChanged([nameof(PlayerLimitColour)])]
        bool allowList { get; set; } = false;
        [NotifyChanged([nameof(PlayerLimitColour)])]
        bool ignoresPlayerLimit { get; set; } = false;
        [NotifyChanged]
        PlayerPermission permission { get; set; }

        public bool IsVerified => !string.IsNullOrWhiteSpace(xuid);
        public Color PlayerLimitColour
        {
            get
            {
                if (!AllowList) return Color.Secondary;
                if (IgnoresPlayerLimit) return Color.Success;
                return Color.Info;
            }
        }

        public Player(string Name, string Xuid, PlayerPermission Permission = PlayerPermission.Default)
        {
            this.Name = Name;
            this.Xuid = Xuid;
            this.Permission = Permission;
        }

        public Player()
        {
            Name = "NewPlayer"; 
            Xuid = string.Empty;
            Permission = PlayerPermission.Default;
        }

        public AllowList ToAllowList() => new() { name = name, xuid = xuid, ignoresPlayerLimit = ignoresPlayerLimit };
        public Permission ToPermission() => new() { permission = Permission.ToString().ToLower(), xuid = xuid };
    }
}
