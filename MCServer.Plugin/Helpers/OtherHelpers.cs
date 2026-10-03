namespace MCServer.Plugins;

public static class OtherHelpers
{
    public static string FormatBackupPath(IGameServer server, string BackupPath, string WorldName)
    {
        return BackupPath
            .Replace("{RootPath}", server.Host.ServerFilesRoot)
            .Replace("{ServerPath}", server.ServerPath)
            .Replace("{ServerName}", server.Settings.Name)
            .Replace("{ServerID}", server.Settings.ID)
            .Replace("{WorldName}", WorldName);
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F2} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F2} MB",
        < 1024L * 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        _ => $"{bytes / (1024.0 * 1024 * 1024 * 1024):N2} TB",
    };
}