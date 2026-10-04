using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MCServer.Plugins;

/// <summary>One timestamped data point in a <see cref="MetricSeries"/>.</summary>
public sealed record MetricSample(DateTime TimestampUtc, double Value);

/// <summary>
/// Fixed-capacity, thread-safe ring buffer of <see cref="MetricSample"/>s.
/// Oldest samples are dropped once <see cref="Capacity"/> is reached.
/// </summary>
public sealed class MetricSeries
{
    private readonly object _lock = new();
    private readonly List<MetricSample> _samples = [];

    public string Name { get; }
    public string? Unit { get; }
    public int Capacity { get; }

    public MetricSeries(string name, int capacity = ServerMetrics.DefaultCapacity, string? unit = null)
    {
        Name = name;
        Capacity = Math.Max(1, capacity);
        Unit = unit;
    }

    public int Count
    {
        get { lock (_lock) return _samples.Count; }
    }

    /// <summary>Latest value, or 0 when no samples were recorded yet.</summary>
    public double Latest
    {
        get { lock (_lock) return _samples.Count == 0 ? 0 : _samples[^1].Value; }
    }

    public void Record(double value) => Record(DateTime.UtcNow, value);

    public void Record(DateTime timestampUtc, double value)
    {
        lock (_lock)
        {
            _samples.Add(new MetricSample(timestampUtc, value));
            while (_samples.Count > Capacity)
                _samples.RemoveAt(0);
        }
    }

    public IReadOnlyList<MetricSample> GetSnapshot()
    {
        lock (_lock) return _samples.ToArray();
    }

    public void Clear()
    {
        lock (_lock) _samples.Clear();
    }
}

/// <summary>
/// Dynamic per-server metric history for plugins. Any plugin can record
/// named data series (player count, TPS, ...); the host UI renders them
/// without plugin-specific UI code. Mirrors the <see cref="SchedulerService"/>
/// pattern: static registry, no <see cref="IGameServer"/> changes, so
/// third-party server implementations keep working.
/// Storage is a <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey, TValue}"/>
/// keyed by server instance, so histories die with their server.
/// </summary>
public static class ServerMetrics
{
    /// <summary>Well-known series the host records for every server.</summary>
    public const string Players = "Players";

    /// <summary>Samples kept per series by default (24h at the host's 1/min cadence).</summary>
    public const int DefaultCapacity = 1440;

    private static readonly ConditionalWeakTable<IGameServer, ConcurrentDictionary<string, MetricSeries>> _table = new();

    private static ConcurrentDictionary<string, MetricSeries> For(IGameServer server) =>
        _table.GetValue(server, _ => new ConcurrentDictionary<string, MetricSeries>(StringComparer.OrdinalIgnoreCase));

    public static MetricSeries GetOrCreate(IGameServer server, string name, int capacity = DefaultCapacity, string? unit = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return For(server).GetOrAdd(name, n => new MetricSeries(n, capacity, unit));
    }

    /// <summary>Appends a sample to the named series (created on first use).</summary>
    public static void Record(IGameServer server, string name, double value, int capacity = DefaultCapacity, string? unit = null) =>
        GetOrCreate(server, name, capacity, unit).Record(value);

    public static bool TryGetSeries(IGameServer server, string name, out MetricSeries? series)
    {
        series = null;
        ArgumentNullException.ThrowIfNull(server);
        return _table.TryGetValue(server, out var all) && all.TryGetValue(name, out series);
    }

    public static IReadOnlyList<MetricSeries> GetAll(IGameServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return _table.TryGetValue(server, out var all)
            ? all.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
    }
}
