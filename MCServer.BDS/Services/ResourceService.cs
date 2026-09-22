using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

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
    string Path,
    bool IsFile,
    long SizeBytes,
    DateTime LastModified);

public sealed record WorldInfo(
    string Name,
    string DisplayName,
    string Path,
    long SizeBytes,
    DateTime LastModified,
    IReadOnlyList<PackInfo> ResourcePacks,
    IReadOnlyList<PackInfo> BehaviorPacks);

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
            result.Add(new(
                name,
                ReadPackDisplayName(dir) ?? name,
                dir,
                false,
                GetDirectorySize(dir),
                Directory.GetLastWriteTimeUtc(dir)));
        }
        foreach (var file in Directory.GetFiles(packsPath))
        {
            var name = Path.GetFileName(file)!;
            if (name.StartsWith('.'))
                continue;
            result.Add(new(name, name, file, true, new FileInfo(file).Length, File.GetLastWriteTimeUtc(file)));
        }
        return result.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? ReadPackDisplayName(string packPath)
    {
        var manifestPath = Path.Combine(packPath, "manifest.json");
        if (!File.Exists(manifestPath))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.TryGetProperty("header", out var header) &&
                header.TryGetProperty("name", out var name))
                return name.GetString()?.Trim() is { Length: > 0 } n ? n : null;
        }
        catch { }
        return null;
    }

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
        if (Directory.Exists(dirPath))
        {
            Directory.Delete(dirPath, recursive: true);
            return;
        }
        if (File.Exists(dirPath))
            File.Delete(dirPath);
    }

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
    /// Imports .mcpack / .mcaddon / .zip into a world's resource_packs or behavior_packs folder.
    /// Folders containing manifest.json are imported as packs; a manifest at the archive
    /// root is wrapped in a folder named after the archive (or its header name).
    /// </summary>
    public static async Task<List<string>> ImportPackAsync(
        this BedrockServer server, string worldName, PackKind kind,
        Stream archiveStream, string fileName,
        CancellationToken cancellationToken = default)
    {
        var lower = fileName.ToLowerInvariant();
        var expected = kind == PackKind.Resource ? ".mcpack" : ".mcaddon";
        if (!(lower.EndsWith(expected) || lower.EndsWith(".zip")))
            throw new InvalidDataException($"Unsupported pack file '{fileName}'. Use {expected} or .zip.");

        var folder = kind == PackKind.Resource ? ResourcePacksFolderName : BehaviorPacksFolderName;
        var targetDir = Path.Combine(server.GetWorldsPath(), worldName, folder);
        Directory.CreateDirectory(targetDir);
        var stage = Directory.CreateTempSubdirectory("mcpack-import-");
        try
        {
            await ExtractZipAsync(archiveStream, stage.FullName, cancellationToken);
            var packRoots = FindPackRoots(stage.FullName);
            if (packRoots.Count == 0)
                throw new InvalidDataException($"No recognizable pack in '{fileName}'. Expected a manifest.json.");

            var imported = new List<string>();
            foreach (var root in packRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string packName;
                if (IsStagingRoot(root, stage.FullName))
                {
                    var header = ReadPackDisplayName(root);
                    packName = SanitizeFileName(header ?? Path.GetFileNameWithoutExtension(fileName));
                    if (string.IsNullOrWhiteSpace(packName))
                        packName = "imported_pack";
                    packName = UniquePackName(targetDir, packName);
                    CopyDirectory(root, Path.Combine(targetDir, packName));
                }
                else
                {
                    packName = UniquePackName(targetDir, Path.GetFileName(root)!);
                    CopyDirectory(root, Path.Combine(targetDir, packName));
                }
                imported.Add(packName);
            }
            return imported;
        }
        finally
        {
            try { Directory.Delete(stage.FullName, recursive: true); } catch { }
        }
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
