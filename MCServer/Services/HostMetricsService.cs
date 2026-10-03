using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace MCServer.Services;

public enum HostMetricType
{
    Cpu,
    Memory,
    Disk,
    Network,
}

public sealed record HostMetricsSnapshot(
    DateTime TimestampUtc,
    double CpuPercent,
    double MemoryPercent,
    double DiskPercent,
    double NetworkReceiveMBps,
    double NetworkSendMBps)
{
    public static HostMetricsSnapshot Empty { get; } =
        new(DateTime.UtcNow, 0, 0, 0, 0, 0);
}

/// <summary>
/// Samples host-wide system metrics (CPU, memory, disk, network) on a timer.
/// CPU and memory are read from /proc on Linux and from Win32
/// (GetSystemTimes / GlobalMemoryStatusEx) on Windows. On Linux, memory is
/// container-aware (cgroup usage vs. limit, like docker stats) when the
/// process runs under a limit, and falls back to whole-machine meminfo
/// otherwise. Disk and network use cross-platform .NET APIs.
/// Mirrors <see cref="ProcessMetricsService"/>: per-process data lives there,
/// host data lives here, and both are rendered through <c>MetricCard</c>
/// (<c>ProcessMetric</c> for servers, <c>HostMetric</c> for the host).
/// </summary>
public sealed class HostMetricsService : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(2));

    private long _prevCpuIdle;
    private long _prevCpuTotal;

    private long _prevNetReceivedBytes;
    private long _prevNetSentBytes;
    private DateTime _prevNetSampleUtc = DateTime.UtcNow;
    private bool _hasNetSample;

    public HostMetricsSnapshot Latest { get; private set; } =
        HostMetricsSnapshot.Empty;

    public event EventHandler<HostMetricsSnapshot>? MetricsUpdated;

    public HostMetricsService()
    {
        Task.Run(MonitorLoopAsync, _cts.Token);
    }

    private async Task MonitorLoopAsync()
    {
        try
        {
            Sample();

            while (await _timer.WaitForNextTickAsync(_cts.Token))
                Sample();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void Sample()
    {
        double cpu = 0;
        double memory = 0;
        double disk = 0;
        double receive = 0;
        double send = 0;

        try { cpu = SampleCpu(); } catch { }
        try { memory = SampleMemory(); } catch { }
        try { disk = SampleDisk(); } catch { }
        try { (receive, send) = SampleNetwork(); } catch { }

        Latest = new HostMetricsSnapshot(
            DateTime.UtcNow, cpu, memory, disk, receive, send);

        MetricsUpdated?.Invoke(this, Latest);
    }

    private double SampleCpu()
    {
        if (OperatingSystem.IsLinux())
            return SampleCpuLinux();

        if (OperatingSystem.IsWindows())
            return SampleCpuWindows();

        return 0;
    }

    private double SampleCpuLinux()
    {
        var line = File.ReadLines("/proc/stat")
            .FirstOrDefault(static l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null)
            return 0;

        var values = line
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(static part => long.TryParse(part, out var value) ? value : 0)
            .ToArray();
        if (values.Length < 4)
            return 0;

        var idle = values[3] + (values.Length > 4 ? values[4] : 0);
        var total = values.Sum();

        return ComputeCpuPercent(idle, total);
    }

    private double SampleCpuWindows()
    {
        if (!WindowsNative.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return 0;

        // Times are in 100-nanosecond units. Kernel time includes idle time,
        // so total is kernel + user and busy is derived from the idle delta.
        var idle = (long)idleTime.ToUInt64();
        var total = (long)(kernelTime.ToUInt64() + userTime.ToUInt64());

        return ComputeCpuPercent(idle, total);
    }

    private double ComputeCpuPercent(long idle, long total)
    {
        if (_prevCpuTotal == 0)
        {
            _prevCpuIdle = idle;
            _prevCpuTotal = total;
            return 0;
        }

        var idleDelta = idle - _prevCpuIdle;
        var totalDelta = total - _prevCpuTotal;
        _prevCpuIdle = idle;
        _prevCpuTotal = total;

        if (totalDelta <= 0)
            return 0;

        return Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100, 0, 100);
    }

    private static double SampleMemory()
    {
        if (OperatingSystem.IsLinux())
            return SampleMemoryLinux();

        if (OperatingSystem.IsWindows())
            return SampleMemoryWindows();

        return 0;
    }

    private static double SampleMemoryLinux()
    {
        // Container-aware first so Docker/K8s memory limits are honored.
        if (TryReadCgroupMemory(out var cgroupUsed, out var cgroupLimit) && cgroupLimit > 0)
            return Math.Clamp((double)cgroupUsed / cgroupLimit * 100, 0, 100);

        // Fallback: whole-machine memory.
        long total = 0;
        long available = 0;

        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                total = ParseMemInfoKilobytes(line);
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                available = ParseMemInfoKilobytes(line);

            if (total > 0 && available > 0)
                break;
        }

        if (total <= 0)
            return 0;

        return Math.Clamp((double)(total - available) / total * 100, 0, 100);
    }

    private static double SampleMemoryWindows()
    {
        var status = new WindowsNative.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<WindowsNative.MEMORYSTATUSEX>(),
        };

        if (!WindowsNative.GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            return 0;

        var used = status.ullTotalPhys - status.ullAvailPhys;
        return Math.Clamp((double)used / status.ullTotalPhys * 100, 0, 100);
    }

    private static long ParseMemInfoKilobytes(string line)
    {
        foreach (var part in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(part, out var value))
                return value;
        }

        return 0;
    }

    /// <summary>
    /// Reads container memory usage and limit from cgroups, like <c>docker stats</c> does.
    /// Tries cgroup v2 first, then v1. Page cache (<c>inactive_file</c>) is excluded
    /// so the gauge isn't pegged by reclaimable cache. Returns false when no
    /// limit is set or cgroups are unavailable — the caller falls back to meminfo.
    /// </summary>
    private static bool TryReadCgroupMemory(out long usedBytes, out long limitBytes)
    {
        usedBytes = 0;
        limitBytes = 0;

        var v2Dir = SelfCgroupV2Directory();
        if (v2Dir is not null && TryReadV2Memory(v2Dir, out usedBytes, out limitBytes))
            return true;

        var v1Dir = SelfCgroupV1MemoryDirectory();
        if (v1Dir is not null && TryReadV1Memory(v1Dir, out usedBytes, out limitBytes))
            return true;

        // Last resort: container runtimes that mount our cgroup at the v2 root
        // while /proc/self/cgroup itself is unreadable.
        if (!string.Equals(v2Dir, CgroupRoot, StringComparison.Ordinal) &&
            TryReadV2Memory(CgroupRoot, out usedBytes, out limitBytes))
        {
            return true;
        }

        return false;
    }

    private const string CgroupRoot = "/sys/fs/cgroup";

    /// <summary>
    /// Resolves our own cgroup v2 directory from /proc/self/cgroup
    /// ("/sys/fs/cgroup" when we sit at the container root).
    /// </summary>
    private static string? SelfCgroupV2Directory()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/self/cgroup"))
            {
                // v2 format is a single "0::<path>" line.
                if (!line.StartsWith("0::", StringComparison.Ordinal))
                    continue;

                var relative = line["0::".Length..].Trim();
                if (relative.Length == 0 || relative == "/")
                    return CgroupRoot;

                var dir = Path.Combine(CgroupRoot, relative.TrimStart('/'));
                return Directory.Exists(dir) ? dir : null;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return null;
    }

    /// <summary>Resolves our own cgroup v1 memory directory from /proc/self/cgroup.</summary>
    private static string? SelfCgroupV1MemoryDirectory()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/self/cgroup"))
            {
                // v1 format is "<id>:<controllers>:<path>".
                var parts = line.Split(':');
                if (parts.Length != 3)
                    continue;

                if (!parts[1].Split(',').Contains("memory", StringComparer.Ordinal))
                    continue;

                var dir = Path.Combine("/sys/fs/cgroup/memory", parts[2].Trim().TrimStart('/'));
                return Directory.Exists(dir) ? dir : null;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return null;
    }

    private static bool TryReadV2Memory(string dir, out long usedBytes, out long limitBytes)
    {
        usedBytes = 0;
        limitBytes = 0;

        if (!TryReadV2Limit(Path.Combine(dir, "memory.max"), out var max) || max <= 0)
            return false;

        if (!TryReadLongFile(Path.Combine(dir, "memory.current"), out var current))
            return false;

        limitBytes = max;
        usedBytes = Math.Max(0, current - ReadMemoryStat(Path.Combine(dir, "memory.stat"), "inactive_file"));
        return true;
    }

    private static bool TryReadV1Memory(string dir, out long usedBytes, out long limitBytes)
    {
        usedBytes = 0;
        limitBytes = 0;

        if (!TryReadLongFile(Path.Combine(dir, "memory.limit_in_bytes"), out var max) ||
            max <= 0 || (ulong)max >= 0x7FFFFFFFFFFFF000UL) // At/above this = "no limit".
        {
            return false;
        }

        if (!TryReadLongFile(Path.Combine(dir, "memory.usage_in_bytes"), out var current))
            return false;

        limitBytes = max;
        usedBytes = Math.Max(0, current - ReadMemoryStat(Path.Combine(dir, "memory.stat"), "total_inactive_file"));
        return true;
    }

    private static bool TryReadLongFile(string path, out long value)
    {
        value = 0;

        try
        {
            var text = File.ReadAllText(path);
            foreach (var part in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part, out value))
                    return true;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return false;
    }

    /// <summary>Reads a cgroup v2 limit file, which holds either a number or "max".</summary>
    private static bool TryReadV2Limit(string path, out long limit)
    {
        limit = 0;

        try
        {
            var text = File.ReadAllText(path).Trim();
            if (text.StartsWith("max", StringComparison.Ordinal))
                return false; // No limit set.

            return long.TryParse(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out limit) && limit > 0;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return false;
    }

    private static long ReadMemoryStat(string path, string key)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 &&
                    parts[0].Equals(key, StringComparison.Ordinal) &&
                    long.TryParse(parts[1], out var value))
                {
                    return Math.Max(0, value);
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return 0;
    }

    private static double SampleDisk()
    {
        var root = Path.GetFullPath(Program.ServerPath);
        var drive = DriveInfo.GetDrives()
            .Where(static d => d.IsReady)
            .OrderByDescending(d => d.Name.Length)
            .FirstOrDefault(d => root.StartsWith(d.Name, StringComparison.OrdinalIgnoreCase))
            ?? DriveInfo.GetDrives().FirstOrDefault(static d => d.IsReady);

        if (drive is null || drive.TotalSize <= 0)
            return 0;

        var used = drive.TotalSize - drive.TotalFreeSpace;
        return Math.Clamp((double)used / drive.TotalSize * 100, 0, 100);
    }

    private (double ReceiveMBps, double SendMBps) SampleNetwork()
    {
        long received = 0;
        long sent = 0;

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
                continue;

            var stats = adapter.GetIPv4Statistics();
            received += stats.BytesReceived;
            sent += stats.BytesSent;
        }

        var now = DateTime.UtcNow;

        if (!_hasNetSample)
        {
            _prevNetReceivedBytes = received;
            _prevNetSentBytes = sent;
            _prevNetSampleUtc = now;
            _hasNetSample = true;
            return (0, 0);
        }

        var elapsed = (now - _prevNetSampleUtc).TotalSeconds;
        var receivedDelta = received - _prevNetReceivedBytes;
        var sentDelta = sent - _prevNetSentBytes;

        _prevNetReceivedBytes = received;
        _prevNetSentBytes = sent;
        _prevNetSampleUtc = now;

        if (elapsed <= 0)
            return (0, 0);

        const double bytesPerMegabyte = 1024 * 1024;
        return (
            Math.Max(0, receivedDelta / elapsed / bytesPerMegabyte),
            Math.Max(0, sentDelta / elapsed / bytesPerMegabyte));
    }

    private static class WindowsNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;

            public readonly ulong ToUInt64() =>
                ((ulong)dwHighDateTime << 32) | dwLowDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemTimes(
            out FILETIME lpIdleTime,
            out FILETIME lpKernelTime,
            out FILETIME lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        _timer.Dispose();
        _cts.Dispose();
    }
}
