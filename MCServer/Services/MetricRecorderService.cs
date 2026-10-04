using MCServer.Plugins;
using MCServer.Server;

namespace MCServer.Services;

/// <summary>
/// Samples every registered server on a fixed cadence into the dynamic
/// <see cref="ServerMetrics"/> history (player count today; plugins can add
/// their own series via <see cref="ServerMetrics.Record"/>). The Overview
/// page charts render whatever is recorded — no per-metric UI code needed.
/// </summary>
public sealed class MetricRecorderService : BackgroundService
{
    /// <summary>Samples are 1/min; with the default capacity that is 24h of history.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>How often chart pages should repaint from recorded history.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private readonly GameServers _servers;
    private readonly ILogger<MetricRecorderService> _logger;

    public MetricRecorderService(GameServers servers, ILogger<MetricRecorderService> logger)
    {
        _servers = servers;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Seed immediately so freshly opened charts aren't empty for a full interval.
        RecordAll();

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                RecordAll();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void RecordAll()
    {
        IGameServer[] servers;
        try
        {
            servers = _servers.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Metric sampling skipped: server registry unavailable");
            return;
        }

        foreach (var server in servers)
        {
            try
            {
                ServerMetrics.Record(server, ServerMetrics.Players, server.PlayerCount, unit: "players");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Metric sampling failed for server '{ID}'", server.Settings.ID);
            }
        }
    }
}
