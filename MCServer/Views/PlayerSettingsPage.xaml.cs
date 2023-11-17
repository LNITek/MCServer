using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MCServer.Models;
using MCServer.ViewModels;
using Wpf.Ui.Controls;

namespace MCServer.Views
{
    /// <summary>
    /// Interaction logic for PlayerSettingsPage.xaml
    /// </summary>
    public partial class PlayerSettingsPage : Page
    {
        /// <summary>
        /// MK 1.0
        /// </summary>
        public PlayerSettingsPage()
        {
            InitializeComponent();

            Data = (PlayerVM)DataContext;
            Setup();
        }

        private PlayerVM Data;

        private void Setup()
        {
            cmbPermission.ItemsSource = Enum.GetValues(typeof(PlayerPermission)).Cast<PlayerPermission>();

            //lstDisplay.PreviewMouseMove += DragOver;
            GetAllowList();
            GetPermission();
            Data.FilterdPlayers.Clear();
            Data.FilterdPlayers.AddRange(Data.Players);

            void GetAllowList()
            {
                if (!File.Exists(MainWindow.ServerPath + "\\allowlist.json"))
                {
                    MainWindow.Window.NotifyUser("Error", "Could Not Find Allowlist File!", ControlAppearance.Danger);
                    return;
                }
                var JsonData = JsonSerializer.Deserialize<AllowList[]>
                    (File.Open(MainWindow.ServerPath + "\\allowlist.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                foreach (var Item in JsonData)
                {
                    if(Item.name == null || Item.xuid == null) continue;
                    var Play = Data.Players.Find(x => x.Name == Item.name);
                    if(Play != null)
                    {
                        Play.AllowList = true;
                        Play.Name = Item.name;
                        Play.IgnoresPlayerLimit = Item.ignoresPlayerLimit;
                        if(Play.Xuid != Item.xuid && !string.IsNullOrWhiteSpace(Item.xuid)) 
                            Play.Xuid = Item.xuid;
                    }
                    else Data.Players.Add(new(Item.name, Item.xuid) { AllowList = true });
                }
            }
            void GetPermission()
            {
                if (!File.Exists(MainWindow.ServerPath + "\\permissions.json"))
                {
                    MainWindow.Window.NotifyUser("Error", "Could Not Find Permissions File!", ControlAppearance.Danger);
                    return;
                }
                var JsonData = JsonSerializer.Deserialize<Permission[]>
                    (File.Open(MainWindow.ServerPath + "\\permissions.json", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                foreach (var Item in JsonData)
                {
                    if (Item.permission == null || Item.xuid == null) continue;
                    var Play = Data.Players.Find(x => x.Xuid == Item.xuid);
                    if (Play != null)
                    {
                        Play.Permission = Enum.Parse<PlayerPermission>(Item.permission, true);
                        if (Play.Xuid != Item.xuid && !string.IsNullOrWhiteSpace(Item.xuid))
                            Play.Xuid = Item.xuid;
                    }
                    else Data.Players.Add(new("Player", Item.xuid, Enum.Parse<PlayerPermission>(Item.permission, true)));
                }
            }
        }

        private void OpenFile(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer", Path.GetDirectoryName(MainWindow.ServerPath));
        }

        private void Add(object sender, RoutedEventArgs e)
        {
            var Play = new Player();
            Data.Players.Add(Play);
            Data.FilterdPlayers.Add(Play);
            lstDisplay.SelectedItem = Play;
        }

        private void Delete(object sender, RoutedEventArgs e)
        {
            Data.FilterdPlayers.Remove(Data.SelectedPlayer);
            Data.Players.Remove(Data.SelectedPlayer);
        }

        private void PermissionChanged(object sender, SelectionChangedEventArgs e) => SaveValue();

        private void PropsToggled(object sender, RoutedEventArgs e) => SaveValue();

        private void SaveValue(object sender = null, TextChangedEventArgs e = null)
        {
            AllowListEdit();
            PermissionEdit();

            void AllowListEdit()
            {
                
                if (!File.Exists(MainWindow.ServerPath + "\\allowlist.json"))
                {
                    MainWindow.Window.NotifyUser("Error", "Could Not Find Allowlist File!", ControlAppearance.Danger);
                    return;
                }
                var JsonData = JsonSerializer.Serialize(Data.Players.Where(x => x.AllowList).Select(x => x.ToAllowList()).ToArray());
                File.WriteAllText(MainWindow.ServerPath + "\\allowlist.json", JsonData);
            }
            void PermissionEdit()
            {
                if (!File.Exists(MainWindow.ServerPath + "\\permissions.json"))
                {
                    MainWindow.Window.NotifyUser("Error", "Could Not Find Permissions File!", ControlAppearance.Danger);
                    return;
                }
                var JsonData = JsonSerializer.Serialize(Data.Players.Where(x => x.IsVerifide && x.Permission != PlayerPermission.Default)
                    .Select(x => x.ToPermission()).ToArray());
                File.WriteAllText(MainWindow.ServerPath + "\\permissions.json", JsonData);
            }
        }

        #region Search And Filter
        private void QuarySubmit(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (Data == null) return;
            var Filtered = Data.Players.Where(x => Filter(x));
            var Sel = lstDisplay.SelectedItem;
            lstDisplay.SelectedItem = null;
            Remove(Filtered);
            AddBack(Filtered);
            Data.UpdateAll();
            lstDisplay.SelectedItem = Sel;

            bool Filter(Player Cli)
            {
                return Cli.Name.Contains(sender.Text, StringComparison.InvariantCultureIgnoreCase) ||
                        Cli.Xuid.Contains(sender.Text, StringComparison.InvariantCultureIgnoreCase);
            }

            void Remove(IEnumerable<Player> filteredData)
            {
                foreach (var item in Data.Players)
                    if (!filteredData.Contains(item))
                        Data.FilterdPlayers.Remove(item);
            }

            void AddBack(IEnumerable<Player> filteredData)
            {
                List<Player> list = new();
                foreach (var item in filteredData)
                    if (!Data.FilterdPlayers.Contains(item))
                        list.Add(item);
                list.ForEach(Data.FilterdPlayers.Add);
            }
        }
        #endregion

        #region Drag & Drop
        private Point _dragStartPoint;

        private T FindVisualParent<T>(DependencyObject child)
            where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null)
                return null;
            T parent = parentObject as T;
            if (parent != null)
                return parent;
            return FindVisualParent<T>(parentObject);
        }

        private new void DragOver(object sender, MouseEventArgs e)
        {
            Point point = e.GetPosition(null);
            Vector diff = _dragStartPoint - point;
            if (e.LeftButton == MouseButtonState.Pressed &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                var lbi = FindVisualParent<ListBoxItem>(((DependencyObject)e.OriginalSource));
                if (lbi != null)
                {
                    DragDrop.DoDragDrop(lbi, lbi.DataContext, DragDropEffects.Move);
                }
            }
        }

        private void Drag(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private new void Drop(object sender, DragEventArgs e)
        {
            if (sender is ListBoxItem)
            {
                var source = e.Data.GetData(typeof(Player)) as Player;
                var target = ((ListBoxItem)(sender)).DataContext as Player;

                int sourceIndex = lstDisplay.Items.IndexOf(source);
                int targetIndex = lstDisplay.Items.IndexOf(target);

                Move(source, sourceIndex, targetIndex);
            }
        }

        private void Move(Player source, int sourceIndex, int targetIndex)
        {
            if (sourceIndex < targetIndex)
            {
                Data.FilterdPlayers.Move(sourceIndex, targetIndex + 1);
                Data.Players.Insert(targetIndex + 1, source);
                Data.Players.RemoveAt(sourceIndex);
            }
            else
            {
                int removeIndex = sourceIndex + 1;
                if (Data.Players.Count + 1 > removeIndex)
                {
                    Data.FilterdPlayers.Move(removeIndex, targetIndex);
                    Data.Players.Insert(targetIndex, source);
                    Data.Players.RemoveAt(removeIndex);
                }
            }
        }
        #endregion
    }
}
