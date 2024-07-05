using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ExtraFunctions.Extras;
using HtmlAgilityPack;
using MCServer.Helpers;
using MCServer.Models;
using MCServer.Properties;
using MCServer.ViewModels;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Security;
using Windows.Win32.System.JobObjects;
using ICommand = MCServer.Models.ICommand;

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
            OutputList.CollectionChanged += (s, e) =>
            {
                if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                    return;

                var Dump = new StreamWriter(Path.Combine(MainWindow.ServerPath, "LOGS", $"Log {StartupDate:yyyy-MM-dd}.txt"), true);
                e.OldItems.Cast<string>().ForEach(Dump.WriteLine);
                Dump.Close();
            };

            MainWindow.Window.SizeChanged += (s, e) => Root.MaxHeight = MainWindow.Window.ActualHeight - 140;
            Root.MaxHeight = MainWindow.Window.ActualHeight - 140;
            WriteDisplay("Welcome To MCServer", Brushes.LightGray, false);
            popProgress.PlacementTarget = Display;

            FastCommands.AddRange([
                new BackupCommand("Backup") { Action = () => CreateThread(() => Backup().Wait()) },
                new StartCommand("Start") { Action = StartServer },
                new RestartCommand("Restart") { Action = () => CreateThread(() => Restart().Wait()) },
                new StopCommand("Stop", new(0,0,10)) { Action = (x) => CreateThread(() => Stop(x).Wait()) },
                new ExitCommand("Exit", new(0,0,10)) { Action = (x) => CreateThread(() => Exit(x).Wait()) },
                new PowerCommand("Shutdown", PowerMode.Shutdown, new(0,0,10)) { Action = (x,I) => CreateThread(() => Power(x,I).Wait()) },
                new PowerCommand("Power Cycle", PowerMode.Restart, new(0,0,10)) { Action = (x,I) => CreateThread(() => Power(x,I).Wait()) },
                ]);

            //return;
            Update().ContinueWith(x =>
            {
                Display.Document.Blocks.Clear();
                Data.CMDRuning = false;
                StartServer();
                var DNow = DateTime.Now.TimeOfDay.Seconds;
                ScheduleTimer = new((x) => RunSchedule(), null,
                    (int)new TimeSpan(0, 0, 60 - DNow).TotalMilliseconds,
                    (int)TimeSpan.FromMinutes(1).TotalMilliseconds);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private static void CreateThread(Action action) =>
            new Thread(new ThreadStart(action)).Start();

        private static Semaphore Que = new(1, 1, "ControllerThreadQue");
        Thread ServerThread;
        Timer ScheduleTimer;
        DateTime StartupDate;
        private bool AutoScroll = true;
        public static bool Exited = true;
        public static SafeFileHandle jobHandle;
        public static Process ServerProcess = new();
        public static StackList<string> OutputList = new(100);
        public StreamWriter StreamInput;
        readonly string[] Files = ["server.properties", "allowlist.json", "permissions.json"/*, "valid_known_packs.json"*/];
        List<ICommand> FastCommands = [];

        private void FastCMD(object sender, RoutedEventArgs e)
        {
            var CMD = (sender as Wpf.Ui.Controls.MenuItem).Header.ToString();

            FastCommands.Find(x => x.Name.Equals(CMD, StringComparison.CurrentCultureIgnoreCase))?.Execute();
        }

        private void EnterCMD(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            CMD(edtInput.Text.Trim());
            edtInput.Text = "";
        }

        private void CMD(string command)
        {
            try
            {
                var (CMD, Params) = Command.Parse(command);

                switch (CMD.Name.ToLower())
                {
                    case "!": CreateThread(() => Power(TimeSpan.FromSeconds(10), PowerMode.Shutdown).Wait()); break;
                    case "backup": CreateThread(() => Backup().Wait()); break;
                    case "start": StartServer(); break;
                    case "restart": CreateThread(() => Restart().Wait()); break;
                    case "stop": CreateThread(() => Stop(TimeSpan.FromSeconds((int)Params[0])).Wait()); break;
                    case "exit": CreateThread(() => Exit(TimeSpan.FromSeconds((int)Params[0])).Wait()); break;
                    case "power": CreateThread(() => Power(TimeSpan.FromSeconds((int)Params[0]), (PowerMode)Params[1]).Wait()); break;
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.StartsWith("C01"))
                    WriteLine(command);
                else
                    MainWindow.Window.NotifyUser("Invalid Command!", ex.Message, Wpf.Ui.Controls.ControlAppearance.Danger);
            }
        }

        private void RunSchedule()
        {
            var DNow = DateTime.Now;
            var Schedules = Schedule.Schedules.Where(x => x.Enabled && x.StartDate <= DNow.Date &&
                x.WeekDays.Contains(DNow.DayOfWeek) && x.Time.Hours == DNow.Hour && x.Time.Minutes == DNow.Minute);

            foreach (var item in Schedules)
            {
                item.LastRun = DateTime.Now;
                CMD(item.ToString());
                if (item.Ones) item.Enabled = false;
                Thread.Sleep(1000);
            }

            if (Schedules.Any())
                SchedulePage.Save();
            return;
        }

        public async Task<bool> Update()
        {
            Que.WaitOne();
            Dispatcher.Invoke(() => Data.CMDRuning = true);
            if (!Exited)
            {
                Dispatcher?.Invoke(() => MainWindow.Window.NotifyUser("Update", "Server is running! Stop The Server To Update.", Wpf.Ui.Controls.ControlAppearance.Danger));
                Que.Release();
                return false;
            }
            HtmlDocument doc = new HtmlWeb() { Timeout = (int)TimeSpan.FromSeconds(10).TotalMicroseconds }
                .Load("https://www.minecraft.net/en-us/download/server/bedrock");
            var Links = doc.DocumentNode.SelectNodes("//a[@href]");
            var URL = Links.Select(x => x.GetAttributeValue("href", ""))
                .FirstOrDefault(x => x.Contains("https://minecraft.azureedge.net/bin-win/bedrock-server")) ?? "";
            if (string.IsNullOrWhiteSpace(URL))
            {
                Dispatcher.Invoke(() => { WriteDisplay("Update Error (URL): " + URL); Data.CMDRuning = false; });
                Que.Release();
                return false;
            }
            var FileName = URL.Replace("https://minecraft.azureedge.net/bin-win/bedrock-server-", "").Replace(".zip","").Trim();
            if(FileName == Settings.Default.ServerMK)
            {
                Dispatcher.Invoke(() => { WriteDisplay("UPDATE ALREADY INSTALLED..."); Data.CMDRuning = false; });
                Que.Release();
                return true;
            }
            var BackupFolder = Path.Combine(MainWindow.ServerPath, "Backup");
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
            List<string> Prop = [];
            var Com = new List<string>();
            while (!Reader.EndOfStream)
            {
                var Line = Reader.ReadLine();
                while (string.IsNullOrWhiteSpace(Line) && !Reader.EndOfStream)
                    Line = Reader.ReadLine();
                if (Line?.StartsWith("# ") ?? false)
                {
                    Com.Add(Line);
                    continue;
                }
                if (Prop.Count != 0)
                {
                    var PName = Prop[0].Trim();
                    Prop.RemoveAt(0);
                    if (Prop.Count == 0) Prop.Add("");
                    if (Com.Count == 0) Com.Add("");
                    NewProp.Add(new Property(PName, string.Join("", Prop.Select(x => x.Trim())), [.. Com]));
                    Prop.Clear();
                    Com.Clear();
                }

                if(!string.IsNullOrWhiteSpace(Line))
                    Prop.AddRange(Line.Split("="));
            }

            Property.Properties.AddRange(NewProp.Where(el => !NewProp.Select(x => x.Name).Intersect(Property.Properties.Select(x => x.Name)).Contains(el.Name)));
            foreach(var x in Property.Properties.ToList())
            {
                var Pro = NewProp.Find(el => el.Name == x.Name);
                if (Pro == null) { Property.Properties.Remove(Pro); continue; }
                if (Pro.Desc != x.Desc) x.Comments = Pro.Comments;
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
            Dispatcher.Invoke(() => { WriteDisplay("Updated!"); Data.CMDRuning = false; });
            Que.Release();
            return true;
        }

        public async Task Backup()
        {
            Que.WaitOne();
            if (Exited)
            {
                Dispatcher?.Invoke(() => MainWindow.Window.NotifyUser("Backup", "Server is not running!", Wpf.Ui.Controls.ControlAppearance.Danger));
                Que.Release();
                return;
            }
            //Var
            bool Saved = false;
            List<string> Files = new();
            string sDate = " " + DateTime.Now.ToString("yyyy-MM-dd"), WorldName = "", Extension = "";
            int I = 0;
            DateTime Timer = DateTime.Now;
            //Code

            Dispatcher?.Invoke(() => Data.CMDRuning = true);
            WriteDisplay("Starting Backup...");
            WriteLine("say §9Starting World Backup...");
            WriteLine("save hold");
            await Task.Delay(1000);
            do
            {
                WriteLine("save query");
                await Task.Delay(2000);
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

            await Task.Delay(1000);
            var Time = (DateTime.Now - Timer);
            WriteLine($"say §9Backup Completed In ({Time})");
            WriteDisplay($"Backup Completed In ({Time})");
            Dispatcher?.Invoke(() => Data.CMDRuning = false);            
            Que.Release();
        }

        unsafe private void Start()
        {
            StartupDate = DateTime.Today;
            if (Data.CMDRuning) return;
            if (!Exited)
            {
                Dispatcher?.Invoke(() => MainWindow.Window.NotifyUser("Start","Server is allready running!", Wpf.Ui.Controls.ControlAppearance.Caution)); 
                return;
            }
            Dispatcher?.Invoke(() =>
            {
                Data.CMDRuning = true;
                Data.ServerStatus = true;
                Exited = false;
            });
            WriteDisplay("Starting Server".ToUpper(), Brushes.LightGray);

            var atts = new SECURITY_ATTRIBUTES
            {
                bInheritHandle = false,
                lpSecurityDescriptor = (void*)null,
                nLength = (uint)Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            };

            jobHandle = PInvoke.CreateJobObject(atts, lpName: null);
            if(jobHandle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                },
            };

            if(!PInvoke.SetInformationJobObject(jobHandle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,&info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (!PInvoke.AssignProcessToJobObject(jobHandle, Process.GetCurrentProcess().SafeHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            ServerProcess.StartInfo.UseShellExecute = false;
            ServerProcess.StartInfo.RedirectStandardInput = true;
            ServerProcess.StartInfo.RedirectStandardOutput = true;
            ServerProcess.StartInfo.FileName = Path.GetFullPath(Path.Combine(MainWindow.ServerPath,"bedrock_server.exe"));
            ServerProcess.StartInfo.StandardInputEncoding = Encoding.Latin1;
            ServerProcess.StartInfo.StandardOutputEncoding = Encoding.Latin1;
            ServerProcess.StartInfo.CreateNoWindow = true;
            ServerProcess.StartInfo.ErrorDialog = true;

            ServerProcess.OutputDataReceived += Write;
            ServerProcess.Start();
            ServerProcess.BeginOutputReadLine();

            ServerProcess.WaitForExit();
            Dispatcher?.Invoke(() =>
            {
                Data.ServerStatus = false;
                Data.CMDRuning = true;
            });
            ServerProcess.CancelOutputRead();
            ServerProcess.OutputDataReceived -= Write;
            ServerProcess.Close();
            if (OutputList.Any(x => x.Contains("Exiting program") && x.Contains("ERROR")))
            {
                var Dump = new StreamWriter(Path.Combine(MainWindow.ServerPath, "LOGS", $"ERROR {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt"));
                OutputList.ForEach(Dump.WriteLine);
                Dump.Close();
                OtherController.ThrowLog("BC-S01 | Server Terminated. Internal ERROR");
                WriteDisplay("Server Stoped With Error.", Brushes.Red);
            }
            else
                WriteDisplay("Server Stoped.", Brushes.LightGray);

            Dispatcher?.Invoke(() =>
            {
                Exited = true;
                Data.ServerStatus = false;
                Data.CMDRuning = false;
                OutputList.Clear();
            });

            void Write(object sender, DataReceivedEventArgs e)
            {
                var Out = e.Data ?? "{NULL}";

                if(Out.EndsWith("Server started."))
                {
                    WriteLine("say §eWelcome To MCServer!");
                    Data.CMDRuning = false;
                    Data.ServerStatus = true;
                }

                if (Out.Contains("ERROR"))
                    WriteDisplay(Out, Brushes.Red);
                else WriteDisplay(Out);

                OutputList.Add(Out);
            };
        }

        public void StartServer()
        {
            if ((ServerThread?.ThreadState ?? System.Threading.ThreadState.Stopped) != System.Threading.ThreadState.Running)
            {
                ServerThread = new(Start) { Name = "ServerThread", };
                ServerThread.Start();
            }
        }

        public async Task Restart()
        {
            if (Exited)
            {
                Dispatcher?.Invoke(() => MainWindow.Window.NotifyUser("Start", "Server is not running!", Wpf.Ui.Controls.ControlAppearance.Danger));
                return;
            }
            Dispatcher?.Invoke(() => Data.CMDRuning = true);
            Que.WaitOne();

            var res = await StopServer("Restarting Server!", new(0,0,10));
            if(res) StartServer();

            Que.Release();
            Dispatcher?.Invoke(() => Data.CMDRuning = false);
        }

        public async Task<bool> StopServer(string Message, TimeSpan Delay)
        {
            TimeSpan iWait = new(0, 1, 0);
            string Colour = "§6";
            string DelayMessage = "";

            if (Delay.Days > 0) DelayMessage += $" {Delay.Days} Days";
            if (Delay.Hours > 0) DelayMessage += $" {Delay.Hours} Hours";
            if (Delay.Minutes > 0) DelayMessage += $" {Delay.Minutes} Minutes";
            if (Delay.Seconds > 0) DelayMessage += $" {Delay.Seconds} Seconds";
            if (Delay.Milliseconds > 0) DelayMessage += $" {Delay.Milliseconds} Milliseconds";
            if (Delay.Microseconds > 0) DelayMessage += $" {Delay.Microseconds} Microseconds";
            if (string.IsNullOrWhiteSpace(DelayMessage))
                DelayMessage = "NOW!";
            else DelayMessage = "In" + DelayMessage + "...";

            WriteDisplay($"Sending Server Warning: {Message}");
            WriteLine($"say {Colour}{Message} {DelayMessage}");
            await Task.Delay(Delay);
            WriteLine($"say {Colour}Turning Off...");
            await Task.Delay(iWait);
            WriteLine("stop");

            int I = 0;
            do
            {
                await Task.Delay(1000);
                I++;
            } while (!Exited && I < 120);
            if (!Exited)
            {
                WriteLine($"say §cError Could Not Stop Server...");
                OtherController.ThrowLog("'BC-S01' | Could Not Stop Process");
                return false;
            }

            return true;
        }

        public async Task Stop(TimeSpan Delay)
        {
            if (Exited)
            {
                Dispatcher?.Invoke(() => MainWindow.Window.NotifyUser("Start", "Server is not running!", Wpf.Ui.Controls.ControlAppearance.Danger));
                return;
            }
            Dispatcher?.Invoke(() => Data.CMDRuning = true); 
            Que.WaitOne();

            await StopServer("Stopping Server!", Delay);
            
            Que.Release();
            Dispatcher?.Invoke(() => Data.CMDRuning = false);
        }

        public async Task Exit(TimeSpan Delay)
        {
            if (Exited)
            {
                Dispatcher?.Invoke(() => MainWindow.Window.NotifyUser("Start", "Server is not running!", Wpf.Ui.Controls.ControlAppearance.Danger));
                return;
            }
            Dispatcher?.Invoke(() => Data.CMDRuning = true);
            Que.WaitOne();

            var res = await StopServer("Stopping Server!", Delay);
            if (res)
            {
                await Task.Delay(1000);
                Environment.Exit(0);
            }

            Que.Release();
            Dispatcher?.Invoke(() => Data.CMDRuning = false);
        }

        public async Task Power(TimeSpan Delay, PowerMode Mode)
        {
            if (Exited)
            {
                Dispatcher?.Invoke(() => MainWindow.Window.NotifyUser("Start", "Server is not running!", Wpf.Ui.Controls.ControlAppearance.Danger));
                return;
            }
            Dispatcher?.Invoke(() => Data.CMDRuning = true);
            Que.WaitOne();

            var res = await StopServer("Stopping Server!", Delay);
            if (res)
            {
                await Task.Delay(1000);
                PowerController.Shutdown(Mode);
            }

            Que.Release();
            Dispatcher?.Invoke(() => Data.CMDRuning = false);
        }

        internal static void WriteLine(string Line) => ServerProcess.StandardInput.WriteLine(Line);

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
                {   // Scroll bar isn'T in bottom
                    // Unset auto-scroll mode
                    AutoScroll = false;
                }
            }

            // Content scroll event : auto-scroll eventually
            if (AutoScroll && e.ExtentHeightChange != 0) ScrollViewer.ScrollToBottom();
        }
    }
}
