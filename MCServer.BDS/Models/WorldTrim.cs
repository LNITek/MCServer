namespace MCServer.BDS.Models;

/// <summary>Bedrock dimension ids as encoded in LevelDB chunk keys.</summary>
public enum BedrockDimension
{
    Overworld = 0,
    Nether = 1,
    End = 2,
}

/// <summary>How the keep-area is defined.</summary>
public enum TrimMode
{
    /// <summary>Keep chunks inside an inclusive chunk-coordinate rectangle.</summary>
    Rect,
    /// <summary>Keep chunks within a radius (square or circle) of a center chunk.</summary>
    Radius,
}

/// <summary>Options for a world trim operation. Coordinates are chunk coordinates (block &gt;&gt; 4).</summary>
public sealed record TrimOptions(
    string WorldName,
    BedrockDimension Dimension,
    TrimMode Mode,
    int MinChunkX,
    int MinChunkZ,
    int MaxChunkX,
    int MaxChunkZ,
    int CenterChunkX,
    int CenterChunkZ,
    int RadiusChunks,
    bool Circular,
    bool DryRun,
    bool CreateBackup)
{
    public static TrimOptions FromRect(
        string worldName, BedrockDimension dimension,
        int minChunkX, int minChunkZ, int maxChunkX, int maxChunkZ,
        bool dryRun = false, bool createBackup = true)
    {
        if (minChunkX > maxChunkX) (minChunkX, maxChunkX) = (maxChunkX, minChunkX);
        if (minChunkZ > maxChunkZ) (minChunkZ, maxChunkZ) = (maxChunkZ, minChunkZ);
        return new(worldName, dimension, TrimMode.Rect,
            minChunkX, minChunkZ, maxChunkX, maxChunkZ,
            (minChunkX + maxChunkX) / 2, (minChunkZ + maxChunkZ) / 2, 0,
            false, dryRun, createBackup);
    }

    public static TrimOptions FromRadius(
        string worldName, BedrockDimension dimension,
        int centerChunkX, int centerChunkZ, int radiusChunks, bool circular = false,
        bool dryRun = false, bool createBackup = true)
    {
        radiusChunks = Math.Max(0, radiusChunks);
        return new(worldName, dimension, TrimMode.Radius,
            centerChunkX - radiusChunks, centerChunkZ - radiusChunks,
            centerChunkX + radiusChunks, centerChunkZ + radiusChunks,
            centerChunkX, centerChunkZ, radiusChunks,
            circular, dryRun, createBackup);
    }

    public bool Contains(int chunkX, int chunkZ)
    {
        if (Mode == TrimMode.Rect)
            return chunkX >= MinChunkX && chunkX <= MaxChunkX &&
                   chunkZ >= MinChunkZ && chunkZ <= MaxChunkZ;
        var dx = chunkX - CenterChunkX;
        var dz = chunkZ - CenterChunkZ;
        if (Circular)
            return (long)dx * dx + (long)dz * dz <= (long)RadiusChunks * RadiusChunks;
        return Math.Abs(dx) <= RadiusChunks && Math.Abs(dz) <= RadiusChunks;
    }

    public string Describe() => Mode switch
    {
        TrimMode.Rect => $"rect X[{MinChunkX}..{MaxChunkX}] Z[{MinChunkZ}..{MaxChunkZ}]",
        _ => Circular
            ? $"circle center ({CenterChunkX},{CenterChunkZ}) r={RadiusChunks}"
            : $"square center ({CenterChunkX},{CenterChunkZ}) r={RadiusChunks}",
    };

    public static bool TryParseDimension(string? value, out BedrockDimension dimension)
    {
        dimension = BedrockDimension.Overworld;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var v = value.Trim().ToLowerInvariant();
        if (v is "overworld" or "over" or "0") { dimension = BedrockDimension.Overworld; return true; }
        if (v is "nether" or "netherwastes" or "1") { dimension = BedrockDimension.Nether; return true; }
        if (v is "end" or "theend" or "the_end" or "2") { dimension = BedrockDimension.End; return true; }
        return false;
    }
}

/// <summary>Outcome of a trim (or dry-run scan).</summary>
public sealed record TrimResult(
    string WorldName,
    BedrockDimension Dimension,
    string KeepArea,
    bool DryRun,
    long ScannedKeys,
    long ChunkKeysInDimension,
    long DeletedKeys,
    long KeptChunkKeys,
    long DistinctChunksDeleted,
    long DistinctChunksKept,
    TimeSpan Duration);

/// <summary>
/// Parses Bedrock LevelDB chunk keys.
/// Chunk key = [x i32 LE][z i32 LE][dim? i32 LE][tag u8][sub? u8] (+8B id for tag 0x78).
/// Overworld omits dim (len 9/10); Nether/End include it (len 13/14, or 21 for Jigsaw).
/// </summary>
public static class BedrockKey
{
    public static bool TryParseChunkKey(byte[] key, out int chunkX, out int chunkZ, out BedrockDimension dimension, out byte tag)
    {
        chunkX = 0;
        chunkZ = 0;
        dimension = BedrockDimension.Overworld;
        tag = 0;
        if (key is null)
            return false;

        if (key.Length == 9 || key.Length == 10)
        {
            chunkX = BitConverter.ToInt32(key, 0);
            chunkZ = BitConverter.ToInt32(key, 4);
            dimension = BedrockDimension.Overworld;
            tag = key[8];
            return true;
        }
        if (key.Length == 13 || key.Length == 14 || key.Length == 21)
        {
            chunkX = BitConverter.ToInt32(key, 0);
            chunkZ = BitConverter.ToInt32(key, 4);
            var dim = BitConverter.ToInt32(key, 8);
            if (dim is < 0 or > 2)
                return false;
            dimension = (BedrockDimension)dim;
            tag = key[12];
            return true;
        }
        return false;
    }
}
