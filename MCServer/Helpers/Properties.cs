using System.Collections.ObjectModel;
using System.ComponentModel;
using ExtraFunctions.ExGenerators;
using ExtraFunctions.Extras;

namespace MCServer.Helpers;

public enum PropertyEditMode
{
    None,
    Overwrite,
    Custom,
}

public partial class Property : INotifyPropertyChanged
{
    [NotifyChanged]
    string name { get; set; } = string.Empty;
    [NotifyChanged("Value")]
    string Val { get; set; } = string.Empty;
    [NotifyChanged]
    IEnumerable<string> comments { get; set; } = [];

    public double Order { get; init; } = 0;
    public PropertyEditMode Mode { get; set; } = PropertyEditMode.None;

    public string Desc
    {
        get => string.Join("\n", Comments);
        set => Comments = [..value.Split("\n")];
    }

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
        this.Comments = Comments;
        Subscribe();
    }

    public Property()
    {
        Name = "new-property";
        Value = "";
        Comments = ["# This is a new property"];
        Mode = PropertyEditMode.Custom;
        Subscribe();
    }

    private void Subscribe()
    {
        PropertyChanged += (s, e) =>
        {
            if (Mode == PropertyEditMode.None)
                Mode = PropertyEditMode.Overwrite;
        };
    }
}
