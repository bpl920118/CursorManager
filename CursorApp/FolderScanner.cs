using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CursorManager
{
    public static class FolderScanner
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".ani", ".cur"
        };

        public static List<CharacterThemeItem> ScanDirectory(string baseDir)
        {
            var results = new List<CharacterThemeItem>();
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
                return results;

            try
            {
                var dirList = new List<string> { baseDir };
                try
                {
                    dirList.AddRange(Directory.GetDirectories(baseDir, "*", SearchOption.AllDirectories));
                }
                catch { }

                var bag = new ConcurrentBag<CharacterThemeItem>();

                Parallel.ForEach(dirList, dir =>
                {
                    var item = TryCreateThemeItem(dir, isTemporary: false);
                    if (item != null)
                        bag.Add(item);
                });

                results = bag.OrderBy(r => r.Group).ThenBy(r => r.Name).ToList();
            }
            catch { }

            return results;
        }

        public static CharacterThemeItem? TryCreateThemeItem(string dir, bool isTemporary)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return null;

            try
            {
                var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
                var cursorFiles = files.Where(f => SupportedExtensions.Contains(Path.GetExtension(f))).ToList();
                if (cursorFiles.Count == 0)
                    return null;

                string dirName = Path.GetFileName(dir.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(dirName)) dirName = "自訂鼠標";
                string parentDirName = Path.GetFileName(Path.GetDirectoryName(dir.TrimEnd('\\', '/')) ?? "");

                string group = isTemporary ? "未存入庫" : ResolveGroup(dir, parentDirName);
                string cleanName = dirName.Replace("Mouse cursor", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (string.IsNullOrWhiteSpace(cleanName)) cleanName = dirName;

                string previewFile = cursorFiles.FirstOrDefault(f =>
                    f.Contains("01") || f.Contains("nomal", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("normal", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("arrow", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("default", StringComparison.OrdinalIgnoreCase)) ?? cursorFiles.First();

                return new CharacterThemeItem
                {
                    Name = cleanName,
                    Group = group,
                    FolderPath = dir,
                    FileCount = cursorFiles.Count,
                    PreviewFilePath = previewFile,
                    PreviewImage = CursorIconHelper.LoadCursorImage(previewFile, CursorIconHelper.SidebarPreviewSize),
                    IsTemporary = isTemporary
                };
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveGroup(string dir, string parentDirName)
        {
            if (dir.Contains("Hololive 0th", StringComparison.OrdinalIgnoreCase)) return "Hololive 0期生";
            if (dir.Contains("Hololive 1st", StringComparison.OrdinalIgnoreCase)) return "Hololive 1期生";
            if (dir.Contains("Hololive 2nd", StringComparison.OrdinalIgnoreCase)) return "Hololive 2期生";
            if (dir.Contains("Hololive 3rd", StringComparison.OrdinalIgnoreCase)) return "Hololive 3期生";
            if (dir.Contains("Hololive 4th", StringComparison.OrdinalIgnoreCase)) return "Hololive 4期生";
            if (dir.Contains("Hololive 5th", StringComparison.OrdinalIgnoreCase)) return "Hololive 5期生";
            if (dir.Contains("Hololive EN", StringComparison.OrdinalIgnoreCase)) return "Hololive EN";
            if (dir.Contains("Hololive Gamers", StringComparison.OrdinalIgnoreCase)) return "Hololive Gamers";
            if (dir.Contains("Hololive ID", StringComparison.OrdinalIgnoreCase)) return "Hololive ID";

            if (!string.IsNullOrEmpty(parentDirName) && !parentDirName.StartsWith("Hololive Mouse cursor", StringComparison.OrdinalIgnoreCase))
            {
                return parentDirName;
            }

            return "自訂鼠標";
        }
    }
}
