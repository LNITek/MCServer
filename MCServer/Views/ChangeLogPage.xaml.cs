using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;

namespace MCServer.Views
{
    public partial class ChangeLogPage : Page
    {
        //MK 1.0
        public ChangeLogPage()
        {
            InitializeComponent();

            Setup();
        }

        private bool DisplayMode = true;

        private void Setup()
        {
            MainWindow.Window.SizeChanged += (s, e) => Root.MaxHeight = MainWindow.Window.ActualHeight - 32;
            Root.MaxHeight = MainWindow.Window.ActualHeight - 32;

            ChangeFile();
        }

        private void ChangeFile(object sender = null, RoutedEventArgs e = null)
        {
            DisplayMode = !DisplayMode;
            if (DisplayMode)
            {
                var FilePath = Path.GetFullPath(Path.GetFullPath("ChangeLog.md"));
                if (File.Exists(FilePath)) mdMain.Source = new(FilePath);
            }
            else
            {
                var FilePath = Path.GetFullPath(Path.Combine(MainWindow.ServerPath, "release-notes.txt"));
                if (File.Exists(FilePath)) mdMain.Source = new(FilePath);
            }
        }

        private void HyperlinkClick(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            var URL = e.Uri.ToString().Replace("&", "^&");
            Process.Start(new ProcessStartInfo("cmd", $"/c start {URL}") { CreateNoWindow = true });
            e.Handled = true;
        }
    }
}
