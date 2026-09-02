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
                    var item = TryCreateThemeItem(dir, isTemporary: false, libraryRoot: baseDir);
                    if (item != null)
                        bag.Add(item);
                });

                results = bag.OrderBy(r => r.Group).ThenBy(r => r.Name).ToList();
            }
            catch { }

            return results;
        }

        public static CharacterThemeItem? TryCreateThemeItem(string dir, bool isTemporary, string? libraryRoot = null)
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

                string group = isTemporary ? "未存入庫" : ResolveGroup(dir, libraryRoot);

                string previewFile = cursorFiles.FirstOrDefault(f =>
                    f.Contains("01") || f.Contains("nomal", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("normal", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("arrow", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("default", StringComparison.OrdinalIgnoreCase)) ?? cursorFiles.First();

                return new CharacterThemeItem
                {
                    Name = dirName,
                    Group = group,
                    FolderPath = dir,
                    FileCount = CursorMatcher.CountMatchedSlots(dir),
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

        private static string ResolveGroup(string dir, string? libraryRoot)
        {
            string? parentPath = Path.GetDirectoryName(dir.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(parentPath))
                return ThemeGroupNames.Ungrouped;

            if (!string.IsNullOrEmpty(libraryRoot) &&
                parentPath.Equals(libraryRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return ThemeGroupNames.Ungrouped;

            string parentName = Path.GetFileName(parentPath);
            return string.IsNullOrEmpty(parentName) ? ThemeGroupNames.Ungrouped : parentName;
        }
    }
}
