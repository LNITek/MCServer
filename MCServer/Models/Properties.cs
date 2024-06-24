using System.ComponentModel;
using ExtraFunctions.ExGenerators;
using ExtraFunctions.Extras;

namespace MCServer.Models
{
    public partial class Property : INotifyPropertyChanged
    {
        public static ExList<Property> Properties { get; set; } = new ExList<Property>();

        public static Property Find(string Name) => Properties.Find(x => x.Name == Name);

        //------------------------------------------------------------------------------------//

        [NotifyChanged]
        string name { get; set; }
        [NotifyChanged("Value")]
        string Val { get; set; }
        public List<string> Comments { get; set; } = [];

        public string Desc => string.Join("\r\n", Comments);
        public string ShortDesc
        {
            get
            {
                var Desc = Comments.FirstOrDefault();
                if (Desc == null) return "";
                if (Desc.Length > 50)
                    return Desc[..50] + "...";
                else return Desc;
            }
        }

        public Property(string Name, string Value, IEnumerable<string> Comments)
        {
            this.Name = Name;
            this.Value = Value;
            this.Comments.AddRange(Comments);
        }
    }
}
