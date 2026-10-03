namespace MCServer.Plugins;

public enum ConsoleLineType
{
    Status,
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
