using System.Formats.Tar;
using System.IO.Compression;
using Newtonsoft.Json.Linq;

namespace MCServer.BDS.Services;

public enum WorldArchiveFormat
{
    Zip,
    TarGz,
    McWorld,
}

public enum PackKind
{
    Resource,
    Behavior,
}

public sealed record PackInfo(
    string Name,
    string DisplayName,
    string DisplayVersion,
    string Description,
    string Path,
    bool IsFile,
    long SizeBytes,
    DateTime LastModified,
    Guid? PackId = null,
    int[]? Version = null,
    IReadOnlyList<string>? ModuleTypes = null);

public sealed record WorldInfo(
    string Name,
    string DisplayName,
    string Path,
    long SizeBytes,
    DateTime LastModified,
    IReadOnlyList<PackInfo> ResourcePacks,
    IReadOnlyList<PackInfo> BehaviorPacks);

/// <summary>Parsed manifest.json identity of a pack.</summary>
public sealed record PackManifest(
    Guid? PackId,
    int[] Version,
    string? Name,
    string? Description,
    IReadOnlyList<string> ModuleTypes,
    IReadOnlyList<PackDependency> Dependencies,
    int[]? MinEngineVersion);

public sealed record PackDependency(Guid? PackId, int[] Version, string? ModuleName);

public enum PackImportOutcome
{
    Installed,
    Updated,
    Downgraded,
    SameVersion,
    InstalledAsCopy,
}

/// <summary>User decision when an uploaded pack shares its uuid with an installed pack.</summary>
public enum PackConflictChoice
{
    /// <summary>Replace the installed pack folder in place (keeps folder name, bumps world pin).</summary>
    Update,
    /// <summary>Keep both: assign fresh uuids to the upload and install it as an extra pack.</summary>
    KeepBoth,
}

public sealed record PackImportResult(
    string PackName,
    Guid? PackId,
    int[]? OldVersion,
    int[] NewVersion,
    PackImportOutcome Outcome,
    PackKind Kind);

/// <summary>One pack extracted to a temp staging area, awaiting commit.</summary>
public sealed record StagedPack(
    PackKind Kind,
    string StagedPath,
    PackManifest? Manifest,
    string CandidateName,
    PackInfo? Existing);

/// <summary>Staging area ownership: caller must call <see cref="BedrockResourceService.DiscardStagedImport"/> when done.</summary>
public sealed class StagedPackImport
{
    public string StagingRoot { get; }
    public List<StagedPack> Packs { get; }

    public StagedPackImport(string stagingRoot, List<StagedPack> packs)
    {
        StagingRoot = stagingRoot;
        Packs = packs;
    }
}

public static class BedrockResourceService
{
    public const string WorldsFolderName = "worlds";
    public const string ResourcePacksFolderName = "resource_packs";
    public const string BehaviorPacksFolderName = "behavior_packs";

    public static string GetWorldsPath(this BedrockServer server) =>
        Path.Combine(server.ServerPath, WorldsFolderName);

    public static List<WorldInfo> GetWorlds(this BedrockServer server)
    {
        var worldsPath = server.GetWorldsPath();
        if (!Directory.Exists(worldsPath))
            return [];

        return Directory.GetDirectories(worldsPath)
            .Select(ReadWorld)
            .OrderBy(w => w.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static WorldInfo? GetWorld(this BedrockServer server, string worldName)
    {
        var path = Path.Combine(server.GetWorldsPath(), worldName);
        if (!Directory.Exists(path))
            return null;
        return ReadWorld(path);
    }

    public static WorldInfo ReadWorld(string worldPath)
    {
        var name = Path.GetFileName(worldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        TryRepairPackFolderNames(worldPath);
        TryRepairPackHistory(worldPath);
        var displayName = ReadLevelName(worldPath) ?? name;
        var size = GetDirectorySize(worldPath);
        var lastModified = Directory.GetLastWriteTimeUtc(worldPath);
        var resourcePacks = ReadPacks(Path.Combine(worldPath, ResourcePacksFolderName));
        var behaviorPacks = ReadPacks(Path.Combine(worldPath, BehaviorPacksFolderName));
        return new(name, displayName, worldPath, size, lastModified, resourcePacks, behaviorPacks);
    }

    public static string? ReadLevelName(string worldPath)
    {
        var levelNamePath = Path.Combine(worldPath, "levelname.txt");
        if (!File.Exists(levelNamePath))
            return null;
        try
        {
            var text = File.ReadAllText(levelNamePath).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    public static List<PackInfo> ReadPacks(string packsPath)
    {
        if (!Directory.Exists(packsPath))
            return [];
        var result = new List<PackInfo>();
        foreach (var dir in Directory.GetDirectories(packsPath))
        {
            var name = Path.GetFileName(dir)!;
            if (name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
                continue;
            var manifest = TryReadManifest(dir);
            result.Add(new(
                name,
                CleanDisplayText(manifest?.Name) ?? name,
                manifest is null ? "0" : FormatVersion(manifest.Version),
                CleanDisplayText(manifest?.Description) ?? "",
                dir,
                false,
                GetDirectorySize(dir),
                Directory.GetLastWriteTimeUtc(dir),
                manifest?.PackId,
                manifest?.Version,
                manifest?.ModuleTypes));
        }
        foreach (var file in Directory.GetFiles(packsPath))
        {
            var name = Path.GetFileName(file)!;
            if (name.StartsWith('.'))
                continue;
            result.Add(new(name, name, "0", "", file, true, new FileInfo(file).Length, File.GetLastWriteTimeUtc(file)));
        }
        return result.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Parses manifest.json into a <see cref="PackManifest"/>; null when missing/unreadable.</summary>
    public static PackManifest? TryReadManifest(string packPath)
    {
        var manifestPath = Path.Combine(packPath, "manifest.json");
        if (!File.Exists(manifestPath))
            return null;
        try
        {
            var obj = JObject.Parse(File.ReadAllText(manifestPath));
            var header = obj["header"];
            var id = Guid.TryParse(header?["uuid"]?.Value<string>(), out var g) ? g : (Guid?)null;
            var version = ReadVersionArray(header?["version"]);
            var name = header?["name"]?.Value<string>()?.Trim();
            var desc = header?["description"]?.Value<string>()?.Trim();
            var modules = obj["modules"] is JArray mods
                ? mods.Select(m => m["type"]?.Value<string>()?.Trim().ToLowerInvariant())
                    .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).Distinct().ToList()
                : new List<string>();
            var deps = new List<PackDependency>();
            if (obj["dependencies"] is JArray depArr)
            {
                foreach (var d in depArr)
                {
                    if (d["module_name"]?.Value<string>() is string modName && !string.IsNullOrWhiteSpace(modName))
                    {
                        deps.Add(new(null, ReadVersionArray(d["version"]), modName));
                    }
                    else
                    {
                        var depId = Guid.TryParse(d["uuid"]?.Value<string>(), out var dg) ? dg : (Guid?)null;
                        deps.Add(new(depId, ReadVersionArray(d["version"]), null));
                    }
                }
            }
            var minEngine = obj["header"]?["min_engine_version"] is JToken min ? ReadVersionArray(min) : null;
            return new(id, version, name, desc, modules, deps, minEngine);
        }
        catch { }
        return null;
    }

    internal static int[] ReadVersionArray(JToken? token)
    {
        if (token is JArray arr && arr.Count > 0)
        {
            var nums = new List<int>();
            foreach (var item in arr)
            {
                if (int.TryParse(item.ToString(), out var n))
                    nums.Add(Math.Max(0, n));
                else
                    nums.Add(0);
            }
            return nums.Count == 0 ? [0, 0, 0] : nums.ToArray();
        }
        return [0, 0, 0];
    }

    public static string FormatVersion(int[]? version) =>
        version is null || version.Length == 0 ? "0" : string.Join(".", version);

    /// <returns>&lt;0 when a&lt;b, 0 when equal, &gt;0 when a&gt;b (missing parts treated as 0).</returns>
    public static int CompareVersions(int[]? a, int[]? b)
    {
        a ??= [0, 0, 0];
        b ??= [0, 0, 0];
        var len = Math.Max(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y)
                return x.CompareTo(y);
        }
        return 0;
    }

    /// <summary>Strips Bedrock formatting codes (§X and control chars) for display.</summary>
    public static string? CleanDisplayText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '§')
            {
                i++; // skip the format code character too
                continue;
            }
            if (char.IsControl(c))
                continue;
            sb.Append(c);
        }
        var cleaned = sb.ToString().Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    public static string? ReadPackDisplayName(string packPath) =>
        CleanDisplayText(TryReadManifest(packPath)?.Name);

    public static string? ReadPackDisplayVersion(string packPath)
    {
        var manifest = TryReadManifest(packPath);
        return manifest is null ? null : FormatVersion(manifest.Version);
    }

    public static string? ReadPackDescription(string packPath) =>
        CleanDisplayText(TryReadManifest(packPath)?.Description);

    public static void DeleteWorld(this BedrockServer server, string worldName)
    {
        var path = Path.Combine(server.GetWorldsPath(), worldName);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    public static void DeletePack(this BedrockServer server, string worldName, PackKind kind, string packName)
    {
        var folder = kind == PackKind.Resource ? ResourcePacksFolderName : BehaviorPacksFolderName;
        var dirPath = Path.Combine(server.GetWorldsPath(), worldName, folder, packName);
        Guid? packId = null;
        if (Directory.Exists(dirPath))
        {
            packId = TryReadManifest(dirPath)?.PackId;
            Directory.Delete(dirPath, recursive: true);
        }
        else if (File.Exists(dirPath))
        {
            File.Delete(dirPath);
        }
        // Drop the world activation pin so BDS doesn't reference a missing pack.
        if (packId.HasValue)
        {
            try { RemoveWorldPackEntry(Path.Combine(server.GetWorldsPath(), worldName), kind, packId.Value); }
            catch { }
        }
    }

    #region World pack pins (world_resource_packs.json / world_behavior_packs.json)

    internal static string GetWorldPackJsonPath(string worldPath, PackKind kind) =>
        Path.Combine(worldPath, kind == PackKind.Resource ? "world_resource_packs.json" : "world_behavior_packs.json");

    internal static string GetWorldPackHistoryPath(string worldPath, PackKind kind) =>
        Path.Combine(worldPath, kind == PackKind.Resource ? "world_resource_pack_history.json" : "world_behavior_pack_history.json");

    /// <summary>Adds or updates the {pack_id, version} activation pin BDS uses to enable the pack.</summary>
    public static void SyncWorldPackEntry(string worldPath, PackKind kind, Guid packId, int[] version)
    {
        var path = GetWorldPackJsonPath(worldPath, kind);
        JArray arr;
        if (File.Exists(path))
        {
            try { arr = JArray.Parse(File.ReadAllText(path)); }
            catch { arr = []; }
        }
        else
        {
            arr = [];
        }
        var idText = packId.ToString();
        JObject? match = null;
        foreach (var item in arr.OfType<JObject>())
        {
            if (string.Equals(item["pack_id"]?.Value<string>(), idText, StringComparison.OrdinalIgnoreCase))
            {
                match = item;
                break;
            }
        }
        var versionArr = new JArray(version.Select(v => v));
        if (match is null)
        {
            arr.Add(new JObject { ["pack_id"] = idText, ["version"] = versionArr });
        }
        else
        {
            match["version"] = versionArr;
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, arr.ToString(Newtonsoft.Json.Formatting.Indented));
    }

    public static void RemoveWorldPackEntry(string worldPath, PackKind kind, Guid packId)
    {
        var path = GetWorldPackJsonPath(worldPath, kind);
        if (!File.Exists(path))
            return;
        try
        {
            var arr = JArray.Parse(File.ReadAllText(path));
            var idText = packId.ToString();
            var removed = false;
            for (var i = arr.Count - 1; i >= 0; i--)
            {
                if (arr[i] is JObject obj &&
                    string.Equals(obj["pack_id"]?.Value<string>(), idText, StringComparison.OrdinalIgnoreCase))
                {
                    arr.RemoveAt(i);
                    removed = true;
                }
            }
            if (removed)
                File.WriteAllText(path, arr.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        catch { }
    }

    /// <summary>
    /// Keeps BDS's world_*_pack_history.json ({packs: [{can_be_redownloaded, name, uuid, version}]})
    /// in step with the activation pins. Raw manifest names are stored, matching BDS style.
    /// </summary>
    public static void SyncWorldPackHistory(string worldPath, PackKind kind, Guid packId, int[] version, string? name)
    {        var path = GetWorldPackHistoryPath(worldPath, kind);
        JObject root;
        if (File.Exists(path))
        {
            try
            {
                var parsed = JObject.Parse(File.ReadAllText(path));
                root = parsed["packs"] is JArray ? parsed : new JObject { ["packs"] = new JArray() };
            }
            catch { root = new JObject { ["packs"] = new JArray() }; }
        }
        else
        {
            root = new JObject { ["packs"] = new JArray() };
        }
        var arr = (JArray)root["packs"]!;
        var idText = packId.ToString();
        JObject? match = null;
        foreach (var item in arr.OfType<JObject>())
        {
            var key = item["uuid"]?.Value<string>() ?? item["pack_id"]?.Value<string>();
            if (string.Equals(key, idText, StringComparison.OrdinalIgnoreCase))
            {
                match = item;
                break;
            }
        }
        var versionArr = new JArray(version.Select(v => v));
        if (match is null)
        {
            arr.Add(new JObject
            {
                ["can_be_redownloaded"] = false,
                ["name"] = name ?? idText,
                ["uuid"] = idText,
                ["version"] = versionArr,
            });
        }
        else
        {
            match["version"] = versionArr;
            if (!string.IsNullOrWhiteSpace(name))
                match["name"] = name;
            if (match["uuid"] is null)
                match["uuid"] = idText;
            if (match["can_be_redownloaded"] is null)
                match["can_be_redownloaded"] = false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch { }
    }

    /// <summary>
    /// Best-effort backfill: ensures every installed pack has a history entry matching
    /// its installed manifest (fixes worlds whose pins moved ahead of history, e.g. after an update).
    /// </summary>
    public static void TryRepairPackHistory(string worldPath)
    {
        try
        {
            foreach (var kind in new[] { PackKind.Resource, PackKind.Behavior })
            {
                var folder = kind == PackKind.Resource ? ResourcePacksFolderName : BehaviorPacksFolderName;
                var packsPath = Path.Combine(worldPath, folder);
                if (!Directory.Exists(packsPath))
                    continue;
                foreach (var dir in Directory.GetDirectories(packsPath))
                {
                    var manifest = TryReadManifest(dir);
                    if (manifest?.PackId.HasValue != true)
                        continue;
                    SyncWorldPackHistory(worldPath, kind, manifest.PackId.Value, manifest.Version, manifest.Name);
                }
            }
        }
        catch { }
    }

    internal static HashSet<Guid> GetWorldPackPins(string worldPath)
    {
        var ids = new HashSet<Guid>();
        foreach (var kind in new[] { PackKind.Resource, PackKind.Behavior })
        {
            var path = GetWorldPackJsonPath(worldPath, kind);
            if (!File.Exists(path))
                continue;
            try
            {
                foreach (var item in JArray.Parse(File.ReadAllText(path)).OfType<JObject>())
                {
                    if (Guid.TryParse(item["pack_id"]?.Value<string>(), out var g))
                        ids.Add(g);
                }
            }
            catch { }
        }
        return ids;
    }

    /// <summary>
    /// Checks the stock content BDS needs at startup (vanilla packs, definitions).
    /// Returns human-readable relative paths that are missing; empty means healthy.
    /// A missing vanilla pack makes BDS quit with "Failed to load Vanilla Resource Pack".
    /// </summary>
    public static List<string> GetMissingStockContent(this BedrockServer server)
    {
        var missing = new List<string>();
        var root = server.ServerPath;
        if (!File.Exists(Path.Combine(root, ResourcePacksFolderName, "vanilla", "manifest.json")))
            missing.Add("resource_packs/vanilla");
        if (!File.Exists(Path.Combine(root, BehaviorPacksFolderName, "vanilla", "manifest.json")))
            missing.Add("behavior_packs/vanilla");
        if (!Directory.Exists(Path.Combine(root, "definitions")))
            missing.Add("definitions");
        return missing;
    }

    /// <summary>Parses a BDS console line like "... Version: 1.26.21.1" into a version string.</summary>
    public static string? ParseServerVersionLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(line, @"Version:\s*(\d+(?:\.\d+){1,3})");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>True when the server version satisfies the pack's min_engine_version (or either is unknown).</summary>
    public static bool IsEngineCompatible(int[]? minEngine, string? serverVersion)
    {
        if (minEngine is null || minEngine.Length == 0 || string.IsNullOrWhiteSpace(serverVersion))
            return true;
        var parts = serverVersion.Split('.')
            .Select(p => int.TryParse(p, out var n) ? n : 0)
            .ToArray();
        return CompareVersions(parts, minEngine) >= 0;
    }

    #endregion

    #region Pack folder naming & repair

    private static readonly HashSet<string> GenericPackFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bp", "rp", "pack", "packs", "addon", "addons",
        "behavior", "behaviour", "behaviorpack", "behaviorpacks", "behaviourpack",
        "resource", "resources", "resourcepack", "resourcepacks",
        "data", "root", "packroot", "mcpack", "mcaddon",
    };

    internal static bool IsGenericPackFolderName(string name)
    {
        var stripped = new string(name.Where(char.IsLetterOrDigit).ToArray());
        return stripped.Length < 3 || GenericPackFolderNames.Contains(stripped);
    }

    /// <summary>Builds a stable, human-meaningful folder name (never from raw color-coded header names).</summary>
    public static string BuildPackFolderCandidate(PackManifest? manifest, string? archiveFolderName, string archiveFileName)
    {
        string? candidate = null;
        if (!string.IsNullOrWhiteSpace(archiveFolderName))
        {
            var clean = SanitizeFileName(archiveFolderName.Trim());
            if (!string.IsNullOrWhiteSpace(clean) && !IsGenericPackFolderName(clean))
                candidate = clean;
        }
        if (candidate is null && manifest?.Name is not null)
        {
            var stripped = CleanDisplayText(manifest.Name);
            if (!string.IsNullOrWhiteSpace(stripped) &&
                !stripped.Equals("pack.name", StringComparison.OrdinalIgnoreCase))
            {
                var ver = FormatVersion(manifest.Version);
                var name = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ").Trim();
                if (name.Length > 60)
                    name = name[..60].Trim();
                // Keep the version visible unless the name already contains it.
                candidate = name.Contains(ver, StringComparison.OrdinalIgnoreCase) ? name : $"{name} {ver}";
                candidate = SanitizeFileName(candidate);
            }
        }
        if (string.IsNullOrWhiteSpace(candidate))
        {
            var fromFile = SanitizeFileName(StripArchiveExtensions(archiveFileName).Trim());
            candidate = string.IsNullOrWhiteSpace(fromFile) ? "imported_pack" : fromFile;
        }
        if (manifest?.PackId.HasValue == true && candidate.Length < 4)
            candidate = $"pack_{manifest.PackId.Value.ToString("N")[..8]}_{FormatVersion(manifest.Version)}";
        return candidate.Trim();
    }

    public static bool LooksLikeCorruptPackFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;
        if (name.Any(char.IsControl))
            return true;
        // e.g. TekRealm's "[2.9": truncated color-coded header name.
        if (name.Length <= 4)
            return true;
        if (name.StartsWith('[') && name.Length < 10 && !Directory.Exists(name))
            return name.Count(c => c == '.') <= 2;
        return false;
    }

    /// <summary>Best-effort repair of mangled pack folder names (e.g. "[2.9"). Idempotent.</summary>
    public static void TryRepairPackFolderNames(string worldPath)
    {
        try
        {
            foreach (var kind in new[] { PackKind.Resource, PackKind.Behavior })
            {
                var folder = kind == PackKind.Resource ? ResourcePacksFolderName : BehaviorPacksFolderName;
                var packsPath = Path.Combine(worldPath, folder);
                if (!Directory.Exists(packsPath))
                    continue;
                foreach (var dir in Directory.GetDirectories(packsPath))
                {
                    var name = Path.GetFileName(dir)!;
                    if (name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase) || !LooksLikeCorruptPackFolderName(name))
                        continue;
                    var manifest = TryReadManifest(dir);
                    if (manifest?.PackId is null)
                        continue;
                    var candidate = BuildPackFolderCandidate(manifest, null, name);
                    var fresh = UniquePackName(packsPath, candidate);
                    if (fresh.Equals(name, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        continue;
                    try { Directory.Move(dir, Path.Combine(packsPath, fresh)); }
                    catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>Routes a pack to its folder by module type; mixed/unknown falls back to <paramref name="preferred"/>.</summary>
    public static PackKind ClassifyPackKind(PackManifest? manifest, PackKind preferred)
    {
        if (manifest is null || manifest.ModuleTypes.Count == 0)
            return preferred;
        var hasResources = manifest.ModuleTypes.Contains("resources");
        var hasBehavior = manifest.ModuleTypes.Contains("data") || manifest.ModuleTypes.Contains("script");
        if (hasResources && !hasBehavior)
            return PackKind.Resource;
        if (hasBehavior && !hasResources)
            return PackKind.Behavior;
        return preferred;
    }

    internal static PackInfo? FindPackById(string packsPath, Guid packId)
    {
        if (!Directory.Exists(packsPath))
            return null;
        return ReadPacks(packsPath).FirstOrDefault(p =>
            p.PackId.HasValue && p.PackId.Value.Equals(packId));
    }

    #endregion

    #region World import / export

    public static string GetWorldExportExtension(WorldArchiveFormat format) => format switch
    {
        WorldArchiveFormat.TarGz => ".tar.gz",
        WorldArchiveFormat.McWorld => ".mcworld",
        _ => ".zip",
    };

    /// <summary>
    /// Exports a world. Archive root contains the world contents (db/, level.dat, ...),
    /// not a top-level world-name folder.
    /// </summary>
    public static async Task<(MemoryStream Stream, string FileName)> ExportWorldAsync(
        this BedrockServer server, string worldName, WorldArchiveFormat format,
        CancellationToken cancellationToken = default)
    {
        var worldPath = Path.Combine(server.GetWorldsPath(), worldName);
        if (!Directory.Exists(worldPath))
            throw new DirectoryNotFoundException($"World '{worldName}' not found.");

        var ext = GetWorldExportExtension(format);
        var fileName = $"{SanitizeFileName(worldName)}{ext}";
        var output = new MemoryStream();

        if (format == WorldArchiveFormat.TarGz)
        {
            await using var tmp = new MemoryStream();
            await TarFile.CreateFromDirectoryAsync(worldPath, tmp, includeBaseDirectory: false, cancellationToken);
            tmp.Seek(0, SeekOrigin.Begin);
            output = new MemoryStream();
            await using (var gz = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                await tmp.CopyToAsync(gz, cancellationToken);
            output.Seek(0, SeekOrigin.Begin);
        }
        else
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.EnumerateFiles(worldPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(worldPath, file).Replace(Path.DirectorySeparatorChar, '/');
                    zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                }
            }
            output.Seek(0, SeekOrigin.Begin);
        }

        return (output, fileName);
    }

    /// <summary>
    /// Imports .zip / .tar.gz / .mcworld archives into the worlds folder.
    /// - Archive holding &lt;WorldName&gt;/db/... imports each such folder as-is.
    /// - Archive holding db/... at its root uses levelname.txt to build &lt;WorldName&gt;/db/...
    /// - Anything else is ignored (no recognizable world).
    /// Returns the imported world names.
    /// </summary>
    public static async Task<List<string>> ImportWorldAsync(
        this BedrockServer server, Stream archiveStream, string fileName,
        CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var lower = fileName.ToLowerInvariant();
        var isTarGz = lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz");
        var isZip = ext is ".zip" or ".mcworld" || lower.EndsWith(".mcworld");

        if (!isTarGz && !isZip)
            throw new InvalidDataException($"Unsupported world archive '{fileName}'. Use .zip, .tar.gz or .mcworld.");

        var worldsPath = server.GetWorldsPath();
        Directory.CreateDirectory(worldsPath);
        var stage = Directory.CreateTempSubdirectory("mcworld-import-");
        try
        {
            if (isTarGz)
                await ExtractTarGzAsync(archiveStream, stage.FullName, cancellationToken);
            else
                await ExtractZipAsync(archiveStream, stage.FullName, cancellationToken);

            var worldRoots = FindWorldRoots(stage.FullName);
            var imported = new List<string>();

            if (worldRoots.Count == 0)
                throw new InvalidDataException(
                    $"No recognizable world in '{fileName}'. Expected <WorldName>/db/... or db/... with levelname.txt.");

            foreach (var root in worldRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string targetName;
                if (IsStagingRoot(root, stage.FullName))
                {
                    // Content sits at archive root: derive folder name from levelname.txt.
                    var levelName = ReadLevelName(root)
                        ?? SanitizeFileName(Path.GetFileNameWithoutExtension(StripArchiveExtensions(fileName)));
                    if (string.IsNullOrWhiteSpace(levelName))
                        levelName = "imported_world";
                    targetName = UniqueWorldName(worldsPath, levelName);
                    CopyDirectory(root, Path.Combine(worldsPath, targetName));
                }
                else
                {
                    targetName = UniqueWorldName(worldsPath, Path.GetFileName(root)!);
                    CopyDirectory(root, Path.Combine(worldsPath, targetName));
                }
                imported.Add(targetName);
            }

            return imported;
        }
        finally
        {
            try { Directory.Delete(stage.FullName, recursive: true); } catch { }
        }
    }

    internal static List<string> FindWorldRoots(string stagePath)
    {
        var roots = new List<string>();
        // Case B: archive root itself is world content (db/ at top level).
        if (LooksLikeWorld(stagePath))
            return [stagePath];

        // Case A: one or more top-level folders holding world content.
        foreach (var dir in Directory.GetDirectories(stagePath))
        {
            var name = Path.GetFileName(dir)!;
            if (name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
                continue;
            if (LooksLikeWorld(dir))
            {
                roots.Add(dir);
                continue;
            }
            // One level of nesting tolerance (e.g. wrapper folder around the world).
            var nested = Directory.GetDirectories(dir)
                .Where(d => !Path.GetFileName(d)!.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
                .Where(LooksLikeWorld)
                .ToList();
            if (nested.Count == 1 && Directory.GetFileSystemEntries(dir).Length == nested.Count)
                roots.AddRange(nested);
        }
        return roots;
    }

    internal static bool LooksLikeWorld(string dir) =>
        Directory.Exists(Path.Combine(dir, "db")) ||
        File.Exists(Path.Combine(dir, "level.dat")) ||
        File.Exists(Path.Combine(dir, "levelname.txt"));

    internal static bool IsStagingRoot(string root, string stagePath) =>
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(stagePath).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static string UniqueWorldName(string worldsPath, string baseName)
    {
        baseName = SanitizeFileName(baseName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "imported_world";
        var candidate = baseName;
        var i = 1;
        while (Directory.Exists(Path.Combine(worldsPath, candidate)) || File.Exists(Path.Combine(worldsPath, candidate)))
            candidate = $"{baseName} ({i++})";
        return candidate;
    }

    #endregion

    #region Pack import / export

    public static List<PackInfo> GetPacks(this BedrockServer server, string worldName, PackKind kind)
    {
        var folder = kind == PackKind.Resource ? ResourcePacksFolderName : BehaviorPacksFolderName;
        return ReadPacks(Path.Combine(server.GetWorldsPath(), worldName, folder));
    }

    public static string GetPackExportExtension(PackKind kind) =>
        kind == PackKind.Resource ? ".mcpack" : ".mcaddon";

    /// <summary>Exports one pack folder (or loose file) as .mcpack / .mcaddon (zip).</summary>
    public static Task<(MemoryStream Stream, string FileName)> ExportPackAsync(
        this BedrockServer server, string worldName, PackKind kind, string packName,
        CancellationToken cancellationToken = default)
    {
        var folder = kind == PackKind.Resource ? ResourcePacksFolderName : BehaviorPacksFolderName;
        var packPath = Path.Combine(server.GetWorldsPath(), worldName, folder, packName);
        var ext = GetPackExportExtension(kind);
        var fileName = $"{SanitizeFileName(packName)}{ext}";
        var output = new MemoryStream();

        if (File.Exists(packPath))
        {
            using var src = File.OpenRead(packPath);
            src.CopyTo(output);
            output.Seek(0, SeekOrigin.Begin);
            return Task.FromResult((output, Path.GetFileName(packPath)));
        }

        if (!Directory.Exists(packPath))
            throw new DirectoryNotFoundException($"Pack '{packName}' not found.");

        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(packPath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(packPath, file).Replace(Path.DirectorySeparatorChar, '/');
                zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
            }
        }
        output.Seek(0, SeekOrigin.Begin);
        return Task.FromResult((output, fileName));
    }

    /// <summary>
    /// Legacy import: stages the archive, auto-splits mixed bundles by module type
    /// (a .mcaddon may hold both behavior and resource packs) and installs everything.
    /// A pack whose uuid matches an installed pack is updated in place.
    /// For interactive conflict handling (update vs. keep-as-copy) use
    /// <see cref="StagePackImportAsync"/> + <see cref="CommitStagedPackAsync"/>.
    /// </summary>
    public static async Task<List<string>> ImportPackAsync(
        this BedrockServer server, string worldName, PackKind kind,
        Stream archiveStream, string fileName,
        CancellationToken cancellationToken = default)
    {
        var staged = await StagePackImportAsync(server, worldName, kind, archiveStream, fileName, cancellationToken);
        try
        {
            var imported = new List<string>();
            foreach (var pack in staged.Packs.ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await CommitStagedPackAsync(server, worldName, staged, pack, PackConflictChoice.Update, cancellationToken);
                imported.Add(result.PackName);
            }
            return imported;
        }
        finally
        {
            DiscardStagedImport(staged);
        }
    }

    /// <summary>
    /// Extracts a .mcpack / .mcaddon / .zip to a temp staging area and detects
    /// uuid matches against installed packs (the update detector).
    /// Nothing is copied into the world yet; caller commits each pack with
    /// <see cref="CommitStagedPackAsync"/> and finally calls <see cref="DiscardStagedImport"/>.
    /// </summary>
    public static async Task<StagedPackImport> StagePackImportAsync(
        this BedrockServer server, string worldName, PackKind preferredKind,
        Stream archiveStream, string fileName,
        CancellationToken cancellationToken = default)
    {
        var lower = fileName.ToLowerInvariant();
        if (!(lower.EndsWith(".mcpack") || lower.EndsWith(".mcaddon") || lower.EndsWith(".zip")))
            throw new InvalidDataException($"Unsupported pack file '{fileName}'. Use .mcpack, .mcaddon or .zip.");

        var worldsPath = server.GetWorldsPath();
        if (!Directory.Exists(Path.Combine(worldsPath, worldName)))
            throw new DirectoryNotFoundException($"World '{worldName}' not found.");

        var stageDir = Directory.CreateTempSubdirectory("mcpack-import-");
        try
        {
            await ExtractZipAsync(archiveStream, stageDir.FullName, cancellationToken);
            var packRoots = FindPackRoots(stageDir.FullName);
            if (packRoots.Count == 0)
                throw new InvalidDataException($"No recognizable pack in '{fileName}'. Expected a manifest.json.");

            var staged = new List<StagedPack>();
            foreach (var root in packRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = TryReadManifest(root);
                var kind = ClassifyPackKind(manifest, preferredKind);
                var folder = kind == PackKind.Resource ? ResourcePacksFolderName : BehaviorPacksFolderName;
                PackInfo? existing = null;
                if (manifest?.PackId.HasValue == true)
                    existing = FindPackById(Path.Combine(worldsPath, worldName, folder), manifest.PackId.Value);
                var archiveFolder = IsStagingRoot(root, stageDir.FullName)
                    ? null
                    : Path.GetFileName(root);
                var candidate = existing?.Name
                    ?? BuildPackFolderCandidate(manifest, archiveFolder, fileName);
                staged.Add(new(kind, root, manifest, candidate, existing));
            }
            return new StagedPackImport(stageDir.FullName, staged);
        }
        catch
        {
            try { Directory.Delete(stageDir.FullName, recursive: true); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Commits one staged pack: <see cref="PackConflictChoice.Update"/> replaces the
    /// installed folder in place; <see cref="PackConflictChoice.KeepBoth"/> assigns
    /// fresh unused uuids to the upload and installs it alongside. World pins are synced.
    /// </summary>
    public static Task<PackImportResult> CommitStagedPackAsync(
        this BedrockServer server, string worldName,
        StagedPackImport staged, StagedPack pack,
        PackConflictChoice choice,
        CancellationToken cancellationToken = default)
    {
        var worldPath = Path.Combine(server.GetWorldsPath(), worldName);
        if (!Directory.Exists(worldPath))
            throw new DirectoryNotFoundException($"World '{worldName}' not found.");
        var folder = pack.Kind == PackKind.Resource ? ResourcePacksFolderName : BehaviorPacksFolderName;
        var targetDir = Path.Combine(worldPath, folder);
        Directory.CreateDirectory(targetDir);

        // Case 1: confirmed update of the matched pack — in-place replace, keep folder name.
        if (choice == PackConflictChoice.Update && pack.Existing is not null && pack.Manifest?.PackId.HasValue == true)
        {
            var dest = Path.Combine(targetDir, pack.Existing.Name);
            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);
            else if (File.Exists(dest))
                File.Delete(dest);
            CopyDirectory(pack.StagedPath, dest);
            SyncWorldPackEntry(worldPath, pack.Kind, pack.Manifest.PackId.Value, pack.Manifest.Version);
            SyncWorldPackHistory(worldPath, pack.Kind, pack.Manifest.PackId.Value, pack.Manifest.Version, pack.Manifest.Name);
            var cmp = CompareVersions(pack.Manifest.Version, pack.Existing.Version);
            var outcome = cmp > 0 ? PackImportOutcome.Updated
                : cmp < 0 ? PackImportOutcome.Downgraded
                : PackImportOutcome.SameVersion;
            return Task.FromResult(new PackImportResult(
                pack.Existing.Name, pack.Manifest.PackId, pack.Existing.Version, pack.Manifest.Version, outcome, pack.Kind));
        }

        // Case 2: fresh install, or "different pack with same uuid" — re-UUID then install as copy.
        Guid? finalId = pack.Manifest?.PackId;
        var version = pack.Manifest?.Version ?? [0, 0, 0];
        if (choice == PackConflictChoice.KeepBoth && pack.Existing is not null && finalId.HasValue)
        {
            finalId = AssignFreshPackIdentity(server, worldName, staged, pack);
            version = staged.Packs.FirstOrDefault(p => p.StagedPath == pack.StagedPath)?.Manifest?.Version ?? version;
        }
        var name = UniquePackName(targetDir, pack.CandidateName);
        CopyDirectory(pack.StagedPath, Path.Combine(targetDir, name));
        if (finalId.HasValue)
        {
            SyncWorldPackEntry(worldPath, pack.Kind, finalId.Value, version);
            var freshName = TryReadManifest(pack.StagedPath)?.Name ?? pack.Manifest?.Name;
            SyncWorldPackHistory(worldPath, pack.Kind, finalId.Value, version, freshName);
        }
        var installedOutcome = choice == PackConflictChoice.KeepBoth && pack.Existing is not null
            ? PackImportOutcome.InstalledAsCopy
            : PackImportOutcome.Installed;
        return Task.FromResult(new PackImportResult(name, finalId, null, version, installedOutcome, pack.Kind));
    }

    public static void DiscardStagedImport(StagedPackImport staged)
    {
        try { if (Directory.Exists(staged.StagingRoot)) Directory.Delete(staged.StagingRoot, recursive: true); }
        catch { }
    }

    /// <summary>
    /// Rewrites the staged manifest so the upload becomes a distinct pack: new header uuid
    /// plus new module uuids, guaranteed unused in the world and the current batch.
    /// Paired packs from the same upload (BP&lt;-&gt;RP dependency) are relinked to the new uuid.
    /// Returns the fresh header uuid.
    /// </summary>
    internal static Guid AssignFreshPackIdentity(
        BedrockServer server, string worldName, StagedPackImport staged, StagedPack pack)
    {
        var oldId = pack.Manifest?.PackId ?? throw new InvalidOperationException("Staged pack has no uuid to replace.");
        var worldPath = Path.Combine(server.GetWorldsPath(), worldName);
        var used = GetWorldPackPins(worldPath);
        foreach (var kind in new[] { ResourcePacksFolderName, BehaviorPacksFolderName })
        {
            foreach (var info in ReadPacks(Path.Combine(worldPath, kind)))
            {
                if (info.PackId.HasValue)
                    used.Add(info.PackId.Value);
            }
        }
        foreach (var other in staged.Packs)
        {
            if (other.Manifest?.PackId.HasValue == true)
                used.Add(other.Manifest.PackId.Value);
        }

        Guid fresh;
        do { fresh = Guid.NewGuid(); } while (!used.Add(fresh));

        // Rewrite this pack's manifest: fresh header + module uuids.
        var manifestPath = Path.Combine(pack.StagedPath, "manifest.json");
        var obj = JObject.Parse(File.ReadAllText(manifestPath));
        if (obj["header"] is JObject header)
            header["uuid"] = fresh.ToString();
        if (obj["modules"] is JArray modules)
        {
            foreach (var m in modules.OfType<JObject>())
            {
                if (m["uuid"] is not null)
                    m["uuid"] = Guid.NewGuid().ToString();
            }
        }
        File.WriteAllText(manifestPath, obj.ToString(Newtonsoft.Json.Formatting.Indented));

        // Relink batch siblings that depend on the old uuid (e.g. the paired RP/BP).
        for (var i = 0; i < staged.Packs.Count; i++)
        {
            var sibling = staged.Packs[i];
            if (sibling.StagedPath == pack.StagedPath || sibling.Manifest is null)
                continue;
            if (!sibling.Manifest.Dependencies.Any(d => d.PackId.HasValue && d.PackId.Value.Equals(oldId)))
                continue;
            var sibPath = Path.Combine(sibling.StagedPath, "manifest.json");
            try
            {
                var sibObj = JObject.Parse(File.ReadAllText(sibPath));
                var changed = false;
                if (sibObj["dependencies"] is JArray sibDeps)
                {
                    foreach (var d in sibDeps.OfType<JObject>())
                    {
                        if (Guid.TryParse(d["uuid"]?.Value<string>(), out var dg) && dg.Equals(oldId))
                        {
                            d["uuid"] = fresh.ToString();
                            changed = true;
                        }
                    }
                }
                if (changed)
                    File.WriteAllText(sibPath, sibObj.ToString(Newtonsoft.Json.Formatting.Indented));
                staged.Packs[i] = sibling with { Manifest = TryReadManifest(sibling.StagedPath) };
            }
            catch { }
        }

        // Refresh this pack's staged record with the rewritten manifest.
        for (var i = 0; i < staged.Packs.Count; i++)
        {
            if (staged.Packs[i].StagedPath == pack.StagedPath)
                staged.Packs[i] = staged.Packs[i] with { Manifest = TryReadManifest(pack.StagedPath) };
        }
        return fresh;
    }

    internal static List<string> FindPackRoots(string stagePath)
    {
        if (File.Exists(Path.Combine(stagePath, "manifest.json")))
            return [stagePath];
        var roots = new List<string>();
        foreach (var dir in Directory.GetDirectories(stagePath, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(dir)!;
            if (name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(Path.Combine(dir, "manifest.json")))
                roots.Add(dir);
        }
        // Keep only top-most pack roots (a .mcaddon may nest packs one level deep).
        return roots.Where(r => !roots.Any(other =>
                other != r && r.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.Ordinal))).ToList();
    }

    internal static string UniquePackName(string packsPath, string baseName)
    {
        baseName = SanitizeFileName(baseName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "imported_pack";
        var candidate = baseName;
        var i = 1;
        while (Directory.Exists(Path.Combine(packsPath, candidate)) || File.Exists(Path.Combine(packsPath, candidate)))
            candidate = $"{baseName} ({i++})";
        return candidate;
    }

    #endregion

    #region Archive helpers

    internal static async Task ExtractZipAsync(Stream archiveStream, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        var destFull = Path.GetFullPath(destination);
        using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
                continue;
            var normalized = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = Path.GetFullPath(Path.Combine(destination, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destFull + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !target.Equals(destFull, StringComparison.Ordinal))
                continue; // zip-slip: ignore
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var src = entry.Open();
            await using var dst = File.Create(target);
            await src.CopyToAsync(dst, ct);
        }
    }

    internal static async Task ExtractTarGzAsync(Stream archiveStream, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        await using var gz = new GZipStream(archiveStream, CompressionMode.Decompress, leaveOpen: true);
        await TarFile.ExtractToDirectoryAsync(gz, destination, overwriteFiles: true, cancellationToken: ct);
        // Drop macOS metadata if present.
        var macOs = Path.Combine(destination, "__MACOSX");
        if (Directory.Exists(macOs))
            Directory.Delete(macOs, recursive: true);
    }

    internal static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(dir)!.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    internal static long GetDirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
        }
        catch { return 0; }
    }

    internal static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim().TrimEnd('.');
    }

    internal static string StripArchiveExtensions(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz"))
            return fileName[..^7];
        foreach (var ext in new[] { ".mcworld", ".mcpack", ".mcaddon", ".tgz", ".zip" })
            if (lower.EndsWith(ext))
                return fileName[..^ext.Length];
        return Path.GetFileNameWithoutExtension(fileName);
    }

    #endregion
}
