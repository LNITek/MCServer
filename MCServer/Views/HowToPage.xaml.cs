using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ExtraFunctions.ExGenerators;

namespace MCServer.Views
{
    public partial class HowToPage : Page, INotifyPropertyChanged
    {
        //MK 1.0
        public HowToPage()
        {
            InitializeComponent();

            Setup();
        }

        [NotifyChanged]
        public Visibility DisplayMain => DisplayMode ? Visibility.Collapsed : Visibility.Visible;
        [NotifyChanged]
        public Visibility DisplayReadMe => DisplayMode ? Visibility.Visible : Visibility.Collapsed;
        [NotifyChanged(new[] { nameof(DisplayMain), nameof(DisplayReadMe) })]
        bool displayMode { get; set; } = false;

        private void Setup()
        {
            MainWindow.Window.SizeChanged += (s, e) => Root.MaxHeight = MainWindow.Window.ActualHeight - 32;
            Root.MaxHeight = MainWindow.Window.ActualHeight - 32;

            var FilePath = Path.GetFullPath(Path.Combine(MainWindow.ServerPath, "bedrock_server_how_to.html"));
            if (File.Exists(FilePath)) wbrMain.Source = new(FilePath);
            else wbrMain.Source = new(Path.GetFullPath("Assets\\HowTo.html"));

            FilePath = Path.GetFullPath(Path.GetFullPath("README.md"));
            if (File.Exists(FilePath)) mdMain.Source = new(FilePath);
        }

        private void HyperlinkClick(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            var URL = e.Uri.ToString().Replace("&", "^&");
            Process.Start(new ProcessStartInfo("cmd", $"/c start {URL}") { CreateNoWindow = true });
            e.Handled = true;
        }
    }
}
