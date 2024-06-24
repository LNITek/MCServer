using System.ComponentModel;
using ExtraFunctions.ExGenerators;

namespace MCServer.ViewModels
{
    public partial class MainVM : INotifyPropertyChanged
    {
        [NotifyChanged]
        double proggess { get; set; } = 0;
        [NotifyChanged([nameof(Status)])]
        bool _CMDRuning { get; set; }
        [NotifyChanged([nameof(Status)])]
        bool serverStatus { get; set; } = false;

        [NotifyChanged]
        public bool Status => !CMDRuning && ServerStatus;
    }
}
