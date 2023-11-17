using System.Diagnostics;
using System.IO;
using System.Security.Policy;
using System.Windows;
using System.Windows.Controls;
using MCServer.Models;
using MCServer.ViewModels;
using Wpf.Ui.Controls;

namespace MCServer.Views
{
    /// <summary>
    /// Interaction logic for PropertiesPage.xaml
    /// </summary>
    public partial class PropertiesPage : Page
    {
        /// <summary>
        /// MK 1.0
        /// </summary>
        public PropertiesPage()
        {
            InitializeComponent();

            Data = (PropertiesVM)DataContext;
            Setup();
        }

        private PropertiesVM Data;

        public void Setup()
        {
            MainWindow.Window.SizeChanged += (s, e) => Root.MaxHeight = MainWindow.Window.ActualHeight - 50;
            Root.MaxHeight = MainWindow.Window.ActualHeight - 50;

            if (!File.Exists(MainWindow.ServerPath + "\\server.properties"))
            {
                MainWindow.Window.NotifyUser("Properties", "Could Not Find Properties File!", ControlAppearance.Danger);
                return;
            }
        }

        private void OpenProps(object sender, RoutedEventArgs e)
        {
            if (File.Exists(MainWindow.ServerPath + "\\server.properties"))
                Process.Start(new ProcessStartInfo(Path.GetFullPath(MainWindow.ServerPath + "\\server.properties")) 
                { UseShellExecute = true });
            else
                Process.Start("explorer", Path.GetDirectoryName(MainWindow.ServerPath));
        }

        private void SaveValue(object sender, TextChangedEventArgs e)
        {
            if (!File.Exists(MainWindow.ServerPath + "\\server.properties"))
            {
                MainWindow.Window.NotifyUser("Error", "Could Not Find Properties File!", ControlAppearance.Danger);
                return;
            }
            using var Writer = File.CreateText(MainWindow.ServerPath + "\\server.properties");
            foreach (var prop in Property.Properties)
            {
                Writer.WriteLine("");
                Writer.WriteLine($"{prop.Name}={prop.Value}");
                prop.Comments.ForEach(Writer.WriteLine);
            }
        }

        #region Search And Filter
        private void QuarySubmit(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (Data == null) return;
            var Filtered = Property.Properties.Where(x => Filter(x));
            var Sel = lstDisplay.SelectedItem;
            lstDisplay.SelectedItem = null;
            Remove(Filtered);
            AddBack(Filtered);
            Data.UpdateAll();
            lstDisplay.SelectedItem = Sel;

            bool Filter(Property Cli)
            {
                return Cli.Name.Contains(sender.Text, StringComparison.InvariantCultureIgnoreCase) ||
                        Cli.Desc.Contains(sender.Text, StringComparison.InvariantCultureIgnoreCase) ||
                        Cli.Value.Contains(sender.Text, StringComparison.InvariantCultureIgnoreCase);
            }

            void Remove(IEnumerable<Property> filteredData)
            {
                foreach (var item in Property.Properties)
                    if (!filteredData.Contains(item))
                        Data.FilterdProps.Remove(item);
            }

            void AddBack(IEnumerable<Property> filteredData)
            {
                List<Property> list = new();
                foreach (var item in filteredData)
                    if (!Data.FilterdProps.Contains(item))
                        list.Add(item);
                list.ForEach(Data.FilterdProps.Add);
            }
        }
        #endregion
    }
}
