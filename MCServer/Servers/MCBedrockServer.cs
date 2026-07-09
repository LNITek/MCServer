using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ExtraFunctions.ExGenerators;
using ExtraFunctions.Extras;
using MCServer.Helpers;
using MCServer.Services;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace MCServer.Server;

public partial class MCBedrockServer : INotifyPropertyChanged, IDisposable, IGameServer
{
    public ServerSettings Settings { get; }
    public string ServerPath => Settings.FullPath;
    public DateTime StartupDate { get; set; }
    [NotifyChanged([nameof(ServerRunningStatus), nameof(ServerExitedStatus), nameof(RunningStatus)])]
    bool serverRunning = false;
    [NotifyChanged([nameof(ServerRunningStatus), nameof(ServerExitedStatus)])]
    bool commandRunning = false;
    public bool ServerRunningStatus => CommandRunning || ServerRunning;
    public bool ServerExitedStatus => CommandRunning || !ServerRunning;
    public Color RunningStatus => ServerRunning ? Color.Success : Color.Error;

    public StackList OutputList { get; set; } = new(100);
    public Process ServerProcess = new();
    public Thread ServerThread { get; set; }
    public Semaphore CommandQue = new(1, 1);
    private IntPtr _jobHandle = IntPtr.Zero;

    [NotifyChanged]
    double? progress { get; set; } = null;

    public ObservableCollection<Schedule> ServerSchedules { get; } = [];
    public ObservableCollection<Player> PlayerList { get; set; } = [];
    public static ServerCommandList Commands { get; set; }
    
    public MCBedrockServer(ServerSettings settings)
    {
        Settings = settings;
        PlayerList.AddRange(this.GetPlayers());
        ServerSchedules.AddRange(this.GetSchedules());
        this.AddSchedules(ServerSchedules);
        ServerSchedules.CollectionChanged += (s, e) =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    this.AddSchedules(e.NewItems?.OfType<Schedule>() ?? []);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    this.RemoveSchedules(e.OldItems?.OfType<Schedule>() ?? []);
                    break;
            }
        };
        
        Commands = 
        [
            //new("!"){ Description = "Stops the server, Exits the app and Power off (Shutdown) in 10 Seconds" },
            new("Backup","Backups the active world.",[
                new("-world","The the world to backup.", typeof(uint))])
            {
                OnExecution = (Args) => this.BackupServer(Args["-world"]?.Value.ToString())
            },
            new("Update", "Updates the server.",[])
            {
                OnExecution = (Args) => this.UpdateServer()
            },
            new("Start", "Starts the server if it's not running yet." ,[])
            {
                OnExecution = (Args) => this.StartServer()
            },
            new("Restart", "Restarts the server." ,[
                new("-delay","The delay before restarting in seconds.", typeof(uint))
            ])
            {
                OnExecution = (Args) => this.RestartServer(TimeSpan.FromSeconds(((int?)Args["-delay"]?.Value) ?? 10))
            },
            new("Stop", "Stops the server.", [
                        new("-delay","The delay before stoping in seconds.", typeof(uint))
                    ])
                    {
                        OnExecution = (Args) => this.StopServer(TimeSpan.FromSeconds(((int?)Args["-delay"]?.Value) ?? 10))
                    },
            // new("Exit", [
            //     new(nameof(ExitCommand.Delay), typeof(int)) { Description = "The Delay Before Stoping In Seconds." }
            //     ]) { Description = "Stops The Server And Exits The App." },
            // new("Power", [
            //     new(nameof(PowerCommand.Delay), typeof(int)) { Description = "The Delay Before Stoping In Seconds." },
            //     new(nameof(PowerCommand.Mode), typeof(int)) { Description = "The Shutdown Mode To Use." }
            //     ]) { Description = "Stops The Server And Power On/Off OS." },
        ];
        Commands.OnCommandException += (command, ex) =>
        {
            if (ex.Message.StartsWith("C-01-"))
                WriteLine(command?.ToString() ?? "");
            else
                Program.NotifyUser(ex.Message, Severity.Error);
        };
    }

    public void RunCommand(string command)
    {
        Commands.Parse(command);
    }

    public void RefreshSchedules()
    {
        
    }

    public void RefreshPlayerProperties()
    {
        WriteLine("allowlist reload");
        WriteLine("permission reload");
        return;
        var newPlayerList = this.GetPlayers();
        PlayerList.Where(x => x.IsOnline).ForEach(x =>
        {
            if (newPlayerList.FirstOrDefault(l => l.Xuid == x.Xuid || l.Name == x.Name) is Player player)
            {
                player.IsOnline = true;
            }
        });
        PlayerList.Clear();
        PlayerList.AddRange(newPlayerList);
    }
    
    public void RefreshPacketConfig()
    {
        WriteLine("reloadpacketlimitconfig");
    }

    public void WriteLine(string Line)
    {
        if (ServerRunning)
            ServerProcess.StandardInput.WriteLine(Line);
    }

    public void RewriteDisplay(string Line)
    {
        var output = OutputList.LastOrDefault();
        if(output is null) 
            output = new(DateTime.Now, Color.Info, Line);
        OutputList.Remove(output);
        output.Line = Line;
        OutputList.Add(output);
    }
    
    public void WriteDisplay(string Line)
    {
        var output = OutputList.LastOrDefault();
        if(output is null) 
            output = new(DateTime.Now, Color.Info, Line);
        OutputList.Remove(output);
        output.Line += Line;
        OutputList.Add(output);
    }

    public void WriteDisplayLine(string Line, Color Color, bool InfoStamp = true)
    {
        OutputList.Add(new(DateTime.Now, Color, Line) { IncludeInfoStamp = InfoStamp });

        //Dispatcher?.Invoke(() =>
        //{
        //    TextRange tr = new(Display.Document.ContentEnd, Display.Document.ContentEnd)
        //    { Text = (NewLine ? Environment.NewLine : " ") + Line };
        //    try { tr.ApplyPropertyValue(TextElement.ForegroundProperty, color); }
        //    catch (FormatException) { }
        //});
    }

    public void WriteDisplayLine(string Line, bool InfoStamp = true)
    {
        OutputList.Add(new(DateTime.Now, Color.Info, Line) { IncludeInfoStamp = InfoStamp });
    }

    public void WriteProcessExit(object sender, EventArgs e)
    {
        if (OutputList.Any(x => x.Line.Contains("Exiting program") && x.Type == Color.Error /*x.Contains("ERROR")*/))
        {
            /*var Dump = new StreamWriter(Path.Combine(server.ServerPath, "LOGS", $"ERROR {DateTime.Now:yyyy-MM-dd HH-mm-ss}.txt"));
            server.OutputList.ForEach((x) => Dump.WriteLine(x));
            Dump.Close();*/
            //OtherController.ThrowLog("BC-S01 | Server Terminated. Internal ERROR");
            WriteDisplayLine("Server Stopped With Error.", Color.Error);
        }
        else
            WriteDisplayLine("Server Stopped.", Color.Info);

        foreach (var player in PlayerList.Where(x => x.IsOnline))
        {
            player.TotalPlayTime += DateTime.Now - (player.LastLogin ?? DateTime.Now);
            player.IsOnline = false;
        };
        this.SetPlayers(PlayerList);
    }
    
    public void WriteProcessError(object sender, DataReceivedEventArgs e)
    {
        var Out = e.Data ?? "{NULL}";
        WriteDisplayLine(Out, Color.Error, true);
    }
    
    public void WriteProcessOut(object sender, DataReceivedEventArgs e)
    {
        var Out = e.Data ?? "{NULL}";

        if (Out.EndsWith("Server started."))
        {
            WriteLine("say §eWelcome To MCServer!");
            CommandRunning = false;
        }

        if (Out.Contains("Player Spawned", StringComparison.OrdinalIgnoreCase))
        {
            var props = Out[Out.IndexOf(']')..].Split([',', ':',' ']).Where(x => !string.IsNullOrWhiteSpace(x));
            var name = props.ElementAt(3).Trim([' ', '"']);
            var xuid = props.ElementAt(5).Trim([' ', '"']);

            if (PlayerList.FirstOrDefault(x => x.Xuid == xuid || x.Name == name) is Player player)
            {
                if (player.Ban)
                    Task.Delay(500).ContinueWith(t =>
                    {
                        WriteLine($"kick \"{player.DisplayName}\" You are currently ban from this server until {player.BanTime?.ToString() ?? "Indefinitely"} for: {player.BanResion}");
                    });

                player.LastLogin = DateTime.Now;
                player.IsOnline = true;
                this.SetPlayers(PlayerList);
            }
            else
            {
                PlayerList.Add(new(name, xuid) { LastLogin = DateTime.Now, IsOnline = true });
                this.SetPlayers(PlayerList);
            }
        }

        if (Out.Contains("Player disconnected", StringComparison.OrdinalIgnoreCase))
        {
            var props = Out[Out.IndexOf(']')..].Split([',', ':']);
            var name = props[1];
            var xuid = props[3];

            if(PlayerList.FirstOrDefault(x => !x.Ban && (x.Xuid == xuid || x.Name == name)) is Player player)
            {
                player.TotalPlayTime += DateTime.Now - (player.LastLogin ?? DateTime.Now);
                player.IsOnline = false;
                this.SetPlayers(PlayerList);
            }
        }

        if (Out.Contains("INFO"))
            WriteDisplayLine(Out, Color.Info, false);
        else if (Out.Contains("WARN"))
            WriteDisplayLine(Out, Color.Warning, false);
        else if (Out.Contains("ERROR"))
            WriteDisplayLine(Out, Color.Error, false);

        else WriteDisplayLine(Out, Color.Info);
    }
    
    public void SetAsChildProcess()
    {
        if (!Program.IsWin) return;

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT.KILL_ON_JOB_CLOSE
            }
        };
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        if (!SetInformationJobObject(job, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, ref info, (uint)size))
        {
            CloseHandle(job);
            return;
        }
        if (!AssignProcessToJobObject(job, ServerProcess.Handle))
        {
            CloseHandle(job);
            return;
        }
        _jobHandle = job;
    }

    public void Dispose()
    {
        ServerRunning = false;
        if (ServerProcess is { HasExited: false })
        {
            try { ServerProcess.Kill(entireProcessTree: true); } catch { }
            try { ServerProcess.Dispose(); } catch { }
        }
        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }

    #region Windows Job Object

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public JOB_OBJECT_LIMIT LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    private enum JOB_OBJECT_LIMIT : uint
    {
        KILL_ON_JOB_CLOSE = 0x00002000,
    }

    private enum JOBOBJECTINFOCLASS
    {
        JobObjectExtendedLimitInformation = 9,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInfoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    #endregion
}
