using System.ComponentModel;
using System.Windows.Media;
using ExtraFunctions.ExGenerators;

namespace MCServer.Models
{
    public enum PlayerPermission
    {
        Default = -1,
        Operator,
        Member,
        Visitor,
    }

    public class AllowList
    {
        public bool ignoresPlayerLimit { get; set; }
        public string name { get; set; }
        public string xuid { get; set; }
    }

    public class Permission
    {
        public string permission { get; set; }
        public string xuid { get; set; }
    }

    public partial class Player : INotifyPropertyChanged
    {
        [NotifyChanged]
        string name { get; set; }
        [NotifyChanged(new[] { nameof(IsVerifide) })]
        string xuid { get; set; }
        [NotifyChanged(new[] { nameof(PlayerlimitBrush) })]
        bool allowList { get; set; } = false;
        [NotifyChanged(new[] { nameof(PlayerlimitBrush) })]
        bool ignoresPlayerLimit { get; set; } = false;
        [NotifyChanged]
        PlayerPermission permission { get; set; }

        public bool IsVerifide => !string.IsNullOrWhiteSpace(xuid);
        public Brush PlayerlimitBrush
        {
            get
            {
                if (!AllowList) return Brushes.Gray;
                if (IgnoresPlayerLimit) return Brushes.LimeGreen;
                return new SolidColorBrush(Color.FromRgb(3, 169, 244));
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
            Name = "Player"; 
            Xuid = string.Empty;
            Permission = PlayerPermission.Default;
        }

        public AllowList ToAllowList() => new() { name = name, xuid = xuid, ignoresPlayerLimit = ignoresPlayerLimit };
        public Permission ToPermission() => new() { permission = Permission.ToString().ToLower(), xuid = xuid };
    }
}
