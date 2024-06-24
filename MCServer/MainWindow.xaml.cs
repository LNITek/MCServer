using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using ExtraFunctions.Extras;
using MCServer.Models;
using MCServer.Properties;
using MCServer.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace MCServer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        public static MainWindow Window { get; private set; }

        public MainWindow(INavigationService navigationService, IServiceProvider serviceProvider, ISnackbarService snackbarService, IContentDialogService contentDialogService)
        {
            DataContext = this;

            InitializeComponent();

            navigationService.SetNavigationControl(navMain);
            snackbarService.SetSnackbarPresenter(SnackbarPresenter);
            navMain.SetServiceProvider(serviceProvider);
            if(!File.Exists(Path.Combine(ServerPath, "bedrock_server.exe")))
            {
                Settings.Default.ServerMK = "0";
                Settings.Default.Save();
            }

            Setup();
            Window = this;

            new Thread(DDNSPage.Setup) { Name = "DDNSThread", IsBackground = true }.Start();
        }

#if DEBUG
        public const string ServerPath = "..\\..\\..\\..\\Data\\Bedrock Server\\";
#else
        public static string ServerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCServer\\Bedrock Server\\");
#endif
        public readonly static string SchedulePath = Path.Combine(ServerPath, "..", "Schedule.json");

        private void Setup()
        {
            StateChanged += (s, e) =>
            { if (WindowState != WindowState.Minimized) Settings.Default.Maximized = WindowState == WindowState.Maximized; };
            Closing += (s, e) =>
            {
                if (MainPage.Exited) Application.Current.Shutdown();
                NotifyUser("Close", "Can Not Close The App While The Server Is Running!", ControlAppearance.Danger);
                e.Cancel = true;
            };
            if (Settings.Default.Maximized) WindowState = WindowState.Maximized;

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            };
            Directory.CreateDirectory(Path.Combine(ServerPath, "LOGS"));
            if (!File.Exists(SchedulePath))
            {
                using var fileStream = File.Create(SchedulePath);
                var json = JsonSerializer.Serialize(Schedule.Schedules, options: jsonOptions );
                fileStream.Write(json.Select(x => (byte)x).ToArray());
            }
            else using (var scheduleStream = new StreamReader(SchedulePath))
                {
                    var schedule = JsonSerializer.Deserialize<IEnumerable<Schedule>>(scheduleStream.ReadToEnd(), options: jsonOptions);
                    Schedule.Schedules.AddRange(schedule);
                };

            if (File.Exists(ServerPath + "\\server.properties"))
                using (var Reader = new StreamReader(ServerPath + "\\server.properties"))
                    while (!Reader.EndOfStream)
                    {
                        var Line = Reader.ReadLine();
                        while (string.IsNullOrWhiteSpace(Line))
                            Line = Reader.ReadLine();
                        var Prop = Line.Split('=');
                        var Com = new List<string>();
                        Line = Reader.ReadLine();
                        while (!string.IsNullOrWhiteSpace(Line) && Line.StartsWith('#'))
                        {
                            Com.Add(Line);
                            Line = Reader.ReadLine();
                        }
                        Property.Properties.Add(new Property(Prop[0].Trim(), Prop[1].Trim(), Com));
                    }
        }

        public void NotifyUser(string Title, string Message, ControlAppearance Appearance)
        {
            SnackbarPresenter.AddToQue(new(SnackbarPresenter) 
            { Title = Title, Content = Message, Appearance = Appearance, Timeout = TimeSpan.FromSeconds(10) });
        }

        private void navMainSelect(object sender, RoutedEventArgs args)
        {
            if (sender is NavigationViewItem SelectedItem && (SelectedItem.TargetPageTag != null))
            {
                Type newPage = Type.GetType($"{typeof(MainWindow).Namespace}.Views." + SelectedItem.TargetPageTag) ??
                    throw new DirectoryNotFoundException("Could Not Find Page");
                navMain.Navigate(newPage);
            }
        }

        private void frmNav_Navigated(NavigationView sender, NavigatedEventArgs e)
        {
            if (e.Page != null)
            {
                navMain.MenuItems.OfType<NavigationViewItem>().ForEach(x => x.IsActive = false);
                navMain.FooterMenuItems.OfType<NavigationViewItem>().ForEach(x => x.IsActive = false);

                var Item = navMain.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(n => n.TargetPageTag.Equals(e.Page.GetType().Name));
                if(Item != null) { Item.IsActive = true; return; }
                Item = navMain.FooterMenuItems.OfType<NavigationViewItem>().FirstOrDefault(n => n.TargetPageTag.Equals(e.Page.GetType().Name));
                if (Item != null) { Item.IsActive = true; return; }
            }

            navMain.Header = navMain.SelectedItem?.Content?.ToString();
        }
    }
}
