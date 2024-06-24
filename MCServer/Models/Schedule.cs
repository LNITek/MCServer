using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using ExtraFunctions.ExGenerators;

namespace MCServer.Models;

public partial class Schedule : INotifyPropertyChanged
{
    public static ObservableCollection<Schedule> Schedules { get; set; } = [
        //new("Session Backup", "backup"){
        //    Description = "Backup world before session ends.",
        //    Enabled = true,
        //    Ones = false,
        //    Time = new(17,30,0),
        //    WeekDays = [WeekDay.Su]
        //},
        //new("Session End", "exit",["600"]){
        //    Description = "End the session.",
        //    Enabled = true,
        //    Ones = false,
        //    Time = new(17,75,0),
        //    WeekDays = [WeekDay.Su]
        //},
        //new("Stiaan End", "exit",["60"]){
        //    Description = "Ends stiaans spesial session.",
        //    Enabled = true,
        //    Ones = false,
        //    Time = new(16,30,0),
        //    WeekDays = [WeekDay.Tu, WeekDay.Th]
        //},
        ];

    //------------------------------------------------------------------------------------//

    [NotifyChanged]
    string name { get; set; }
    [NotifyChanged]
    string description { get; set; } = string.Empty;
    [NotifyChanged([nameof(StateBrush)])]
    bool enabled { get; set; } = false;
    [NotifyChanged([nameof(StateBrush)])]
    bool ones { get; set; } = false;

    [NotifyChanged]
    string command { get; set; }
    public List<int> Args { get; set; } = [];
    [NotifyChanged([nameof(StartDateFormated)])]
    DateTime startDate { get; set; } = DateTime.Today;
    [NotifyChanged]
    TimeSpan time { get; set; } = TimeSpan.Zero;
    public List<DayOfWeek> WeekDays { get; set; } = [];

    [NotifyChanged([nameof(LastRunFormated)])]
    DateTime? lastRun { get; set; } = null;
    [NotifyChanged, JsonIgnore]
    public string LastRunFormated => "Last executetion: " + LastRun?.ToString("d") ?? "-Unknown";

    [NotifyChanged, JsonIgnore]
    public Brush StateBrush
    {
        get
        {
            if (!Enabled) return Brushes.Red;
            if (Ones) return Brushes.LimeGreen;
            return new SolidColorBrush(Color.FromRgb(3, 169, 244));
        }
    }

    [JsonIgnore]
    public string StartDateFormated => StartDate.ToString("d") ?? "Pick A Date";

    [JsonConstructor]
    public Schedule(string Name, string Command)
    {
        this.Name = Name;
        this.Command = Command;
    }

    public Schedule(string Name, string Command, IEnumerable<int> Args)
    {
        this.Name = Name;
        this.Command = Command;
        this.Args.AddRange(Args);
    }

    public override string ToString() => $"{Command.Trim().ToLower()} {string.Join(" ", Args)}";
}
