namespace MCServer;

public class AppSettings
{
    public const string SettingName = nameof(AppSettings);

    public string BackupPath { get; set; } = "{ServerPath}/../WorldBackups";

    public List<ServerSettings> ServerSettings { get; set; } = [];
}