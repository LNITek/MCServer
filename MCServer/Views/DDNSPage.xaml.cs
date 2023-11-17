using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ExtraFunctions.ExGenerators;
using ExtraFunctions.Extras;
using MCServer.Models;

namespace MCServer.Views
{
    /// <summary>
    /// Interaction logic for DDNSPage.xaml
    /// </summary>
    public partial class DDNSPage : Page, INotifyPropertyChanged
    {
        /// <summary>
        /// MK 1.0
        /// </summary>
        public DDNSPage()
        {
            InitializeComponent();

            cmbDDNS.ItemsSource = DDNS.DDNSList;

            cmbDDNS.SelectedIndex = Properties.Settings.Default.DDNSType;
            edtUser.Text = Properties.Settings.Default.UserName;
            edtPass.Password = Properties.Settings.Default.Password;
            edtSub.Text = Properties.Settings.Default.SubDNS;
            ntdInterval.Value = Properties.Settings.Default.Interval;

            StatusList.CollectionChanged += (s, e) => UpdateAll();
        }

        internal static Timer Timer;
        public static ExList<string> StatusList { get; } = new() { "Curently Not Running.", "Last Updated: -Unknown" };
        [NotifyChanged]
        public string Status { get => string.Join("\r\n",StatusList); }

        internal static void Setup()
        {
            if (Properties.Settings.Default.DDNSType <= 0) return;
            var Errors = Validate();
            if (Errors == null || Errors.Any())
            {
                MainWindow.Window.NotifyUser("Dynamic DNS", string.Join("\r\n", Errors), Wpf.Ui.Controls.ControlAppearance.Danger);
                return;
            }

            Timer = new Timer(x => UpdateDDNS(), null, TimeSpan.Zero, TimeSpan.FromMinutes(Properties.Settings.Default.Interval));
            MainWindow.Window.Dispatcher.Invoke(() => MainWindow.Window.NotifyUser("Dynamic DNS", "Dynamic DNS Started.", Wpf.Ui.Controls.ControlAppearance.Success));
        }

        private static IEnumerable<string> Validate()
        {
            if (Properties.Settings.Default.DDNSType < 0) yield return "No Dynamic DNS Provider Selected!";
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.UserName)) yield return "Invalid User Name!";
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.Password)) yield return "Invalid Password!";
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.SubDNS)) yield return "Invalid Sub Domain!";
            if (Properties.Settings.Default.Interval <= 0) yield return "Interval Can Not Be Less Than 0!";
        }

        private static void UpdateDDNS()
        {
            try
            {
                var DDNSItem = DDNS.Find(Properties.Settings.Default.DDNSType);
                if (DDNSItem == null)
                {
                    MainWindow.Window.NotifyUser("Dynamic DNS", "Invalid Dynamic DNS Provider!", Wpf.Ui.Controls.ControlAppearance.Danger);
                    return;
                }
                var client = new HttpClient(new SocketsHttpHandler()
                { Credentials = new NetworkCredential(Properties.Settings.Default.UserName, Properties.Settings.Default.Password) })
                { Timeout = TimeSpan.FromMinutes(10), };

                var res = client.GetAsync(DDNSItem.GetURL(Properties.Settings.Default.SubDNS)).Result;
                res.EnsureSuccessStatusCode();
                var response = res.Content.ReadAsStringAsync().Result;
                StatusList[0] = $"IP: {Regex.Replace(response,"[a-zA-Z\\s]","")}";
                StatusList[1] = "Last Updated: " + DateTime.Now.ToString();
            }
            catch (HttpRequestException)
            {
                //Dispatcher.Invoke(() => MainWindow.Window.NotifyUser("Dynamic DNS", $"Error: ({ex.StatusCode}) {ex.Message}", Wpf.Ui.Controls.ControlAppearance.Danger));
                StatusList[0] = "Connection Failed: " + DateTime.Now.ToString();
            }
        }

        private void SetDDNS(object sender, RoutedEventArgs e)
        {
            if (cmbDDNS.SelectedIndex <= 0)
            {
                cmbDDNS.SelectedIndex = 0;
                edtUser.Text = "";
                edtPass.Password = "";
                edtSub.Text = "";
                ntdInterval.Value = 60;
            }
            Properties.Settings.Default.DDNSType = cmbDDNS.SelectedIndex;
            Properties.Settings.Default.UserName = edtUser.Text;
            Properties.Settings.Default.Password = edtPass.Password;
            Properties.Settings.Default.SubDNS = edtSub.Text;
            Properties.Settings.Default.Interval = ntdInterval.Value ?? 0;
            Properties.Settings.Default.Save();

            var Errors = Validate();
            if (Errors == null || Errors.Any())
            {
                MainWindow.Window.NotifyUser("Dynamic DNS", string.Join("\r\n", Errors), Wpf.Ui.Controls.ControlAppearance.Danger);
                return;
            }

            if (Properties.Settings.Default.DDNSType <= 0) { Timer.Dispose(); Timer = null; return; }
            if (Timer == null) { Setup(); return; }
            if (!Timer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(Properties.Settings.Default.Interval)))
                MainWindow.Window.NotifyUser("Dynamic DNS", "Could Not Update Interval.", Wpf.Ui.Controls.ControlAppearance.Danger);
        }
    }
}
