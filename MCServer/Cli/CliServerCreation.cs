using MCServer.BDS.Services;
using MCServer.Plugins;

namespace MCServer.Cli;

/// <summary>
/// Shared, non-UI server creation used by the offline CLI and the named-pipe
/// host. The UI dialog remains responsible for its own interactive flow.
/// </summary>
public static class CliServerCreation
{
    public static bool IsBedrockType(string? type) =>
        string.Equals(type, "BDS", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "MCBedrockServer", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "BedrockServer", StringComparison.OrdinalIgnoreCase);

    public static Task<CliCreateResult> CreateOfflineAsync(
        string filesRoot,
        string? name,
        string? type,
        bool preview,
        bool autoStart,
        bool noDownload,
        IReadOnlyCollection<ServerSettings> existing,
        IProgress<double>? progress,
        CancellationToken cancellationToken) =>
        CreateAsync(filesRoot, name, type, preview, autoStart, noDownload, existing,
            progress, cancellationToken);

    /// <summary>
    /// Validates, creates and downloads a server. Does not add the entry to
    /// settings; callers own persistence. On failure the newly-created folder is
    /// removed when it was created by this call.
    /// </summary>
    public static async Task<CliCreateResult> CreateAsync(
        string filesRoot,
        string? name,
        string? type,
        bool preview,
        bool autoStart,
        bool noDownload,
        IReadOnlyCollection<ServerSettings> existing,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var serverType = string.IsNullOrWhiteSpace(type) ? "BDS" : type.Trim();
        var validation = ServerProvisioning.ValidateCreate(name, serverType, existing, filesRoot);
        if (!validation.IsValid)
            return CliCreateResult.Failed(validation.Error ?? "Server validation failed.");

        var folder = validation.Folder;
        var folderExisted = Directory.Exists(folder);

        try
        {
            if (!noDownload)
            {
                if (!IsBedrockType(serverType))
                    return CliCreateResult.Failed(
                        $"No CLI download is registered for server type '{serverType}'. Use --type BDS or --no-download.");

                var details = await MaintenanceService.InstallNewestAsync(
                    folder,
                    preview,
                    OperatingSystem.IsWindows(),
                    progress,
                    cancellationToken);

                return CliCreateResult.Success(new ServerSettings
                {
                    ID = validation.Id,
                    Name = validation.Name,
                    Type = "BDS",
                    Version = details.Version,
                    Preview = preview,
                    AutoStart = autoStart,
                }, folder);
            }

            progress?.Report(0);
            ServerProvisioning.CreateServerDirectory(folder);
            progress?.Report(1);

            return CliCreateResult.Success(new ServerSettings
            {
                ID = validation.Id,
                Name = validation.Name,
                Type = serverType,
                Version = "0.0.0.0",
                Preview = preview,
                AutoStart = autoStart,
            }, folder);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!folderExisted)
                ServerProvisioning.CleanupFailedCreate(folder);

            return CliCreateResult.Failed($"Failed to create server: {ex.Message}");
        }
    }
}

public sealed record CliCreateResult(bool Ok, ServerSettings? Settings, string? Error, string? Folder = null)
{
    public static CliCreateResult Success(ServerSettings settings, string folder) => new(true, settings, null, folder);
    public static CliCreateResult Failed(string error) => new(false, null, error, null);
}
