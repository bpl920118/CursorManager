using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace HololiveCursorApp
{
    public class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
    }

    public static class UpdateChecker
    {
        private const string RepoOwner = "bpl920118";
        private const string RepoName = "CursorManager";
        private const string ApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        public const string ReleaseWebUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

        public static async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            var info = new UpdateInfo();
            try
            {
                var curVer = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(2, 5, 0);
                info.CurrentVersion = $"v{curVer.Major}.{curVer.Minor}.{curVer.Build}";

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.Add("User-Agent", "CursorManager-App");

                var resp = await client.GetAsync(ApiUrl);
                if (!resp.IsSuccessStatusCode)
                {
                    return info;
                }

                var jsonStr = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
                string body = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";
                string htmlUrl = root.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? "" : ReleaseWebUrl;

                info.LatestVersion = tagName;
                info.ReleaseUrl = string.IsNullOrEmpty(htmlUrl) ? ReleaseWebUrl : htmlUrl;
                info.ReleaseNotes = body;

                var cleanTag = Regex.Replace(tagName, @"[^\d\.]", "");
                if (Version.TryParse(cleanTag, out var remoteVer))
                {
                    // If remote version is greater than current version
                    if (remoteVer > curVer)
                    {
                        info.HasUpdate = true;
                    }
                }
            }
            catch
            {
                // Ignore network failure
            }

            return info;
        }

        public static void OpenReleasePage(string? url = null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = string.IsNullOrEmpty(url) ? ReleaseWebUrl : url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("無法開啟瀏覽器：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
