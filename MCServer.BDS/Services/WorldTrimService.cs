using LevelDB;
using MCServer.BDS.Models;

namespace MCServer.BDS.Services;

/// <summary>
/// Trims Bedrock chunks for a single dimension by deleting LevelDB chunk keys
/// outside a keep-area (rectangle or radius). The server must be stopped;
/// the caller is responsible for the backup (see <c>MaintenanceService.TrimServer</c>).
/// Only chunk keys (9/10/13/14/21 bytes) are touched; global keys
/// (~local_player, scoreboard, portals, actorprefix, ...) are left alone.
/// </summary>
public static class WorldTrimService
{
    private const int DeleteBatchSize = 1000;

    public static string GetDbPath(this BedrockServer server, string worldName) =>
        Path.Combine(server.GetWorldsPath(), worldName, "db");

    /// <summary>Dry-run: count what <see cref="TrimAsync"/> would delete.</summary>
    public static Task<TrimResult> ScanAsync(
        this BedrockServer server, TrimOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        RunCoreAsync(server, options with { DryRun = true }, progress, cancellationToken);

    public static Task<TrimResult> TrimAsync(
        this BedrockServer server, TrimOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        RunCoreAsync(server, options, progress, cancellationToken);

    private static async Task<TrimResult> RunCoreAsync(
        BedrockServer server, TrimOptions options,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.WorldName))
            throw new ArgumentException("World name is required.", nameof(options));
        var activeWorld = server.GetProperties().FirstOrDefault(p => p.Name == "level-name")?.Value;
        var isActive = string.Equals(activeWorld, options.WorldName, StringComparison.OrdinalIgnoreCase);
        if (server.ServerRunning && isActive)
            throw new InvalidOperationException(
                $"Stop the server before trimming the active world '{options.WorldName}' (its LevelDB is locked while running).");

        var dbPath = server.GetDbPath(options.WorldName);
        if (!Directory.Exists(dbPath))
            throw new DirectoryNotFoundException(
                $"World '{options.WorldName}' has no LevelDB folder (expected {dbPath}).");

        var started = DateTime.UtcNow;
        // LevelDBSharp wraps a blocking native engine: keep it off the UI thread.
        return await Task.Run(() =>
        {
            List<byte[]>? toDelete = options.DryRun ? null : new List<byte[]>();
            long scanned = 0, inDim = 0, deletedKeys = 0, keptKeys = 0;
            var keptChunks = new HashSet<(int X, int Z)>();
            var deletedChunks = new HashSet<(int X, int Z)>();

            var levelOptions = new Options { CreateIfMissing = false };
            using (var db = new DB(levelOptions, dbPath))
            {
                using var it = db.CreateIterator();
                for (it.SeekToFirst(); it.IsValid(); it.Next())
                {
                    ct.ThrowIfCancellationRequested();
                    scanned++;
                    byte[] key;
                    try { key = it.Key(); }
                    catch { continue; }
                    if (!BedrockKey.TryParseChunkKey(key, out var cx, out var cz, out var dim, out _))
                        continue;
                    if (dim != options.Dimension)
                        continue;
                    inDim++;
                    if (options.Contains(cx, cz))
                    {
                        keptKeys++;
                        keptChunks.Add((cx, cz));
                    }
                    else
                    {
                        deletedKeys++;
                        deletedChunks.Add((cx, cz));
                        toDelete?.Add(key);
                    }
                }

                if (toDelete is { Count: > 0 })
                {
                    using var batch = new WriteBatch();
                    var writeOptions = new WriteOptions { Sync = true };
                    long written = 0;
                    foreach (var key in toDelete)
                    {
                        ct.ThrowIfCancellationRequested();
                        batch.Delete(key);
                        written++;
                        if (written % DeleteBatchSize == 0)
                        {
                            db.Write(batch, writeOptions);
                            batch.Clear();
                            progress?.Report((double)written / toDelete.Count);
                        }
                    }
                    if (written % DeleteBatchSize != 0)
                        db.Write(batch, writeOptions);
                    progress?.Report(1.0);
                }
            }

            return new TrimResult(
                options.WorldName, options.Dimension, options.Describe(), options.DryRun,
                scanned, inDim, deletedKeys, keptKeys,
                deletedChunks.Count, keptChunks.Count, DateTime.UtcNow - started);
        }, ct);
    }
}
