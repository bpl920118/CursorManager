using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace CursorManager
{
    public static class UpdateInstaller
    {
        public static bool CanInstallToCurrentLocation()
        {
            string? targetExe = GetCurrentExePath();
            if (string.IsNullOrEmpty(targetExe))
                return false;

            string? dir = Path.GetDirectoryName(targetExe);
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) && IsDirectoryWritable(dir);
        }

        public static void InstallAndRestart(string downloadedExePath)
        {
            if (!File.Exists(downloadedExePath))
                throw new FileNotFoundException("找不到已下載的更新檔案。", downloadedExePath);

            string? targetExe = GetCurrentExePath();
            if (string.IsNullOrEmpty(targetExe))
                throw new InvalidOperationException("無法判斷目前程式路徑。");

            string targetDir = Path.GetDirectoryName(targetExe) ?? AppPaths.InstallDirectory;
            if (!IsDirectoryWritable(targetDir))
                throw new InvalidOperationException(
                    "目前程式所在資料夾無法寫入，請將 CursorManager.exe 移到可寫入的位置，或改用手動下載覆蓋。");

            string scriptPath = Path.Combine(UpdateDownloader.GetCacheDirectory(), "apply_update.bat");
            string script = BuildBatchScript(downloadedExePath, targetExe);
            File.WriteAllText(scriptPath, script, Encoding.UTF8);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);
            Application.Current.Shutdown();
        }

        private static string BuildBatchScript(string sourceExe, string targetExe)
        {
            string pid = Process.GetCurrentProcess().Id.ToString();
            return
                "@echo off\r\n" +
                "chcp 65001 >nul\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"taskkill /f /pid {pid} >nul 2>&1\r\n" +
                "taskkill /f /im CursorManager.exe >nul 2>&1\r\n" +
                "taskkill /f /im CursorTool.exe >nul 2>&1\r\n" +
                $"move /y \"{sourceExe}\" \"{targetExe}\" >nul\r\n" +
                $"start \"\" \"{targetExe}\"\r\n" +
                "del \"%~f0\"\r\n";
        }

        private static string? GetCurrentExePath()
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return path;

            try
            {
                path = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch { }

            string fallback = Path.Combine(AppPaths.InstallDirectory, "CursorManager.exe");
            return File.Exists(fallback) ? fallback : null;
        }

        private static bool IsDirectoryWritable(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return false;

            try
            {
                string probe = Path.Combine(directory, $".update_probe_{Guid.NewGuid():N}");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
