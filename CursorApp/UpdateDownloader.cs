using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CursorManager
{
    public static class UpdateDownloader
    {
        private const string UserAgent = "CursorManager-App";

        public static string GetCacheDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "CursorManager", "updates");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetCachedExePath(string versionTag)
        {
            string safe = SanitizeVersion(versionTag);
            return Path.Combine(GetCacheDirectory(), $"CursorManager_{safe}.exe");
        }

        public static bool HasCachedDownload(string versionTag)
        {
            string path = GetCachedExePath(versionTag);
            return File.Exists(path) && new FileInfo(path).Length > 1024;
        }

        public static async Task DownloadAsync(
            UpdateInfo update,
            IProgress<double>? progress,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(update.DownloadUrl))
                throw new InvalidOperationException("此版本沒有可自動下載的安裝檔，請改用手動下載。");

            string destPath = GetCachedExePath(update.LatestVersion);
            string tempPath = destPath + ".download";

            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

                using var response = await client.GetAsync(
                    update.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"下載失敗（HTTP {response.StatusCode}）");

                long totalBytes = response.Content.Headers.ContentLength ?? update.DownloadSize;
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true);

                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;

                    if (totalBytes > 0)
                        progress?.Report((double)downloaded / totalBytes);
                    else if (downloaded > 0)
                        progress?.Report(0);
                }

                fileStream.Close();

                if (File.Exists(destPath))
                    File.Delete(destPath);

                File.Move(tempPath, destPath);

                if (new FileInfo(destPath).Length < 1024)
                    throw new InvalidOperationException("下載的檔案大小異常，請稍後重試或改用手動下載。");

                progress?.Report(1);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException("下載更新時發生錯誤：" + ex.Message, ex);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }
            }
        }

        private static string SanitizeVersion(string versionTag)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                versionTag = versionTag.Replace(c, '_');
            return versionTag.Trim();
        }
    }
}
