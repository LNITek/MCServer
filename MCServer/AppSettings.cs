using MCServer.Plugins;

namespace MCServer;

public class AppSettings
{
    public const string SettingName = nameof(AppSettings);

    public string BackupPath { get; set; } = "{ServerPath}/../WorldBackups";

    public List<ServerSettings> ServerSettings { get; set; } = [];

    /// <summary>
    /// Login accounts. List-based for future multi-user support;
    /// currently the app enforces a single Admin user.
    /// </summary>
    public List<UserAccount> Users { get; set; } = [];
}
