using System.ComponentModel;
using ExtraFunctions.ExGenerators;

namespace MCServer.Plugins;

public partial class ProgressDisplay : INotifyPropertyChanged
{
    /// <summary>Progress fraction 0..1. Reporters must use fractions, not percents.</summary>
    [NotifyChanged([nameof(DisplayValue)])] private double value = 0;
    [NotifyChanged] private string message = string.Empty;
    [NotifyChanged] private bool open = false;
    [NotifyChanged] private bool loading = false;

    public double DisplayValue => Value * 100;

    public void Show(string msg)
    {
        Message = msg;
        Open = true;
    }

    public void Show(string msg, bool loading)
    {
        Show(msg);
        Value = 0;
        Loading = loading;
    }

    public void Show(string msg, double progress)
    {
        Show(msg);
        Value = progress;
        Loading = false;
    }

    public void Hide()
    {
        Value = 0;
        Message = string.Empty;
        Open = false;
        Loading = false;
    }

    public override string ToString() => Loading ? $"{Message}" : $"{Message} {DisplayValue}%";
}