using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using HtmlAgilityPack;
using MCServer.Models;
using MCServer.Properties;
using MCServer.ViewModels;

namespace MCServer.Views
{
    /// <summary>
    /// Interaction logic for MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();

            Data = (MainVM)DataContext;
            Setup();
        }

        private MainVM Data;

        private void Setup()
        {
            MainWindow.Window.SizeChanged += (s, e) => Root.MaxHeight = MainWindow.Window.ActualHeight - 85;
            Root.MaxHeight = MainWindow.Window.ActualHeight - 85;
            WriteDisplay("Welcome To MCServer", Brushes.LightGray, false);
            Thread BaseThread = new(Start) { Name = "BaseThread", };
            
            Update().ContinueWith(x =>
            {
                Display.Document.Blocks.Clear();
                BaseThread.Start();
                Timer();
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private bool AutoScroll = true;
        public static bool Exited = true;
        public static Process ServerProcess = new();
        public static List<string> OutputList = new();
        public StreamWriter StreamInput;
        readonly string[] Files = new[] { "server.properties", "allowlist.json", "permissions.json"/*, "valid_known_packs.json"*/ };

        private void FastCMD(object sender, RoutedEventArgs e) =>
            CMD((sender as Wpf.Ui.Controls.MenuItem).Header.ToString().ToLower().Split(" ").ToList());

        private void EnterCMD(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var Args = edtInput.Text.ToLower().Split(" ").ToList();
            edtInput.Clear();

            CMD(Args);
        }

        private void CMD(List<string> Args)
        {
            switch (Args.First())
            {
                case "!":
                    new Thread(x => Stop((((PowerMode Mode, bool Backup))x).Mode, (((PowerMode Mode, bool Backup))x).Backup))
                    { Name = "StoperThread", }.Start((PowerMode.ForceShutdown, false)); break;
                case "exit" when Args.Count >= 2:
                    if (!Enum.TryParse(((PowerMode)Convert.ToInt32(Args[1])).ToString(), true, out PowerMode Mode))
                        Mode = PowerMode.Shutdown;
                    new Thread(x => Stop((((PowerMode Mode, bool Backup))x).Mode, (((PowerMode Mode, bool Backup))x).Backup))
                    { Name = "StoperThread", }.Start((Mode, true));
                    break;
                case "exit" when Args.Count < 2:
                    new Thread(x => Stop((((PowerMode Mode, bool Backup))x).Mode, (((PowerMode Mode, bool Backup))x).Backup))
                    { Name = "StoperThread", }.Start((PowerMode.Shutdown, true)); break;
                case "start" when Exited: new Thread(Start) { Name = "BaseThread", }.Start(); break;
                case "start": WriteDisplay("Server Is Already Running!", Brushes.Yellow); break;
                case "stop" when !Exited:
                    new Thread(x => Stop((((PowerMode Mode, bool Backup))x).Mode, (((PowerMode Mode, bool Backup))x).Backup))
                    { Name = "StoperThread", }.Start((PowerMode.None, false)); break;
                case "stop": WriteDisplay("Server Is Already Stoped!", Brushes.Yellow); break;
                case "restart":
                    if (!Exited) Task.Run(() => Stop(PowerMode.None, false)).ContinueWith(t =>
                    { Thread.Sleep(1000); new Thread(Start) { Name = "BaseThread", }.Start(); }, TaskContinuationOptions.ExecuteSynchronously);
                    else
                    { Thread.Sleep(1000); new Thread(Start) { Name = "BaseThread", }.Start(); }
                    break;
                case "backup": new Thread(Backup) { Name = "BackupThread", }.Start(); break;
                default: WriteLine(Args.First()); break;//WriteDisplay("Invalid Args : " + command);
            }
        }

        /// <summary>
        /// MK 1.0
        /// </summary>
        /// <returns>True On Sucess, Otherwise False.</returns>
        public async Task<bool> Update()
        {
            HtmlDocument doc = new HtmlWeb() { Timeout = (int)TimeSpan.FromSeconds(10).TotalMicroseconds }
                .Load("https://www.minecraft.net/en-us/download/server/bedrock");
            var Links = doc.DocumentNode.SelectNodes("//a[@href]");
            var URL = Links.Select(x => x.GetAttributeValue("href", ""))
                .FirstOrDefault(x => x.Contains("https://minecraft.azureedge.net/bin-win/bedrock-server")) ?? "";
            if (string.IsNullOrWhiteSpace(URL))
            {
                Dispatcher.Invoke(() => WriteDisplay("Update Error (URL): " + URL));
                return false;
            }
            var FileName = URL.Replace("https://minecraft.azureedge.net/bin-win/bedrock-server-", "").Replace(".zip","").Trim();
            if(FileName == Settings.Default.ServerMK)
            {
                Dispatcher.Invoke(() => WriteDisplay("UPDATE ALREADY INSTALLED..."));
                return true;
            }
            var BackupFolder = Path.Combine(MainWindow.ServerPath, "Backup");
            if (!Directory.Exists(BackupFolder))
                Directory.CreateDirectory(BackupFolder);
            foreach(var Item in Files)
            {
                Dispatcher.Invoke(() => WriteDisplay("BACKING UP: " + Item));
                var path = Path.Combine(MainWindow.ServerPath, Item);
                if (File.Exists(path)) File.Copy(path, Path.Combine(BackupFolder, Item), true);
            }

            Dispatcher.Invoke(() =>
            {
                WriteDisplay("DOWNLOADING: " + FileName);
                lblTitle.Text = "DOWNLOADING... " + FileName;
                Data.Proggess = 0;
                popProgress.IsOpen = true;
            });
            using var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(10), };
            using var file = await client.DownloadAsync(URL, new Progress<double>(I => Dispatcher.Invoke(() => Data.Proggess = I)));
            Data.Proggess = 100;
            Dispatcher.Invoke(() => popProgress.IsOpen = false);

            Dispatcher.Invoke(() =>
            {
                WriteDisplay("UPDATING SERVER FILES...");
                lblTitle.Text = "UPDATING SERVER FILES...";
                Data.Proggess = 0;
                popProgress.IsOpen = true;
            });
            var ZIP = new ZipArchive(file, ZipArchiveMode.Read);
            await ZIP.ExtractAsync(MainWindow.ServerPath, new Progress<double>(I => Dispatcher.Invoke(() => Data.Proggess = I)));
            Data.Proggess = 100;
            Dispatcher.Invoke(() => popProgress.IsOpen = false);

            foreach (var Item in Files)
            {
                Dispatcher.Invoke(() => WriteDisplay("RESTORING: " + Item));
                var path = Path.Combine(MainWindow.ServerPath, Item);
                if (File.Exists(Path.Combine(BackupFolder,Item))) 
                    File.Copy(Path.Combine(BackupFolder, Item), path, true);
            }

            using var Reader = new StreamReader(ZIP.Entries.First(x => x.Name == "server.properties").Open());
            var NewProp = new List<Property>();

            Dispatcher.Invoke(() => WriteDisplay("UPDATING SERVER PROPERTIES..."));
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
                NewProp.Add(new Property(Prop[0].Trim(), Prop[1].Trim(), Com));
            }

            Property.Properties.AddRange(NewProp.Where(el => !NewProp.Select(x => x.Name).Intersect(Property.Properties.Select(x => x.Name)).Contains(el.Name)));
            foreach(var x in Property.Properties.ToList())
            {
                var Prop = NewProp.Find(el => el.Name == x.Name);
                if (Prop == null) { Property.Properties.Remove(Prop); continue; }
                if (Prop.Desc != x.Desc) x.Comments = Prop.Comments;
            };

            using var Writer = File.CreateText(MainWindow.ServerPath + "\\server.properties");
            foreach (var prop in Property.Properties)
            {
                Writer.WriteLine("");
                Writer.WriteLine($"{prop.Name}={prop.Value}");
                prop.Comments.ForEach(x => Writer.WriteLine(x));
            }

            Settings.Default.ServerMK = FileName;
            Settings.Default.Save();
            Dispatcher.Invoke(() => WriteDisplay("Updated!"));
            Thread.Sleep(1000);
            return true;
        }

        /// <summary>
        /// MK 1.1
        /// </summary>
        public void Backup()
        {
            //Var
            bool Saved = false;
            List<string> Files = new();
            string sDate = " " + DateTime.Now.ToString("yyyy-MM-dd"), WorldName = "", Extension = "";
            int I = 0;

            WriteDisplay("Starting Backup...");
            WriteLine("say §9 Starting World Backup...");
            WriteLine("save hold");
            Thread.Sleep(1000);
            do
            {
                WriteLine("save query");
                Thread.Sleep(2000);
                var Index = OutputList.FindLastIndex(x => x.Contains("Data saved. Files are now ready to be copied.")) + 1;
                if (Index <= 0) continue;
                Files = OutputList[Index].Split(", ").Select(x => x.Split(':').First()).ToList();
                if (Files.Count > 0) Saved = true;
            } while (!Saved);
            WorldName = string.Join("", Files.First().TakeWhile(x => x != '/'));
            var SourcePath = MainWindow.ServerPath + "\\worlds\\" + WorldName + "\\";
            var BackupPath = string.IsNullOrWhiteSpace(Settings.Default.BackupPath) ?
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\MCBackups" :
                Settings.Default.BackupPath;
            var WorldPath = Path.Combine(MainWindow.ServerPath, "..", "Backups", WorldName);
            Files = Files.Select(x => x.Replace('/', '\\').Replace(WorldName, "")).ToList();

            if (Directory.Exists(WorldPath)) Directory.Delete(WorldPath, true);
            Directory.CreateDirectory(WorldPath); 
            if (!Directory.Exists(BackupPath)) Directory.CreateDirectory(BackupPath);

            BackupPath += "\\" + WorldName + sDate;
            while (File.Exists(BackupPath + Extension + ".zip"))
                Extension = $" ({I++})";

            foreach (var file in Files)
            {
                var FilePath = Path.GetDirectoryName(WorldPath + file);
                if (file.Contains('\\') && !Directory.Exists(FilePath))
                    Directory.CreateDirectory(FilePath);
                File.Copy(SourcePath + file, WorldPath + file);
            }

            WriteLine("save resume");
            ZipFile.CreateFromDirectory(WorldPath, BackupPath + Extension + ".zip", CompressionLevel.SmallestSize, false);
            WriteLine("say §9 Backup Completed!");
            WriteDisplay("Backup Completed");
        }

        /// <summary>
        /// MK 1.1
        /// </summary>
        public void Start()
        {
            WriteDisplay("Starting Server".ToUpper(), Brushes.LightGray);

            ServerProcess.StartInfo.UseShellExecute = false;
            ServerProcess.StartInfo.RedirectStandardInput = true;
            ServerProcess.StartInfo.RedirectStandardOutput = true;
            ServerProcess.StartInfo.FileName = Path.GetFullPath(Path.Combine(MainWindow.ServerPath,"bedrock_server.exe"));
            //ServerProcess.StartInfo.FileName = Path.Combine("C:\\Users\\Egbert\\Documents\\Visual Studio\\Visual C#\\MCServer\\Data\\Bedrock Server", "bedrock_server.exe");
            ServerProcess.StartInfo.StandardInputEncoding = Encoding.UTF8;
            ServerProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            ServerProcess.StartInfo.CreateNoWindow = true;

            ServerProcess.OutputDataReceived += Write;
            ServerProcess.Start();
            ServerProcess.BeginOutputReadLine();
            Exited = false;
            WriteLine("say §e Welcome To MCServer!");

            ServerProcess.WaitForExit();
            ServerProcess.CancelOutputRead();
            ServerProcess.OutputDataReceived -= Write;
            ServerProcess.Close();
            Exited = true;
            if (OutputList.Any(x => x.Contains("Exiting program") && x.Contains("ERROR")))
            {
                var Dump = new StreamWriter(Path.Combine(MainWindow.ServerPath, "..", "ERROR_DUMP.txt"));
                OutputList.ForEach(Dump.WriteLine);
                Dump.Close();
                OtherController.ThrowLog("BC-S01 | Server Terminated. Internal ERROR");
                WriteDisplay("Server Stoped With Error.", Brushes.Red);
            }
            else
                WriteDisplay("Server Stoped.", Brushes.LightGray);

            //OutputList.Clear();
            void Write(object sender, DataReceivedEventArgs e)
            {
                var Out = e.Data ?? "{NULL}";
                if (Out.Contains("ERROR"))
                    WriteDisplay(Out, Brushes.Red);
                else WriteDisplay(Out);
                OutputList.Add(Out);
            };
        }

        /// <summary>
        /// MK 1.1
        /// </summary>
        /// <param name="Mode">Mode For System Power Control.</param>
        /// <param name="CanBackup">Alow Backup First.</param>
        public void Stop(PowerMode Mode, bool CanBackup = true)
        {
            if (CanBackup) Backup();
            Thread.Sleep(10000);
            //Var
            int I = 0;
            string Colour = "§6", sMessage;
            bool Force = false;
            TimeSpan iSleep = new(0, 10, 0);
            TimeSpan iWait = new(0, 1, 0);
            //Code
            switch (Mode)
            {
                case PowerMode.None: sMessage = "Disconecting All Players"; Force = true; iWait = new(0, 0, 10); break;

                case PowerMode.Restart: sMessage = "Reastarting"; break;
                case PowerMode.Shutdown: sMessage = "Shuting Down"; break;
                case PowerMode.PowerOff: sMessage = "Cutting Power"; break;
                case PowerMode.LogOff: sMessage = "Disconecting All Players"; break;

                case PowerMode.ForceRestart: sMessage = "Emergency Reastart"; Force = true; iWait = new(0, 0, 10); break;
                case PowerMode.ForceShutdown: sMessage = "Emergency Shutdown"; Force = true; iWait = new(0, 0, 10); break;
                case PowerMode.ForcePowerOff: sMessage = "Emergency Power Cut"; Force = true; iWait = new(0, 0, 10); break;
                case PowerMode.ForceLogOff: sMessage = "Emergency Disconect"; Force = true; iWait = new(0, 0, 10); break;

                default: return;
            };

            string sTime = "Minutes";
            int iTime = iSleep.Minutes;
            if (Force) { iSleep = new(0, 0, 10); sTime = "Seconds"; iTime = iSleep.Seconds; };
            WriteDisplay($"Sending Server Warning : {sMessage}");
            WriteLine($"say {Colour} {sMessage} In {iTime} {sTime}...");
            Thread.Sleep(iSleep);
            WriteLine($"say {Colour} Turning Off...");
            Thread.Sleep(iWait);
            WriteLine("stop");
            do
            {
                Thread.Sleep(1000);
                I++;
            } while (!Exited && I < 120);
            if (!Exited)
            {
                WriteLine($"say §c Error Could Not Stop Server...");
                OtherController.ThrowLog("'BC-S01' | Could Not Stop Process");
                return;
            }
            PowerController.Shutdown(Mode);
        }

        private void Timer()
        {
            var Time = Settings.Default.ShutdownTime;
            MyScheduler.IntervalInDays(Time.Hours, Time.Minutes, 1,
            () => Stop((PowerMode)Settings.Default.ShutdownMode));
        }

        /// <summary>
        /// MK 1.0
        /// </summary>
        internal static void Stop()
        {
            //Var
            int I = 0;
            string Colour = "§6", sMessage = "Disconecting All Players";
            //Code

            WriteLine($"say {Colour} {sMessage} In 10 Seconds...");
            Thread.Sleep(TimeSpan.FromSeconds(10));
            WriteLine($"say {Colour} Turning Off...");
            Thread.Sleep(TimeSpan.FromSeconds(10));
            WriteLine("stop");
            do
            {
                Thread.Sleep(1000);
                I++;
            } while (!Exited && I < 120);
            if (!Exited)
            {
                WriteLine($"say §c Error Could Not Stop Server...");
                OtherController.ThrowLog("'BC-S01' | Could Not Stop Process");
                return;
            }
        }

        private static void WriteLine(string Line) => ServerProcess.StandardInput.WriteLine(Line);

        private void WriteDisplay(string Line, Brush color, bool NewLine = true)
        {
            Dispatcher?.Invoke(() =>
            {
                TextRange tr = new(Display.Document.ContentEnd, Display.Document.ContentEnd)
                { Text = (NewLine ? Environment.NewLine : " ") + Line };
                try { tr.ApplyPropertyValue(TextElement.ForegroundProperty, color); }
                catch (FormatException) { }
            });
        }

        private void WriteDisplay(string Line, bool NewLine = true)
        {
            Dispatcher?.Invoke(() =>
            {
                TextRange tr = new(Display.Document.ContentEnd, Display.Document.ContentEnd)
                { Text = (NewLine ? Environment.NewLine : " ") + Line };
                try { tr.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.White); }
                catch (FormatException) { }
            });
        }

        private void Display_AutoScroll(object sender, ScrollChangedEventArgs e)
        {
            ScrollViewer ScrollViewer = sender as ScrollViewer;
            if (ScrollViewer == null) return;
            // User scroll event : set or unset auto-scroll mode
            if (e.ExtentHeightChange == 0)
            {   // Content unchanged : user scroll event
                if (ScrollViewer.VerticalOffset == ScrollViewer.ScrollableHeight)
                {   // Scroll bar is in bottom
                    // Set auto-scroll mode
                    AutoScroll = true;
                }
                else
                {   // Scroll bar isn't in bottom
                    // Unset auto-scroll mode
                    AutoScroll = false;
                }
            }

            // Content scroll event : auto-scroll eventually
            if (AutoScroll && e.ExtentHeightChange != 0) ScrollViewer.ScrollToBottom();
        }
    }
}
