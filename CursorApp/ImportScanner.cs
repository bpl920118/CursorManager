using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CursorManager
{
    public sealed class ImportScanResult
    {
        public string FolderPath { get; init; } = string.Empty;
        public int AniCount { get; init; }
        public int CurCount { get; init; }
        public bool HasInstallInf { get; init; }
        public string? InstallInfPath { get; init; }
        public int MatchedSlots { get; init; }
        public int TotalSlots { get; init; } = 15;
        public int SkippedEntries { get; init; }

        public int TotalCursorFiles => AniCount + CurCount;
        public int MatchPercent => TotalSlots > 0 ? MatchedSlots * 100 / TotalSlots : 0;

        public IEnumerable<string> ToPreviewBulletPoints()
        {
            yield return $"游標檔：.ani ×{AniCount}、.cur ×{CurCount}（共 {TotalCursorFiles} 個）";

            if (HasInstallInf && !string.IsNullOrEmpty(InstallInfPath))
                yield return $"含 install.inf（{Path.GetFileName(InstallInfPath)}）— 將用於精確配對";
            else
                yield return "未找到 install.inf — 將以檔名關鍵字配對";

            yield return $"預估配對率：{MatchedSlots} / {TotalSlots}（{MatchPercent}%）";
        }
    }

    public static class ImportScanner
    {
        private static readonly HashSet<string> CursorExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".ani", ".cur"
        };

        public static ImportScanResult ScanFolder(string folderPath)
        {
            var result = new ImportScanResult { FolderPath = folderPath };

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return result;

            try
            {
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
                int ani = 0, cur = 0;

                foreach (var file in files)
                {
                    switch (Path.GetExtension(file).ToLowerInvariant())
                    {
                        case ".ani": ani++; break;
                        case ".cur": cur++; break;
                    }
                }

                string? installInf = files.FirstOrDefault(f =>
                    Path.GetFileName(f).Equals("install.inf", StringComparison.OrdinalIgnoreCase));

                var slots = CursorMatcher.MatchFolder(folderPath);
                int matched = slots.Count(s => s.HasFile);

                return new ImportScanResult
                {
                    FolderPath = folderPath,
                    AniCount = ani,
                    CurCount = cur,
                    HasInstallInf = installInf != null,
                    InstallInfPath = installInf,
                    MatchedSlots = matched,
                    TotalSlots = slots.Count
                };
            }
            catch
            {
                return result;
            }
        }

        public static string? FindThemeRootFolder(string rootDir)
        {
            if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
                return null;

            string? installInfDir = null;
            try
            {
                foreach (var inf in Directory.GetFiles(rootDir, "*.inf", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(inf).Equals("install.inf", StringComparison.OrdinalIgnoreCase))
                    {
                        installInfDir = Path.GetDirectoryName(inf);
                        if (!string.IsNullOrEmpty(installInfDir))
                            return installInfDir;
                    }
                }
            }
            catch { }

            int CountCursorFiles(string dir)
            {
                try
                {
                    return Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                        .Count(f => CursorExtensions.Contains(Path.GetExtension(f)));
                }
                catch
                {
                    return 0;
                }
            }

            int rootCount = CountCursorFiles(rootDir);
            if (rootCount > 0)
                return rootDir;

            string? best = null;
            int bestCount = 0;

            try
            {
                foreach (var dir in Directory.GetDirectories(rootDir, "*", SearchOption.AllDirectories))
                {
                    int count = CountCursorFiles(dir);
                    if (count > bestCount)
                    {
                        bestCount = count;
                        best = dir;
                    }
                }
            }
            catch { }

            return bestCount > 0 ? best : null;
        }

        public static (string? extractDir, int skippedEntries, string? error) ExtractZipSafely(string zipPath)
        {
            string extractDir = Path.Combine(Path.GetTempPath(), "CursorManager", $"import_{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(extractDir);
                string extractRoot = Path.GetFullPath(extractDir);
                if (!extractRoot.EndsWith(Path.DirectorySeparatorChar))
                    extractRoot += Path.DirectorySeparatorChar;

                int skipped = 0;

                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string destPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
                    if (!destPath.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    string? destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    entry.ExtractToFile(destPath, overwrite: true);
                }

                return (extractDir, skipped, null);
            }
            catch (Exception ex)
            {
                try
                {
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, recursive: true);
                }
                catch { }

                return (null, 0, ex.Message);
            }
        }
    }
}
