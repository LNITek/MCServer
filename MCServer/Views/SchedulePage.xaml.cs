using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ExtraFunctions.Extras;
using MCServer.Models;
using MCServer.ViewModels;
using Wpf.Ui.Controls;

namespace MCServer.Views
{
    public partial class SchedulePage : Page
    {
        public SchedulePage()
        {
            InitializeComponent();

            Data = (ScheduleVM)DataContext;
            Unloaded += (s, e) => Save();
            Setup();
        }

        private readonly ScheduleVM Data;

        public void Setup()
        {
            MainWindow.Window.SizeChanged += (s, e) => Root.MaxHeight = MainWindow.Window.ActualHeight - 50;
            Root.MaxHeight = MainWindow.Window.ActualHeight - 50;

            if (!File.Exists(MainWindow.SchedulePath))
            {
                MainWindow.Window.NotifyUser("Schedule", "Could Not Find Schedule File!", ControlAppearance.Danger);
                return;
            }
        }

        private void ScheduleSelected(object sender, SelectionChangedEventArgs e)
        {
            ClearDayOfWeeks();
            if (Data == null || lstDisplay.SelectedItem is not Schedule sel)
            {
                nbxDalay.Visibility = Visibility.Hidden;
                cmbPowerMode.Visibility = Visibility.Hidden;
                nbxDalay.Value = 10;
                cmbPowerMode.SelectedIndex = 0;
                return;
            }

            nbxTimeH.Value = sel.Time.Hours;
            nbxTimeM.Value = sel.Time.Minutes;
            nbxTimeS.Value = sel.Time.Seconds;

            sel.WeekDays.ForEach(SetDayOfWeeks);
            switch (sel.Command.ToLower())
            {
                default:
                case "backup":
                case "start":
                case "restart":
                    nbxDalay.Visibility = Visibility.Hidden;
                    cmbPowerMode.Visibility = Visibility.Hidden;
                    break;
                case "stop":
                case "exit":
                    nbxDalay.Value = sel.Args.ElementAtOrDefault(0);
                    nbxDalay.Visibility = Visibility.Visible;
                    cmbPowerMode.Visibility = Visibility.Hidden;
                    break;
                case "power":
                    nbxDalay.Value = sel.Args.ElementAtOrDefault(0);
                    cmbPowerMode.SelectedIndex = sel.Args.ElementAtOrDefault(1);
                    nbxDalay.Visibility = Visibility.Visible;
                    cmbPowerMode.Visibility = Visibility.Visible;
                    break;
            }

            void SetDayOfWeeks(DayOfWeek WeekDay)
            {
                (WeekDay switch
                {
                    DayOfWeek.Sunday => rbtSu,
                    DayOfWeek.Monday => rbtMo,
                    DayOfWeek.Tuesday => rbtTu,
                    DayOfWeek.Wednesday => rbtWe,
                    DayOfWeek.Thursday => rbtTh,
                    DayOfWeek.Friday => rbtFr,
                    DayOfWeek.Saturday => rbtSa,
                    _ => null,
                }).IsChecked = true;
            }
            void ClearDayOfWeeks()
            {
                rbtSu.IsChecked = false;
                rbtMo.IsChecked = false;
                rbtTu.IsChecked = false;
                rbtWe.IsChecked = false;
                rbtTh.IsChecked = false;
                rbtFr.IsChecked = false;
                rbtSa.IsChecked = false;
            }
        }

        private void WeekDay_Checked(object sender, RoutedEventArgs e)
        {
            if (Data == null || !Data.Selected || sender is not CheckBox cbx) return;

            var DayOfWeek = (DayOfWeek)Grid.GetColumn(cbx);

            if ((cbx.IsChecked ?? false) && !Data.SelectedSchedule.WeekDays.Contains(DayOfWeek))
                Data.SelectedSchedule.WeekDays.Add(DayOfWeek);
            if (!(cbx.IsChecked ?? false) && Data.SelectedSchedule.WeekDays.Contains(DayOfWeek))
                Data.SelectedSchedule.WeekDays.Remove(DayOfWeek);
        }

        private void nbxDalay_ValueChanged(object sender, RoutedEventArgs e)
        {
            if (Data == null || !Data.Selected) return;

            if (Data.SelectedSchedule.Args.Count >= 1)
                Data.SelectedSchedule.Args[0] = (int?)nbxDalay.Value ?? 0;
            else Data.SelectedSchedule.Args.Add((int?)nbxDalay.Value ?? 0);
        }

        private void cmbPowerMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Data == null || !Data.Selected) return;

            if (Data.SelectedSchedule.Args.Count < 2)
                Data.SelectedSchedule.Args.Add((int?)nbxDalay.Value ?? 0);

            if (Data.SelectedSchedule.Args.Count >= 2)
                Data.SelectedSchedule.Args[1] = (int)cmbPowerMode.SelectedIndex;
            else Data.SelectedSchedule.Args.Add((int)cmbPowerMode.SelectedIndex);
        }

        private void TimeChanged(object sender, RoutedEventArgs e)
        {
            if (Data == null || !Data.Selected) return;

            Data.SelectedSchedule.Time = new((int?)nbxTimeH.Value ?? 0, (int?)nbxTimeM.Value ?? 0, (int?)nbxTimeS.Value ?? 0);
        }

        private void AddNew(object sender, RoutedEventArgs e)
        {
            var CMDType = ((sender as Wpf.Ui.Controls.MenuItem)?.Header?.ToString() ?? "")
                .ToLower().Replace("schedule", "").Trim();

            Schedule sched;

            switch (CMDType)
            {
                case "backup":
                case "start":
                case "restart":
                    sched = new("New Schedule", CMDType);
                    break;
                case "stop":
                case "exit":
                    sched = new("New Schedule", CMDType, [10]);
                    break;
                case "power":
                    sched = new("New Schedule", CMDType, [10, 0]);
                    break;
                default: return;
            }

            Schedule.Schedules.Add(sched);
            Data.SelectedSchedule = sched;
        }

        private void Delete(object sender, RoutedEventArgs e)
        {
            Schedule.Schedules.Remove(Data.SelectedSchedule);
            //Data.SelectedSchedule = null;
        }

        internal static void Save()
        {
            using var scheduleStream = new StreamWriter(MainWindow.SchedulePath, false);
            var json = JsonSerializer.Serialize(Schedule.Schedules, options: new()
            {
                WriteIndented = true,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            });
            scheduleStream.Write(json);
        }

        #region Search And Filter
        private void QuarySubmit(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (Data == null) return;
            var Filtered = Schedule.Schedules.Where(x => Filter(x));
            var Sel = lstDisplay.SelectedItem;
            lstDisplay.SelectedItem = null;
            Remove(Filtered);
            AddBack(Filtered);
            Data.UpdateAll();
            lstDisplay.SelectedItem = Sel;

            bool Filter(Schedule Cli)
            {
                return Cli.Name.Contains(sender.Text, StringComparison.InvariantCultureIgnoreCase) ||
                        Cli.Description.Contains(sender.Text, StringComparison.InvariantCultureIgnoreCase);
            }

            void Remove(IEnumerable<Schedule> filteredData)
            {
                foreach (var item in Schedule.Schedules)
                    if (!filteredData.Contains(item))
                        Data.FilterdSchedule.Remove(item);
            }

            void AddBack(IEnumerable<Schedule> filteredData)
            {
                List<Schedule> list = new();
                foreach (var item in filteredData)
                    if (!Data.FilterdSchedule.Contains(item))
                        list.Add(item);
                list.ForEach(Data.FilterdSchedule.Add);
            }
        }
        #endregion
    }
}
