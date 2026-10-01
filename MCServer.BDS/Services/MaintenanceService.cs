using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ExtraFunctions.Extras;
using MCServer.BDS.Models;
using MCServer.Plugins;
using MudBlazor;

namespace MCServer.BDS.Services;

public static class MaintenanceService
{
    #region Backup World
    public static void BackupServer(this BedrockServer server, string? WorldName = null)
    {
        var ActiveWorld = WorldName = server.GetProperties().FirstOrDefault(x => x.Name == "level-name")?.Value;

        if (server.ServerRunning && WorldName == ActiveWorld)
            BedrockProcessService.CreateThread(async () =>
            {
                await BackupRunningTask(server);
            });
        else
            BedrockProcessService.CreateThread(async () =>
            {
                if (string.IsNullOrWhiteSpace(WorldName))
                    WorldName = ActiveWorld;
                await BackupStoppedTask(server, WorldName);
            });
    }

    internal static async Task BackupStoppedTask(BedrockServer server, string? WorldName)
    {
        server.CommandQue.WaitOne();
        string sDate = " " + DateTime.Now.ToString("yyyy-MM-dd"), Extension = "";
        int I = 0;
        DateTime Timer = DateTime.Now;

        if (string.IsNullOrWhiteSpace(WorldName)) return;

        server.CommandRunning = true;
        server.WriteDisplayLine("Starting Backup...");
        await Task.Delay(1000);

        var WorldPath = Path.Combine(server.ServerPath, "worlds", WorldName);
        var BackupPath = string.IsNullOrWhiteSpace(server.Host.BackupPath) ?
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "/MCBackups" :
            server.Host.BackupPath;
        BackupPath = FormatBackupPath(server, BackupPath, WorldName);
        var SourcePath = Path.Combine(server.ServerPath, "..", "Backups", WorldName);

        if (Directory.Exists(SourcePath)) Directory.Delete(SourcePath, true);
        Directory.CreateDirectory(SourcePath);
        if (!Directory.Exists(BackupPath)) Directory.CreateDirectory(BackupPath);

        var FilePath = Path.GetDirectoryName(SourcePath);
        if (!Directory.Exists(FilePath))
            Directory.CreateDirectory(FilePath);
        ExFun.CopyDir(WorldPath, SourcePath, true);

        BackupPath += "/" + WorldName + sDate;
        while (File.Exists(BackupPath + Extension + ".tar.gz"))
            Extension = $" ({I++})";

        var ArchivePath = BackupPath + Extension + ".tar.gz";

        using FileStream fs = new(ArchivePath, FileMode.CreateNew, FileAccess.Write);
        using GZipStream gz = new(fs, CompressionMode.Compress, leaveOpen: true);

        await TarFile.CreateFromDirectoryAsync(SourcePath, gz, false);

        await Task.Delay(1000);
        var Time = (DateTime.Now - Timer);
        server.WriteDisplayLine($"Backup Completed In ({Time})");
        server.CommandRunning = false;
        server.CommandQue.Release();
    }

    internal static async Task BackupRunningTask(BedrockServer server)
    {
        server.CommandQue.WaitOne();
        bool Saved = false;
        List<string> Files = new();
        string sDate = " " + DateTime.Now.ToString("yyyy-MM-dd"), WorldName = "", Extension = "";
        int I = 0;
        DateTime Timer = DateTime.Now;

        server.CommandRunning = true;
        server.WriteDisplayLine("Starting Backup...");
        server.WriteLine("say §9Starting World Backup...");
        server.WriteLine("save hold");
        await Task.Delay(1000);
        do
        {
            server.WriteLine("save query");
            await Task.Delay(2000);
            var Index = server.OutputList.FindLastIndex(x => x.Line.Contains("Data saved. Files are now ready to be copied.")) + 1;
            if (Index <= 0) continue;
            Files = server.OutputList[Index].Line.Split(", ").Select(x => x.Split(':').First()).ToList();
            if (Files.Count > 0) Saved = true;
        } while (!Saved);
        WorldName = string.Join("", Files.First().TakeWhile(x => x != '/'));
        var WorldPath = Path.Combine(server.ServerPath, "worlds", WorldName);
        var BackupPath = string.IsNullOrWhiteSpace(server.Host.BackupPath) ?
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "/MCBackups" :
            server.Host.BackupPath;
        BackupPath = FormatBackupPath(server, BackupPath, WorldName);
        var SourcePath = Path.Combine(server.ServerPath, "..", "Backups", WorldName);
        Files = Files.Select(x => x.Replace('\\', '/').Replace(WorldName, "")).ToList();

        if (Directory.Exists(SourcePath)) Directory.Delete(SourcePath, true);
        Directory.CreateDirectory(SourcePath);
        if (!Directory.Exists(BackupPath)) Directory.CreateDirectory(BackupPath);

        foreach (var file in Files)
        {
            var FilePath = Path.GetDirectoryName(SourcePath + file);
            if (file.Contains('/') && !Directory.Exists(FilePath))
                Directory.CreateDirectory(FilePath);
            File.Copy(WorldPath + file, SourcePath + file);
        }

        server.WriteLine("save resume");

        BackupPath += "/" + WorldName + sDate;
        while (File.Exists(BackupPath + Extension + ".tar.gz"))
            Extension = $" ({I++})";

        var ArchivePath = BackupPath + Extension + ".tar.gz";

        using FileStream fs = new(ArchivePath, FileMode.CreateNew, FileAccess.Write);
        using GZipStream gz = new(fs, CompressionMode.Compress, leaveOpen: true);

        await TarFile.CreateFromDirectoryAsync(SourcePath, gz, false);

        await Task.Delay(1000);
        var Time = (DateTime.Now - Timer);
        server.WriteLine($"say §9Backup Completed In ({Time})");
        server.WriteDisplayLine($"Backup Completed In ({Time})");
        server.CommandRunning = false;
        server.CommandQue.Release();
    }

    public static string FormatBackupPath(BedrockServer server, string BackupPath, string WorldName)
    {
        return BackupPath
            .Replace("{ServerPath}", server.ServerPath)
            .Replace("{ServerName}", server.Settings.Name)
            .Replace("{ServerID}", server.Settings.ID)
            .Replace("{WorldName}", WorldName);
    }
    #endregion

    #region Trim World
    /// <summary>
    /// Trims chunks outside the keep-area for one dimension. Offline-only:
    /// refuses while the server runs and takes a backup first (unless dry-run
    /// or backup disabled). Only chunk keys are deleted; global keys are kept.
    /// </summary>
    public static void TrimServer(this BedrockServer server, TrimOptions options)
    {
        BedrockProcessService.CreateThread(async () =>
        {
            if (!options.DryRun && options.CreateBackup)
                await BackupStoppedTask(server, options.WorldName);
            await TrimTask(server, options);
        });
    }

    internal static async Task TrimTask(BedrockServer server, TrimOptions options)
    {
        server.CommandQue.WaitOne();
        var Timer = DateTime.Now;
        try
        {
            server.CommandRunning = true;
            var activeWorld = server.GetProperties().FirstOrDefault(x => x.Name == "level-name")?.Value;
            if (server.ServerRunning && string.Equals(activeWorld, options.WorldName, StringComparison.OrdinalIgnoreCase))
            {
                server.Host.NotifyUser($"Trim: stop the server before trimming the active world '{options.WorldName}'.", Severity.Error);
                return;
            }

            var action = options.DryRun ? "Scanning" : "Trimming";
            server.Progress.Show($"{action} '{options.WorldName}' [{options.Dimension}] keep {options.Describe()}...", 0);
            server.WriteDisplayLine($"{action} '{options.WorldName}' [{options.Dimension}] keep {options.Describe()}...");
            var progress = new Progress<double>(p => server.Progress.Value = p);
            var result = options.DryRun
                ? await server.ScanAsync(options, progress)
                : await server.TrimAsync(options, progress);

            var Time = DateTime.Now - Timer;
            server.WriteDisplayLine(
                $"{action} {(options.DryRun ? "scan" : "complete")} in ({Time}): " +
                $"scanned {result.ScannedKeys} keys, {result.ChunkKeysInDimension} in {result.Dimension}; " +
                $"{(options.DryRun ? "would delete" : "deleted")} {result.DeletedKeys} keys " +
                $"({result.DistinctChunksDeleted} chunks), kept {result.DistinctChunksKept} chunks.");
        }
        catch (Exception ex)
        {
            server.WriteDisplayLine($"Trim failed: {ex.Message}", ConsoleLineType.Error);
            server.Host.NotifyUser($"Trim failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            server.Progress.Hide();
            server.CommandRunning = false;
            server.CommandQue.Release();
        }
    }
    #endregion

    #region Update Server
    public static void UpdateServer(this BedrockServer server)
    {
        BedrockProcessService.CreateThread(async () => await UpdateTask(server));
    }

    internal static async Task UpdateTask(BedrockServer server)
    {
        string[] FilesToBackup = ["server.properties", "allowlist.json", "permissions.json"];
        string[] FoldersToTrim = ["behavior_packs", "config", "data", "definitions", "resource_packs"];
        server.CommandQue.WaitOne();

        server.CommandRunning = true;

        server.Progress.Show("Checking Update Version...", true);
        server.WriteDisplayLine("Checking Update Version...");
        var restartServer = false;
        using var httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };
        var api = await httpClient.GetStringAsync("https://net-secondary.web.minecraft-services.net/api/v1.0/download/links");
        var links = JsonSerializer.Deserialize<JsonElement>(api)!.GetProperty("result").GetProperty("links")
            .EnumerateArray().ToDictionary(x =>
                x.GetProperty("downloadType").GetString() ?? "", x => x.GetProperty("downloadUrl").GetString());

        string type = "serverBedrock";
        if (server.Settings.Preview) type += "Preview";
        if (server.Host.IsWindows) type += "Windows";
        else type += "Linux";

        var URL = links.GetValueOrDefault(type);
        if (string.IsNullOrWhiteSpace(URL))
        {
            server.WriteDisplayLine("Update Error (URL): " + URL, ConsoleLineType.Error);
            End();
            return;
        }

        var FileName = URL.Split("bedrock-server-").LastOrDefault()?.Replace(".zip", "") ?? "0.0.0.0";
        server.WriteDisplayLine($"Update versions: New {FileName} | Old {server.Settings.Version}");
        if (Version.TryParse(FileName, out Version NewMK) && Version.TryParse(server.Settings.Version, out Version ExistingMK))
        {
            if (NewMK <= ExistingMK)
            {
                server.WriteDisplayLine($"Update current version is newer", ConsoleLineType.Warning);
                End();
                return;
            }
        }
        else
        {
            server.WriteDisplayLine($"Update invalid version", ConsoleLineType.Error);
            End();
            return;
        }

        if (server.ServerRunning)
        {
            if (!(await server.Stop("Updating Server", TimeSpan.FromSeconds(10))))
            {
                server.Host.NotifyUser("Update: Server is running! Stop The Server To Update.", Severity.Error);
                End();
                return;
            }

            restartServer = true;
        }

        server.CommandRunning = true;

        server.Progress.Show("Backing up config files", true);
        var BackupFolder = Path.Combine(server.ServerPath, "Backup");
        Directory.CreateDirectory(BackupFolder);

        foreach (var Item in FilesToBackup)
        {
            server.WriteDisplayLine("BACKING UP: " + Item);
            var path = Path.Combine(server.ServerPath, Item);
            if (File.Exists(path)) File.Copy(path, Path.Combine(BackupFolder, Item), true);
        }

        server.Progress.Show("DOWNLOADING: " + FileName, 0);
        server.WriteDisplayLine("DOWNLOADING: " + FileName);

        //server.WriteDisplayLine($"Progress: [{GenerateProgress(0)}] 0%");
        using var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(10), };
        using var file = await client.DownloadAsync(URL, new Progress<double>(I =>
        {
            server.Progress.Value = I;
        }));

        server.Progress.Show("UPDATING SERVER FILES...", true);
        server.WriteDisplayLine("UPDATING SERVER FILES...");

        var UpdatePath = Path.Combine(server.ServerPath, "Downloads", type, FileName);
        if (Directory.Exists(UpdatePath)) Directory.Delete(UpdatePath, true);
        else Directory.CreateDirectory(UpdatePath);

        //server.WriteDisplayLine($"Progress: [{GenerateProgress(0)}] 0%");
        foreach (var Folder in FoldersToTrim)
        {
            var TrimPath = Path.Combine(server.ServerPath, Folder);
            if (Directory.Exists(TrimPath))
            {
                server.WriteDisplayLine("TRIMMING: " + Folder);
                Directory.Delete(TrimPath, true);
            }
        }

        //server.WriteDisplayLine($"Progress: [{GenerateProgress(0)}] 0%");
        server.Progress.Loading = false;
        var ZIP = new ZipArchive(file, ZipArchiveMode.Read);
        await ZIP.ExtractAsync(UpdatePath, new Progress<double>(I =>
        {
            server.Progress.Value = I;
        }));

        server.Progress.Loading = true;
        foreach (var Item in FilesToBackup)
        {
            server.WriteDisplayLine("RESTORING: " + Item);
            var path = Path.Combine(server.ServerPath, Item);
            if (File.Exists(Path.Combine(BackupFolder, Item)))
                File.Copy(Path.Combine(BackupFolder, Item), path, true);
        }

        server.Progress.Show("UPDATING SERVER PROPERTIES...", true);
        server.WriteDisplayLine("UPDATING SERVER PROPERTIES...");
        using var Reader = new StreamReader(ZIP.Entries.First(x => x.Name == "server.properties").Open());
        var NewProps = BedrockPropertiesService.ReadProperties(Reader);
        var Props = server.GetProperties();

        foreach (var property in NewProps)
        {
            if (Props.Find(x => x.Name == property.Name) is Property prop)
            {
                if (prop.Mode == PropertyEditMode.Custom)
                {
                    prop.Name += "-custom";
                    Props.Add(property);
                    continue;
                }

                if (prop.Mode == PropertyEditMode.None)
                    prop.Value = property.Value;
                prop.Comments = property.Comments;
            }

            Props.Add(property);
        }

        Props.RemoveAll(el => el.Mode == PropertyEditMode.None && NewProps.Find(x => x.Name == el.Name) == null);
        server.SetProperties(Props);

        server.Settings.Version = FileName;
        server.Host.SaveSettings();
        server.WriteDisplayLine("Updated!");
        End();

        void End()
        {
            server.CommandRunning = false;
            server.Progress.Hide();
            
            server.CommandQue.Release();
            if (restartServer)
                server.StartServer();
        }
    }

    #region Progress
    public static async Task<Stream> DownloadAsync(this HttpClient client, string requestUri, IProgress<double> progress = null, CancellationToken cancellationToken = default)
    {
        var destinationStream = new MemoryStream();
        using var response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength;
        using var download = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[4096];
        var totalBytesRead = 0L;
        var readBytes = 0;
        while ((readBytes = await download.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destinationStream.WriteAsync(buffer.AsMemory(0, readBytes), cancellationToken);
            totalBytesRead += readBytes;
            if (contentLength.HasValue && progress != null)
            {
                var progressPercentage = (double)Math.Round((decimal)totalBytesRead * 100 / contentLength.Value * 100, 2) / 100;
                progress.Report(progressPercentage);
            }
        }
        return destinationStream;
    }

    public static async Task ExtractAsync(this ZipArchive archive, string extractPath, IProgress<double> progress)
    {
        await Task.Run(() =>
        {
            int extractedEntries = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string entryExtractPath = Path.Combine(extractPath, entry.FullName.Replace("/", "\\"));
                if (!Directory.Exists(Path.GetDirectoryName(entryExtractPath)))
                    Directory.CreateDirectory(Path.GetDirectoryName(entryExtractPath));
                if (!string.IsNullOrWhiteSpace(Path.GetFileName(entryExtractPath)))
                    entry.ExtractToFile(entryExtractPath, true);
                extractedEntries++;

                var progressPercentage = (double)Math.Round((decimal)extractedEntries * 100 / archive.Entries.Count * 100, 2) / 100;
                progress?.Report(progressPercentage);
            }
        });
    }

    internal static string GenerateProgress(double Progress)
    {
        double CharLengh = 10, i = 0;
        var res = new StringBuilder("");
        var ProgressLengh = Progress / CharLengh;

        for (i = 0; i < ProgressLengh; i++)
            res.Append("#");
        if (ProgressLengh % 1 > 0)
        {
            ProgressLengh = Math.Ceiling(ProgressLengh);
            res.Append("-");
        }
        for (i = ProgressLengh; i < CharLengh; i++)
            res.Append(" ");

        return res.ToString();
    }
    #endregion
    #endregion
}
