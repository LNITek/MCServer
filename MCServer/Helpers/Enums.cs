namespace MCServer.Helpers;

public enum ConsoleLineType
{
    Standard,
    Info,
    Success,
    Warning,
    Error,
}

public enum ProcessMetricType
{
    Cpu,
    Memory,
    Threads,
    DiskRead,
    DiskWrite,
    NetworkReceive,
    NetworkSend
}