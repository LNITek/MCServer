using System.Collections.ObjectModel;
using System.ComponentModel;
using ExtraFunctions.ExGenerators;
using ExtraFunctions.Extras;
using MCServer.Models;

namespace MCServer.ViewModels;

public partial class ScheduleVM : INotifyPropertyChanged
{
    public bool Selected => SelectedSchedule != null;
    [NotifyChanged("SelectedSchedule", [nameof(Selected)])]
    Schedule SelSchedule { get; set; }

    public ExList<Schedule> FilterdSchedule { get; set; } = [];

    public IEnumerable<PowerMode> PowerModes => Enum.GetValues<PowerMode>();

    public ScheduleVM()
    {
        FilterdSchedule.AddRange(Schedule.Schedules);
        Schedule.Schedules.CollectionChanged += (s, e) =>
        {
            switch (e.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                    FilterdSchedule.AddRange(e.NewItems.Cast<Schedule>());
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                    FilterdSchedule.RemoveRange(e.OldItems.Cast<Schedule>());
                    break;
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    FilterdSchedule.Clear();
                    break;
            }
        };
    }
}
