using System.ComponentModel;
using ExtraFunctions.ExGenerators;
using ExtraFunctions.Extras;
using MCServer.Models;

namespace MCServer.ViewModels
{
    public partial class PlayerVM : INotifyPropertyChanged
    {
        public bool Selected => SelectedPlayer != null;
        [NotifyChanged("SelectedPlayer", new[] { nameof(Selected) })]
        Player SelPlayer { get; set; }

        public ExList<Player> FilterdPlayers { get; set; } = new();
        public List<Player> Players { get; set; } = new();
    }
}
