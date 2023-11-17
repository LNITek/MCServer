using System.ComponentModel;
using ExtraFunctions.ExGenerators;

namespace MCServer.ViewModels
{
    public partial class MainVM : INotifyPropertyChanged
    {
        [NotifyChanged]
        double proggess { get; set; } = 0;
    }
}
