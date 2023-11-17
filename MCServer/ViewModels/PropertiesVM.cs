using System.ComponentModel;
using ExtraFunctions.ExGenerators;
using ExtraFunctions.Extras;
using MCServer.Models;

namespace MCServer.ViewModels
{
    public partial class PropertiesVM : INotifyPropertyChanged
    {
        public bool Selected => SelectedProp != null;
        [NotifyChanged("SelectedProp", new[] { nameof(Selected) })]
        Property SelProp { get; set; }

        public ExList<Property> FilterdProps { get; set; } = new ExList<Property>();

        public PropertiesVM()
        {
            FilterdProps.AddRange(Property.Properties);
        }
    }
}
