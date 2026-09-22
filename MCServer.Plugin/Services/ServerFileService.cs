using System.Formats.Tar;
using System.IO.Compression;

namespace MCServer.Plugins;

public enum FileArchiveFormat
{
    Zip,
    TarGz,
}

public sealed record ServerEntry(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long SizeBytes,
    DateTime LastModified);

/// <summary>
/// File/folder management rooted at the server directory. Works for every
/// server type through <see cref="IGameServer"/>.
/// Every relative path is validated to stay inside <see cref="IGameServer.ServerPath"/>.
/// </summary>
public static class ServerFileService
{
    public const long MaxTextFileBytes = 1024 * 1024;
    public const long MaxUploadBytes = 1024L * 1024 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".json", ".properties", ".yml", ".yaml", ".xml", ".html",
        ".log", ".cfg", ".conf", ".toml", ".ini", ".md",
        ".mcfunction", ".csv", ".js", ".ts",
    };

    public static bool IsArchiveFile(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.EndsWith(".zip") || lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz");
    }

    public static bool IsEditableFile(string name) =>
        TextExtensions.Contains(Path.GetExtension(name));

    public static string GetSafeFullPath(this IGameServer server, string relativePath)
    {
        var root = GetServerRoot(server);
        var normalized = (relativePath ?? "").Replace('\\', '/').Trim().TrimStart('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p is "." or ".."))
            throw new UnauthorizedAccessException("Path must stay inside the server folder.");
        var combined = parts.Length == 0
            ? root
            : Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!combined.Equals(root, cmp) && !combined.StartsWith(root + Path.DirectorySeparatorChar, cmp))
            throw new UnauthorizedAccessException("Path must stay inside the server folder.");
        return combined;
    }

    /// <summary>
    /// Normalized server root without any trailing separator.
    /// (Settings paths like <c>./../Data/Bedrock Server/</c> keep their trailing
    /// slash through <see cref="Path.GetFullPath"/>, which would otherwise break
    /// the <c>root + separator</c> prefix check for every child path.)
    /// </summary>
    internal static string GetServerRoot(IGameServer server)
    {
        var root = Path.GetFullPath(server.ServerPath);
        var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) ? Path.GetPathRoot(root)! : trimmed;
    }

    public static string ToRelativePath(this IGameServer server, string fullPath)
    {
        var relative = Path.GetRelativePath(GetServerRoot(server), Path.GetFullPath(fullPath));
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public static List<ServerEntry> ListEntries(this IGameServer server, string relativePath)
    {
        var dir = server.GetSafeFullPath(relativePath);
        if (!Directory.Exists(dir))
            return [];
        var entries = new List<ServerEntry>();
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var name = Path.GetFileName(sub)!;
            entries.Add(new(name, server.ToRelativePath(sub), true,
                GetDirectorySize(sub), Directory.GetLastWriteTimeUtc(sub)));
        }
        foreach (var file in Directory.GetFiles(dir))
        {
            var info = new FileInfo(file);
            entries.Add(new(info.Name, server.ToRelativePath(file), false,
                info.Length, info.LastWriteTimeUtc));
        }
        return entries
            .OrderBy(e => !e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ReadTextFile(this IGameServer server, string relativePath)
    {
        if (!IsEditableFile(Path.GetFileName(relativePath)))
            throw new InvalidDataException($"'{relativePath}' is not an editable text file.");
        var full = server.GetSafeFullPath(relativePath);
        var info = new FileInfo(full);
        if (!info.Exists)
            throw new FileNotFoundException($"File '{relativePath}' not found.");
        if (info.Length > MaxTextFileBytes)
            throw new InvalidDataException($"File is larger than {MaxTextFileBytes / 1024} KB; editing is disabled.");
        return File.ReadAllText(full);
    }

    public static void WriteTextFile(this IGameServer server, string relativePath, string content)
    {
        if (!IsEditableFile(Path.GetFileName(relativePath)))
            throw new InvalidDataException($"'{relativePath}' is not an editable text file.");
        if (content.Length > MaxTextFileBytes * 4)
            throw new InvalidDataException("Content is too large.");
        File.WriteAllText(server.GetSafeFullPath(relativePath), content);
    }

    public static string CreateFolder(this IGameServer server, string parentRelativePath, string name)
    {
        var validated = ValidateSingleName(name);
        var full = UniqueFullPath(Path.Combine(server.GetSafeFullPath(parentRelativePath), validated));
        Directory.CreateDirectory(full);
        return server.ToRelativePath(full);
    }

    public static string RenameEntry(this IGameServer server, string relativePath, string newName)
    {
        var validated = ValidateSingleName(newName);
        var src = server.GetSafeFullPath(relativePath);
        var dest = Path.Combine(Path.GetDirectoryName(src)!, validated);
        if (Path.GetFullPath(src).Equals(Path.GetFullPath(dest), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return relativePath;
        dest = UniqueFullPath(dest, allowSelf: src);
        if (Directory.Exists(src))
            Directory.Move(src, dest);
        else if (File.Exists(src))
            File.Move(src, dest);
        else
            throw new FileNotFoundException($"'{relativePath}' not found.");
        return server.ToRelativePath(dest);
    }

    public static void DeleteEntry(this IGameServer server, string relativePath)
    {
        var full = server.GetSafeFullPath(relativePath);
        if (full.Equals(GetServerRoot(server), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Cannot delete the server root folder.");
        if (Directory.Exists(full))
            Directory.Delete(full, recursive: true);
        else if (File.Exists(full))
            File.Delete(full);
    }

    public static string SaveUploadedFile(this IGameServer server, string parentRelativePath, string fileName, Stream content)
    {
        var validated = ValidateSingleName(Path.GetFileName(fileName));
        var parent = server.GetSafeFullPath(parentRelativePath);
        Directory.CreateDirectory(parent);
        var dest = Path.Combine(parent, validated);
        using var dst = File.Create(dest);
        content.CopyTo(dst);
        return server.ToRelativePath(dest);
    }

    public static async Task ExtractUploadedArchiveAsync(this IGameServer server, string parentRelativePath,
        string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        if (!IsArchiveFile(fileName))
            throw new InvalidDataException($"'{fileName}' is not a .zip or .tar.gz archive.");
        var parent = server.GetSafeFullPath(parentRelativePath);
        Directory.CreateDirectory(parent);
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".zip"))
            await ExtractZipAsync(content, parent, cancellationToken);
        else
            await ExtractTarGzAsync(content, parent, cancellationToken);
    }

    public static string CopyEntry(this IGameServer server, string sourceRelativePath, string destDirRelativePath)
    {
        var src = server.GetSafeFullPath(sourceRelativePath);
        var destDir = server.GetSafeFullPath(destDirRelativePath);
        if (!Directory.Exists(destDir))
            throw new DirectoryNotFoundException($"Folder '{destDirRelativePath}' not found.");
        var dest = UniqueFullPath(Path.Combine(destDir, Path.GetFileName(src)));
        if (Directory.Exists(src))
            CopyDirectory(src, dest);
        else if (File.Exists(src))
            File.Copy(src, dest);
        else
            throw new FileNotFoundException($"'{sourceRelativePath}' not found.");
        return server.ToRelativePath(dest);
    }

    public static string MoveEntry(this IGameServer server, string sourceRelativePath, string destDirRelativePath)
    {
        var src = server.GetSafeFullPath(sourceRelativePath);
        var destDir = server.GetSafeFullPath(destDirRelativePath);
        if (!Directory.Exists(destDir))
            throw new DirectoryNotFoundException($"Folder '{destDirRelativePath}' not found.");
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (Directory.Exists(src) && (destDir.Equals(src, cmp) || destDir.StartsWith(src + Path.DirectorySeparatorChar, cmp)))
            throw new InvalidOperationException("Cannot move a folder into itself.");
        if (Path.GetDirectoryName(src)!.Equals(destDir, cmp))
            return sourceRelativePath; // already there
        var dest = UniqueFullPath(Path.Combine(destDir, Path.GetFileName(src)));
        if (Directory.Exists(src))
            Directory.Move(src, dest);
        else if (File.Exists(src))
            File.Move(src, dest);
        else
            throw new FileNotFoundException($"'{sourceRelativePath}' not found.");
        return server.ToRelativePath(dest);
    }

    public static Stream OpenReadEntry(this IGameServer server, string relativePath)
    {
        var full = server.GetSafeFullPath(relativePath);
        if (Directory.Exists(full))
            throw new InvalidOperationException("Use folder download for directories.");
        return File.Open(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    public static async Task<(MemoryStream Stream, string FileName)> ExportFolderAsync(
        this IGameServer server, string relativePath, FileArchiveFormat format,
        CancellationToken cancellationToken = default)
    {
        var full = server.GetSafeFullPath(relativePath);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Folder '{relativePath}' not found.");
        var name = string.IsNullOrEmpty(relativePath) ? server.Settings.Name : Path.GetFileName(full)!;
        name = SanitizeFileName(name);
        if (string.IsNullOrWhiteSpace(name))
            name = "folder";

        if (format == FileArchiveFormat.TarGz)
        {
            await using var tmp = new MemoryStream();
            await TarFile.CreateFromDirectoryAsync(full, tmp, includeBaseDirectory: false, cancellationToken);
            tmp.Seek(0, SeekOrigin.Begin);
            var output = new MemoryStream();
            await using (var gz = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                await tmp.CopyToAsync(gz, cancellationToken);
            output.Seek(0, SeekOrigin.Begin);
            return (output, $"{name}.tar.gz");
        }

        var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(full, file).Replace(Path.DirectorySeparatorChar, '/');
                zip.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
            }
        }
        zipStream.Seek(0, SeekOrigin.Begin);
        return (zipStream, $"{name}.zip");
    }

    public static async Task<(MemoryStream Stream, string FileName)> ExportEntriesZipAsync(
        this IGameServer server, string parentRelativePath, IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        var parent = server.GetSafeFullPath(parentRelativePath);
        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in names)
            {
                var full = Path.Combine(parent, ValidateSingleName(name));
                server.GetSafeFullPath(
                    string.IsNullOrEmpty(parentRelativePath) ? name : $"{parentRelativePath.TrimEnd('/')}/{name}");
                if (Directory.Exists(full))
                {
                    foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        zip.CreateEntryFromFile(file,
                            $"{name}/{Path.GetRelativePath(full, file).Replace(Path.DirectorySeparatorChar, '/')}",
                            CompressionLevel.Optimal);
                    }
                }
                else if (File.Exists(full))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    zip.CreateEntryFromFile(full, name, CompressionLevel.Optimal);
                }
            }
        }
        output.Seek(0, SeekOrigin.Begin);
        return (output, "selection.zip");
    }

    #region Helpers

    internal static string ValidateSingleName(string name)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            throw new InvalidDataException("Name must not be empty.");
        if (name.Any(c => c is '/' or '\\') || Path.GetFileName(name) != name)
            throw new InvalidDataException($"'{name}' is not a plain file or folder name.");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"'{name}' contains invalid characters.");
        return name;
    }

    internal static string UniqueFullPath(string fullPath, string? allowSelf = null)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (allowSelf is not null && fullPath.Equals(Path.GetFullPath(allowSelf), cmp))
            return fullPath;
        if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
            return fullPath;
        var dir = Path.GetDirectoryName(fullPath)!;
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Directory.Exists(fullPath) ? "" : Path.GetExtension(fullPath);
        var i = 1;
        string candidate;
        do { candidate = Path.Combine(dir, $"{stem} ({i++}){ext}"); }
        while (Directory.Exists(candidate) || File.Exists(candidate));
        return candidate;
    }

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

    #endregion
}
