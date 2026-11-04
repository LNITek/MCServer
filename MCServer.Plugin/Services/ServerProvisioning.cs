using System.Text.RegularExpressions;

namespace MCServer.Plugins;

/// <summary>
/// Shared validation used by the UI dialogs, the offline CLI and the named-pipe
/// host. Keeping the rules in one place prevents the CLI from silently accepting
/// servers that the UI would reject.
/// </summary>
public static class ServerProvisioning
{
    private static readonly Regex InvalidFileNamePattern = new(
        $"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Normalises the identifier used for the default server folder.</summary>
    public static string NormalizeServerId(string name) => (name ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Validates a user supplied server name and returns a stable ID.</summary>
    public static ServerValidationResult ValidateCreate(
        string? name,
        string? type,
        IEnumerable<ServerSettings> existing,
        string filesRoot)
    {
        string? error = ValidateName(name, out var trimmedName, existing, filesRoot);
        if (error is not null)
            return ServerValidationResult.Failed(error);

        if (string.IsNullOrWhiteSpace(type))
            return ServerValidationResult.Failed("A server type is required.");

        var map = new Dictionary<string, ServerSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in existing)
        {
            if (!string.IsNullOrWhiteSpace(setting.ID))
                map[setting.ID] = setting;
        }

        var id = NormalizeServerId(trimmedName);
        if (map.ContainsKey(id))
            return ServerValidationResult.Failed($"A server with the ID '{id}' already exists.");

        var folder = Path.GetFullPath(Path.Combine(filesRoot, id));
        if (Directory.Exists(folder))
            return ServerValidationResult.Failed($"A server folder '{folder}' already exists.");

        return ServerValidationResult.Success(id, trimmedName, folder);
    }

    /// <summary>Validates a name and its default folder without looking at an ID.</summary>
    public static string? ValidateName(string? name, out string trimmedName, IEnumerable<ServerSettings> existing,
        string filesRoot)
    {
        trimmedName = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedName))
            return "Enter a server name.";

        if (InvalidFileNamePattern.IsMatch(trimmedName))
            return $"Server name contains invalid characters: {trimmedName}";

        if (trimmedName is "." or "..")
            return "The server name cannot be '.' or '..'.";

        if (trimmedName.Length > 80)
            return "The server name cannot be longer than 80 characters.";

        foreach (var setting in existing)
        {
            if (string.Equals(setting.Name?.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase))
                return $"A server named '{trimmedName}' already exists.";
        }

        var folder = Path.GetFullPath(Path.Combine(filesRoot, NormalizeServerId(trimmedName)));
        var root = Path.GetFullPath(filesRoot);
        var relative = Path.GetRelativePath(root, folder);

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative))
            return "The server folder must be inside the MCServer servers directory.";

        return null;
    }

    /// <summary>Checks that a delete target is still inside the files root.</summary>
    public static void ValidateDeletePath(string filesRoot, string serverPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filesRoot);

        if (string.IsNullOrWhiteSpace(serverPath))
            throw new InvalidOperationException("Server path is empty; refusing to delete.");

        var path = Path.GetFullPath(serverPath);
        var root = Path.GetFullPath(filesRoot);
        var fsRoot = Path.GetPathRoot(path);

        if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete the shared server files root.");

        if (fsRoot is not null && path.Equals(fsRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to delete filesystem root '{path}'.");

        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative))
            throw new InvalidOperationException($"Refusing to delete '{path}' because it is outside the servers directory.");

        if (File.Exists(path))
            throw new InvalidOperationException("The server path points at a file; refusing to delete it.");
    }

    /// <summary>Checks the live server before a destructive delete is allowed.</summary>
    public static void ValidateCanDelete(IGameServer server)
    {
        ArgumentNullException.ThrowIfNull(server);

        if (server.ServerRunningStatus)
            throw new InvalidOperationException($"Stop server '{server.Settings.Name}' before deleting it.");

        ValidateDeletePath(server.Host.ServerFiles, server.ServerPath);
    }

    /// <summary>Removes an existing server directory after safety checks.</summary>
    public static void DeleteServerDirectory(IGameServer server)
    {
        ValidateCanDelete(server);

        var path = Path.GetFullPath(server.ServerPath);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    /// <summary>Creates an empty server folder, refusing to overwrite existing files.</summary>
    public static void CreateServerDirectory(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var full = Path.GetFullPath(folder);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException($"The server folder '{full}' already exists and is not empty.");

        Directory.CreateDirectory(full);
    }

    /// <summary>Removes a newly-created folder after a failed create operation.</summary>
    public static void CleanupFailedCreate(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // Best effort cleanup; the original failure is more useful to report.
        }
    }
}

public readonly record struct ServerValidationResult(bool IsValid, string? Error, string Id, string Name, string Folder)
{
    public static ServerValidationResult Success(string id, string name, string folder) =>
        new(true, null, id, name, folder);

    public static ServerValidationResult Failed(string error) =>
        new(false, error, string.Empty, string.Empty, string.Empty);
}
