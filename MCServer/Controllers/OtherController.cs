using System.IO;
using System.IO.Compression;
using System.Net.Http;
using ExtraFunctions.Extras;

namespace MCServer
{
    public enum SettingProp
    {
        ServerPath,
        BackupPath,
        ShutdownTime,
        ShutdownMode,
    }

    public static class OtherController
    {
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
        #endregion
        #region Logger
        internal static readonly ExLog Loger = new("ErrorLog.txt", Path.Combine(MainWindow.ServerPath, "LOGS"));

        public static void ThrowLog(string Error)
        {
            Loger.Log(DateTime.Now.ToString("[yyyy/MM/dd HH:mm:ss] ") + Error);
            Console.WriteLine(DateTime.Now.ToString("[yyyy/MM/dd HH:mm:ss:fff ERROR] ") + Error);
        }
        #endregion
    }
}