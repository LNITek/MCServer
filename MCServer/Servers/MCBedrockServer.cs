using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ExtraFunctions.ExGenerators;
using MCServer.Helpers;
using MudBlazor;

namespace MCServer.Server;

public partial class MCBedrockServer : INotifyPropertyChanged, IDisposable
{
    public string ServerPath { get; set; }
    public DateTime StartupDate { get; set; }
    [NotifyChanged([nameof(ServerRunningStatus), nameof(ServerExitedStatus)])]
    bool serverRunning = false;
    [NotifyChanged([nameof(ServerRunningStatus), nameof(ServerExitedStatus)])]
    bool commandRunning = false;
    public bool ServerRunningStatus => CommandRunning || ServerRunning;
    public bool ServerExitedStatus => CommandRunning || !ServerRunning;

    public StackList OutputList { get; set; } = new(100);
    public Process ServerProcess = new();
    public Thread ServerThread { get; set; }
    public Semaphore CommandQue = new(1, 1, "CommandThreadQue");
    private IntPtr _jobHandle = IntPtr.Zero;

    public MCBedrockServer(string path)
    {
        ServerPath = path;
    }

    public void RunCommand(string command)
    {

    }

    public void WriteLine(string Line) => ServerProcess.StandardInput.WriteLine(Line);

    public void WriteDisplay(string Line)
    {
        var output = OutputList.LastOrDefault();
        output?.Line += Line;
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
