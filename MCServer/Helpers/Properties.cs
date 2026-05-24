using System.Collections.ObjectModel;
using System.ComponentModel;
using ExtraFunctions.ExGenerators;
using ExtraFunctions.Extras;

namespace MCServer.Helpers;

public partial class Property : INotifyPropertyChanged
{
    [NotifyChanged]
    string name { get; set; } = string.Empty;
    [NotifyChanged("Value")]
    string Val { get; set; } = string.Empty;
    public List<string> Comments { get; set; } = [];

    public double Order { get; set; } = 0;

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

    public Property()
    {
        Name = "NewProperty";
        Value = "";
        Comments.Add("# This is a new Property");
    }
}
