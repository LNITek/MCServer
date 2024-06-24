using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using ExtraFunctions.ExGenerators;
using MCServer.Models;
using MCServer.Properties;

namespace MCServer.Views
{
    public partial class SettingsPage : Page, INotifyPropertyChanged
    {
        public SettingsPage()
        {
            InitializeComponent();

            Unloaded += (s, e) => Settings.Default.Save();
            Setup();
        }

        //[NotifyChanged]
        //Wpf.Ui.Appearance.ThemeType _currentTheme = Wpf.Ui.Appearance.ThemeType.Unknown;
        [NotifyChanged]
        string backupPath { get => Settings.Default.BackupPath; set => Settings.Default.BackupPath = value; }

        [NotifyChanged]
        public string ServerMK => $"Current Server Version: " + Settings.Default.ServerMK;

        private void Setup()
        {
            //CurrentTheme = Wpf.Ui.Appearance.Theme.GetAppTheme();Assembly.GetExecutingAssembly().Location
#if DEBUG
            lblMK.Content = $"Software Version: {GetAssemblyVersion()} Pre-release";
#else
            lblMK.Content = $"Software Version: {GetAssemblyVersion()}";
#endif
            var FilePath = Path.GetFullPath(Path.Combine("Assets", "LNI.Tek Logo.png"));
            if (File.Exists(FilePath)) imgLogo.Source = new BitmapImage(new(FilePath));
        }

        private string GetAssemblyVersion() =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        [RelayCommand]
        private void OnChangeTheme(string parameter)
        {
            /*switch (parameter)
            {
                case "theme_light":
                    if (CurrentTheme == Wpf.Ui.Appearance.ThemeType.Light)
                        break;

                    Wpf.Ui.Appearance.Theme.Apply(Wpf.Ui.Appearance.ThemeType.Light);
                    CurrentTheme = Wpf.Ui.Appearance.ThemeType.Light;

                    break;

                default:
                    if (CurrentTheme == Wpf.Ui.Appearance.ThemeType.Dark)
                        break;

                    Wpf.Ui.Appearance.Theme.Apply(Wpf.Ui.Appearance.ThemeType.Dark);
                    CurrentTheme = Wpf.Ui.Appearance.ThemeType.Dark;

                    break;
            }*/
        }

        private void OpenFolder(object sender, System.Windows.RoutedEventArgs e)
        {
            var fbdBackup = new FolderBrowserDialog
            { SelectedPath = BackupPath, };
            if (fbdBackup.ShowDialog() != DialogResult.OK) return;

            BackupPath = fbdBackup.SelectedPath;
        }
    }
}
