using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using MCServer.Helpers;

namespace MCServer.Services;

public class ProcessMetricsList : List<ProcessMetricsList.History>
{
    public const uint HISTORY_LIMIT = 60;
    
    public double this[ProcessMetricType Metric]
    {
        get => Find(x => x.Metric == Metric)?.Value ?? 0;
        set => Find(x => x.Metric == Metric)?.PushValue(value);
    }
    
    public class History
    {
        public ProcessMetricType Metric { get; }
        public double Value { get; set; } = 0;
        public double[] Record { get; } = new double[HISTORY_LIMIT];

        public event EventHandler? NewValuePushed;

        public void PushValue(double newValue)
        {
            Value = newValue;
            Array.Copy(Record, 1, Record, 0, Record.Length - 1);
            Record[HISTORY_LIMIT -1] = newValue;
        }

        public History(ProcessMetricType metric)
        {
            Metric = metric;
        }
    }
}

public sealed record ProcessMetricsSnapshot(
    int ProcessId,
    DateTime TimestampUtc,

    double CpuPercent,

    long WorkingSetBytes,
    int ThreadCount,

    long DiskReadBytesPerSecond,
    long DiskWriteBytesPerSecond,

    long NetworkReceiveBytesPerSecond,
    long NetworkSendBytesPerSecond);

public sealed class ProcessMetricsService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, ProcessMonitor> _monitors = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(2));

    private long _lastNetworkReceivedBytes;
    private long _lastNetworkSentBytes;
    private DateTime _lastNetworkSampleUtc;
    private bool _hasNetworkSample;

    public event EventHandler<ProcessMetricsSnapshot>? MetricsUpdated;

    public ProcessMetricsService()
    {
        Task.Run(MonitorLoopAsync, _cts.Token);
    }

    public void Register(Process process)
    {
        try
        {
            if (process.HasExited)
                return;

            _monitors.GetOrAdd(process.Id, _ => new ProcessMonitor(process));
        }
        catch
        {
            // Process may have exited before registration completed.
        }
    }

    public void Unregister(Process process)
    {
        try
        {
            _monitors.TryRemove(process.Id, out _);
        }
        catch
        {
            // Process may already be unavailable.
        }
    }

    public ProcessMetricsSnapshot? GetSnapshot(Process process)
    {
        try
        {
            return _monitors.TryGetValue(
                    process.Id,
                    out var monitor)
                ? monitor.Latest
                : null;
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<ProcessMetricsSnapshot> GetHistory(Process process)
    {
        try
        {
            return _monitors.TryGetValue(
                    process.Id,
                    out var monitor)
                ? monitor.History
                : [];
        }
        catch
        {
            return [];
        }
    }

    private async Task MonitorLoopAsync()
    {
        try
        {
            SampleAll();

            while (await _timer.WaitForNextTickAsync(_cts.Token))
            {
                SampleAll();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void SampleAll()
    {
        var network = SampleNetwork();

        foreach (var pair in _monitors.ToArray())
        {
            var monitor = pair.Value;

            if (!monitor.TrySample(
                    network.ReceiveBytesPerSecond,
                    network.SendBytesPerSecond,
                    out var snapshot))
            {
                _monitors.TryRemove(pair.Key, out _);
                continue;
            }

            MetricsUpdated?.Invoke(this, snapshot);
        }
    }

    private NetworkRate SampleNetwork()
    {
        var now = DateTime.UtcNow;

        long received = 0;
        long sent = 0;

        try
        {
            foreach (var networkInterface
                     in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus
                    != OperationalStatus.Up)
                    continue;

                var stats = networkInterface.GetIPv4Statistics();

                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
        }
        catch
        {
            return new NetworkRate(0, 0);
        }

        if (!_hasNetworkSample)
        {
            _lastNetworkReceivedBytes = received;
            _lastNetworkSentBytes = sent;
            _lastNetworkSampleUtc = now;
            _hasNetworkSample = true;

            return new NetworkRate(0, 0);
        }

        var elapsedSeconds =
            (now - _lastNetworkSampleUtc).TotalSeconds;

        if (elapsedSeconds <= 0)
            return new NetworkRate(0, 0);

        var receiveRate =
            (long)Math.Max(
                0,
                (received - _lastNetworkReceivedBytes)
                / elapsedSeconds);

        var sendRate =
            (long)Math.Max(
                0,
                (sent - _lastNetworkSentBytes)
                / elapsedSeconds);

        _lastNetworkReceivedBytes = received;
        _lastNetworkSentBytes = sent;
        _lastNetworkSampleUtc = now;

        return new NetworkRate(
            receiveRate,
            sendRate);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        _timer.Dispose();
        _cts.Dispose();

        _monitors.Clear();
    }

    private readonly record struct NetworkRate(
        long ReceiveBytesPerSecond,
        long SendBytesPerSecond);

    private sealed class ProcessMonitor : IDisposable
    {
        private const int MaxHistory = 60;

        private readonly Process _process;
        private readonly object _lock = new();

        private TimeSpan _lastCpuTime;

        private long _lastReadBytes;
        private long _lastWrittenBytes;

        private DateTime _lastSampleTimeUtc;

        private bool _hasPreviousSample;

        private readonly List<ProcessMetricsSnapshot> _history = [];

        public ProcessMetricsSnapshot? Latest { get; private set; }

        public IReadOnlyList<ProcessMetricsSnapshot> History
        {
            get
            {
                lock (_lock)
                {
                    return _history.ToArray();
                }
            }
        }

        public ProcessMonitor(Process process)
        {
            _process = process;
        }

        public bool TrySample(
            long networkReceiveRate,
            long networkSendRate,
            out ProcessMetricsSnapshot snapshot)
        {
            snapshot = default!;

            try
            {
                if (_process.HasExited)
                    return false;

                var now = DateTime.UtcNow;

                var cpuTime =
                    _process.TotalProcessorTime;

                var workingSet =
                    _process.WorkingSet64;

                var threads =
                    _process.Threads.Count;

                var io =
                    ProcessIoReader.Get(_process);

                double cpuPercent = 0;
                long readRate = 0;
                long writeRate = 0;

                if (_hasPreviousSample)
                {
                    var elapsedSeconds =
                        (now - _lastSampleTimeUtc).TotalSeconds;

                    if (elapsedSeconds > 0)
                    {
                        var cpuElapsedMs =
                            (cpuTime - _lastCpuTime)
                            .TotalMilliseconds;

                        cpuPercent =
                            cpuElapsedMs /
                            (elapsedSeconds *
                             1000 *
                             Environment.ProcessorCount)
                            * 100;

                        cpuPercent =
                            Math.Clamp(cpuPercent, 0, 100);

                        readRate =
                            (long)Math.Max(
                                0,
                                (io.ReadBytes - _lastReadBytes)
                                / elapsedSeconds);

                        writeRate =
                            (long)Math.Max(
                                0,
                                (io.WriteBytes - _lastWrittenBytes)
                                / elapsedSeconds);
                    }
                }

                _lastCpuTime = cpuTime;

                _lastReadBytes =
                    io.ReadBytes;

                _lastWrittenBytes =
                    io.WriteBytes;

                _lastSampleTimeUtc = now;
                _hasPreviousSample = true;

                snapshot = new ProcessMetricsSnapshot(
                    ProcessId: _process.Id,
                    TimestampUtc: now,

                    CpuPercent: cpuPercent,

                    WorkingSetBytes: workingSet,
                    ThreadCount: threads,

                    DiskReadBytesPerSecond: readRate,
                    DiskWriteBytesPerSecond: writeRate,

                    NetworkReceiveBytesPerSecond:
                        networkReceiveRate,

                    NetworkSendBytesPerSecond:
                        networkSendRate);

                lock (_lock)
                {
                    Latest = snapshot;

                    _history.Add(snapshot);

                    if (_history.Count > MaxHistory)
                    {
                        _history.RemoveAt(0);
                    }
                }

                return true;
            }
            catch (
                InvalidOperationException)
            {
                return false;
            }
            catch (
                System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            // We don't own the Process instance.
        }
    }

    private readonly record struct ProcessIo(
        long ReadBytes,
        long WriteBytes);

    private static class ProcessIoReader
    {
        public static ProcessIo Get(Process process)
        {
            if (OperatingSystem.IsLinux())
                return GetLinux(process.Id);

            if (OperatingSystem.IsWindows())
                return GetWindows(process);

            return new ProcessIo(0, 0);
        }

        private static ProcessIo GetLinux(int processId)
        {
            try
            {
                long readBytes = 0;
                long writeBytes = 0;

                var path = $"/proc/{processId}/io";

                foreach (var line in File.ReadLines(path))
                {
                    var parts = line.Split(
                        ':',
                        StringSplitOptions.TrimEntries);

                    if (parts.Length != 2)
                        continue;

                    if (parts[0] == "read_bytes")
                    {
                        long.TryParse(
                            parts[1],
                            out readBytes);
                    }
                    else if (parts[0] == "write_bytes")
                    {
                        long.TryParse(
                            parts[1],
                            out writeBytes);
                    }
                }

                return new ProcessIo(
                    readBytes,
                    writeBytes);
            }
            catch
            {
                return new ProcessIo(0, 0);
            }
        }

        private static ProcessIo GetWindows(
            Process process)
        {
            if (!GetProcessIoCounters(
                    process.Handle,
                    out var counters))
            {
                return new ProcessIo(0, 0);
            }

            return new ProcessIo(
                counters.ReadTransferCount,
                counters.WriteTransferCount);
        }

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        private static extern bool GetProcessIoCounters(
            IntPtr hProcess,
            out IO_COUNTERS lpIoCounters);

        [StructLayout(
            LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public long ReadOperationCount;
            public long WriteOperationCount;
            public long OtherOperationCount;

            public long ReadTransferCount;
            public long WriteTransferCount;
            public long OtherTransferCount;
        }
    }
}
