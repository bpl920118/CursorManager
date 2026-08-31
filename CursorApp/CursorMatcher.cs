using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HololiveCursorApp
{
    public static class CursorMatcher
    {
        private static readonly Dictionary<string, string[]> KeywordMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arrow"] = new[] { "arrow", "normal", "nomal", "default", "left_ptr", "pointer" },
            ["Help"] = new[] { "help", "question", "whats_this", "dnd-ask", "helpptr" },
            ["AppStarting"] = new[] { "background", "working", "appstarting", "progress", "half-busy", "left_ptr_watch", "work" },
            ["Wait"] = new[] { "busy", "wait", "loading", "watch", "sandglass" },
            ["Crosshair"] = new[] { "crosshair", "cross", "precision", "tcross" },
            ["IBeam"] = new[] { "ibeam", "text", "beam", "xterm" },
            ["NWPen"] = new[] { "pen", "handwriting", "pencil", "draft", "nwpen" },
            ["No"] = new[] { "unavailable", "unavailiable", "not allowed", "not-allowed", "not_allowed", "forbidden", "no-drop", "circle", "crossed_circle", "no" },
            ["SizeNS"] = new[] { "ns-resize", "vertical", "v-double-arrow", "sizens", "n-resize", "s-resize", "size_ver", "vert" },
            ["SizeWE"] = new[] { "ew-resize", "horizontal", "h-double-arrow", "sizewe", "e-resize", "w-resize", "size_hor", "horz" },
            ["SizeNWSE"] = new[] { "nwse-resize", "sizenwse", "nwse", "nw-resize", "se-resize", "size_fdiag", "fd_double_arrow", "dgn1", "fdiag", "diag1" },
            ["SizeNESW"] = new[] { "nesw-resize", "sizenesw", "nesw", "ne-resize", "sw-resize", "size_bdiag", "bd_double_arrow", "dgn2", "bdiag", "diag2" },
            ["SizeAll"] = new[] { "move", "sizeall", "size_all", "all-scroll", "fleur" },
            ["UpArrow"] = new[] { "uparrow", "alternate", "up_arrow", "center_ptr", "up" },
            ["Hand"] = new[] { "hand", "link", "pointing_hand", "hand2", "openhand", "grab" }
        };

        private static readonly Dictionary<string, string[]> InfTagMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arrow"] = new[] { "pointer", "arrow", "main", "normal", "cur_01" },
            ["Help"] = new[] { "help", "helpptr", "cur_02" },
            ["AppStarting"] = new[] { "work", "appstarting", "working", "cur_03" },
            ["Wait"] = new[] { "busy", "wait", "cur_04" },
            ["Crosshair"] = new[] { "cross", "crosshair", "precision", "cur_05" },
            ["IBeam"] = new[] { "beam", "ibeam", "text", "cur_06" },
            ["NWPen"] = new[] { "pen", "handwriting", "nwpen", "cur_07" },
            ["No"] = new[] { "unavailiable", "unavailable", "no", "notallowed", "cur_08" },
            ["SizeNS"] = new[] { "vert", "sizens", "ns-resize", "cur_09" },
            ["SizeWE"] = new[] { "horz", "sizewe", "ew-resize", "cur_10" },
            ["SizeNWSE"] = new[] { "dgn1", "sizenwse", "nwse-resize", "cur_11" },
            ["SizeNESW"] = new[] { "dgn2", "sizenesw", "nesw-resize", "cur_12" },
            ["SizeAll"] = new[] { "move", "sizeall", "cur_13" },
            ["UpArrow"] = new[] { "alternate", "uparrow", "up", "cur_14" },
            ["Hand"] = new[] { "link", "hand", "cur_15" }
        };

        private static readonly Regex TagRegex = new(@"\[(.*?)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NumberPrefixRegex = new(@"(?:_|\b)(0[1-9]|1[0-5])(?:\b|_|\[)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<CursorSlot> CreateDefaultSlots()
        {
            return new List<CursorSlot>
            {
                new CursorSlot { Order = 1,  KeyName = "Arrow",       DisplayName = "正常選擇",       EnglishName = "Normal Select" },
                new CursorSlot { Order = 2,  KeyName = "Help",        DisplayName = "說明選擇",       EnglishName = "Help Select" },
                new CursorSlot { Order = 3,  KeyName = "AppStarting", DisplayName = "在背景工作",     EnglishName = "Working in Background" },
                new CursorSlot { Order = 4,  KeyName = "Wait",        DisplayName = "忙碌",           EnglishName = "Busy" },
                new CursorSlot { Order = 5,  KeyName = "Crosshair",   DisplayName = "精確度選擇",     EnglishName = "Precision Select" },
                new CursorSlot { Order = 6,  KeyName = "IBeam",       DisplayName = "文字選擇",       EnglishName = "Text Select" },
                new CursorSlot { Order = 7,  KeyName = "NWPen",       DisplayName = "手寫",           EnglishName = "Handwriting" },
                new CursorSlot { Order = 8,  KeyName = "No",          DisplayName = "無法使用",       EnglishName = "Unavailable" },
                new CursorSlot { Order = 9,  KeyName = "SizeNS",      DisplayName = "垂直調整大小",   EnglishName = "Vertical Resize" },
                new CursorSlot { Order = 10, KeyName = "SizeWE",      DisplayName = "水平調整大小",   EnglishName = "Horizontal Resize" },
                new CursorSlot { Order = 11, KeyName = "SizeNWSE",    DisplayName = "對角調整 1",     EnglishName = "Diagonal Resize 1" },
                new CursorSlot { Order = 12, KeyName = "SizeNESW",    DisplayName = "對角調整 2",     EnglishName = "Diagonal Resize 2" },
                new CursorSlot { Order = 13, KeyName = "SizeAll",     DisplayName = "移動",           EnglishName = "Move" },
                new CursorSlot { Order = 14, KeyName = "UpArrow",     DisplayName = "替代選取",       EnglishName = "Alternate Select" },
                new CursorSlot { Order = 15, KeyName = "Hand",        DisplayName = "連結選取",       EnglishName = "Link Select" }
            };
        }

        public static List<CursorSlot> MatchFolder(string folderPath)
        {
            var slots = CreateDefaultSlots();
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return slots;

            // Search for all supported files
            var allFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".ani", StringComparison.OrdinalIgnoreCase) || 
                            f.EndsWith(".cur", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (allFiles.Count == 0)
                return slots;

            // Prefer .ani / .cur files if available
            var aniCurFiles = allFiles.Where(f => f.EndsWith(".ani", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cur", StringComparison.OrdinalIgnoreCase)).ToList();
            var files = aniCurFiles.Count > 0 ? aniCurFiles : allFiles;

            // 0. Match from .inf or scheme_map.ini if present
            string schemeMapFile = Path.Combine(folderPath, "scheme_map.ini");
            if (File.Exists(schemeMapFile))
            {
                MatchFromSchemeMapFile(schemeMapFile, slots, folderPath);
                if (slots.Any(s => !string.IsNullOrEmpty(s.FilePath)))
                {
                    ApplySmartFallbacks(slots, files);
                    LoadSlotPreviews(slots);
                    return slots;
                }
            }

            var infFiles = Directory.GetFiles(folderPath, "*.inf", SearchOption.AllDirectories);
            if (infFiles.Length > 0)
            {
                MatchFromInfFile(infFiles[0], slots, folderPath);
                if (slots.Any(s => !string.IsNullOrEmpty(s.FilePath)))
                {
                    ApplySmartFallbacks(slots, files);
                    LoadSlotPreviews(slots);
                    return slots;
                }
            }

            var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Pass 1: Square bracket tag matching [tag]
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                var match = TagRegex.Match(fileName);
                if (match.Success)
                {
                    string tag = match.Groups[1].Value.Trim().ToLowerInvariant();
                    var slot = FindSlotByKeyword(slots, tag);
                    if (slot != null && string.IsNullOrEmpty(slot.FilePath))
                    {
                        slot.FilePath = file;
                        matchedFiles.Add(file);
                    }
                }
            }

            // 2. Pass 2: Order prefix number matching e.g. 01, _02, etc.
            foreach (var file in files)
            {
                if (matchedFiles.Contains(file)) continue;

                string fileName = Path.GetFileName(file);
                var numMatch = NumberPrefixRegex.Match(fileName);
                if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int index))
                {
                    var slot = slots.FirstOrDefault(s => s.Order == index);
                    if (slot != null && string.IsNullOrEmpty(slot.FilePath))
                    {
                        slot.FilePath = file;
                        matchedFiles.Add(file);
                    }
                }
            }

            // 3. Pass 3: General keyword in filename matching
            foreach (var file in files)
            {
                if (matchedFiles.Contains(file)) continue;

                string lower = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                foreach (var slot in slots.Where(s => string.IsNullOrEmpty(s.FilePath)))
                {
                    if (IsMatchByKeyword(slot.KeyName, lower))
                    {
                        slot.FilePath = file;
                        matchedFiles.Add(file);
                        break;
                    }
                }
            }

            // 4. Apply smart fallbacks for missing slots
            ApplySmartFallbacks(slots, files);

            // 5. Load previews
            LoadSlotPreviews(slots);
            return slots;
        }

        private static void ApplySmartFallbacks(List<CursorSlot> slots, List<string> files)
        {
            var nsSlot = slots.FirstOrDefault(s => s.KeyName == "SizeNS");
            var weSlot = slots.FirstOrDefault(s => s.KeyName == "SizeWE");
            var nwseSlot = slots.FirstOrDefault(s => s.KeyName == "SizeNWSE");
            var neswSlot = slots.FirstOrDefault(s => s.KeyName == "SizeNESW");
            var arrowSlot = slots.FirstOrDefault(s => s.KeyName == "Arrow");
            var handSlot = slots.FirstOrDefault(s => s.KeyName == "Hand");
            var waitSlot = slots.FirstOrDefault(s => s.KeyName == "Wait");
            var bgSlot = slots.FirstOrDefault(s => s.KeyName == "AppStarting");
            var noSlot = slots.FirstOrDefault(s => s.KeyName == "No");

            // Diagonal Resize fallback
            if (nwseSlot != null && (string.IsNullOrEmpty(nwseSlot.FilePath) || nwseSlot.FilePath.EndsWith(".cur", StringComparison.OrdinalIgnoreCase)))
            {
                var aniDiag1 = files.FirstOrDefault(f => f.EndsWith(".ani", StringComparison.OrdinalIgnoreCase) && 
                    (f.Contains("nwse", StringComparison.OrdinalIgnoreCase) || f.Contains("dgn1", StringComparison.OrdinalIgnoreCase) || f.Contains("fdiag", StringComparison.OrdinalIgnoreCase) || f.Contains("11")));
                if (aniDiag1 != null)
                {
                    nwseSlot.FilePath = aniDiag1;
                }
                else if (nsSlot != null && !string.IsNullOrEmpty(nsSlot.FilePath) && nsSlot.FilePath.EndsWith(".ani", StringComparison.OrdinalIgnoreCase))
                {
                    nwseSlot.FilePath = nsSlot.FilePath;
                }
            }

            if (neswSlot != null && (string.IsNullOrEmpty(neswSlot.FilePath) || neswSlot.FilePath.EndsWith(".cur", StringComparison.OrdinalIgnoreCase)))
            {
                var aniDiag2 = files.FirstOrDefault(f => f.EndsWith(".ani", StringComparison.OrdinalIgnoreCase) && 
                    (f.Contains("nesw", StringComparison.OrdinalIgnoreCase) || f.Contains("dgn2", StringComparison.OrdinalIgnoreCase) || f.Contains("bdiag", StringComparison.OrdinalIgnoreCase) || f.Contains("12")));
                if (aniDiag2 != null)
                {
                    neswSlot.FilePath = aniDiag2;
                }
                else if (weSlot != null && !string.IsNullOrEmpty(weSlot.FilePath) && weSlot.FilePath.EndsWith(".ani", StringComparison.OrdinalIgnoreCase))
                {
                    neswSlot.FilePath = weSlot.FilePath;
                }
                else if (nsSlot != null && !string.IsNullOrEmpty(nsSlot.FilePath) && nsSlot.FilePath.EndsWith(".ani", StringComparison.OrdinalIgnoreCase))
                {
                    neswSlot.FilePath = nsSlot.FilePath;
                }
            }

            // Hand fallback
            if (handSlot != null && string.IsNullOrEmpty(handSlot.FilePath) && arrowSlot != null && !string.IsNullOrEmpty(arrowSlot.FilePath))
            {
                handSlot.FilePath = arrowSlot.FilePath;
            }

            // Wait / Background fallback
            if (waitSlot != null && string.IsNullOrEmpty(waitSlot.FilePath) && bgSlot != null && !string.IsNullOrEmpty(bgSlot.FilePath))
            {
                waitSlot.FilePath = bgSlot.FilePath;
            }
            else if (bgSlot != null && string.IsNullOrEmpty(bgSlot.FilePath) && waitSlot != null && !string.IsNullOrEmpty(waitSlot.FilePath))
            {
                bgSlot.FilePath = waitSlot.FilePath;
            }

            // Unavailable fallback
            if (noSlot != null && string.IsNullOrEmpty(noSlot.FilePath))
            {
                var busy = waitSlot?.FilePath ?? bgSlot?.FilePath ?? arrowSlot?.FilePath;
                if (!string.IsNullOrEmpty(busy)) noSlot.FilePath = busy;
            }
        }

        private static void LoadSlotPreviews(List<CursorSlot> slots)
        {
            foreach (var slot in slots)
            {
                if (!string.IsNullOrEmpty(slot.FilePath))
                {
                    slot.PreviewImage = CursorIconHelper.LoadCursorImage(slot.FilePath);
                    if (slot.FilePath.EndsWith(".ani", StringComparison.OrdinalIgnoreCase))
                    {
                        slot.AniSequence = CursorIconHelper.LoadAniSequence(slot.FilePath);
                        if (slot.AniSequence != null && slot.AniSequence.Frames.Count > 0)
                        {
                            slot.PreviewImage = slot.AniSequence.Frames[0];
                        }
                    }
                }
            }
        }

        private static void MatchFromInfFile(string infPath, List<CursorSlot> slots, string baseFolder)
        {
            try
            {
                var lines = File.ReadAllLines(infPath);
                string infDir = Path.GetDirectoryName(infPath) ?? baseFolder;
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith(";") || string.IsNullOrEmpty(line)) continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim().Trim('%', ' ');
                        string val = parts[1].Trim().Trim('"', ' ', '%');
                        dict[key] = val;
                    }
                }

                var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in dict)
                {
                    string v = kvp.Value;
                    if (dict.TryGetValue(v, out string? realVal))
                    {
                        v = realVal;
                    }
                    resolved[kvp.Key] = v;
                }

                foreach (var slot in slots)
                {
                    if (!InfTagMap.TryGetValue(slot.KeyName, out var keys)) continue;

                    foreach (var k in keys)
                    {
                        if (resolved.TryGetValue(k, out var fileName) || dict.TryGetValue(k, out fileName))
                        {
                            string target = Path.Combine(infDir, fileName);
                            if (File.Exists(target))
                            {
                                slot.FilePath = target;
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void MatchFromSchemeMapFile(string iniPath, List<CursorSlot> slots, string baseFolder)
        {
            try
            {
                var lines = File.ReadAllLines(iniPath);
                string iniDir = Path.GetDirectoryName(iniPath) ?? baseFolder;
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.StartsWith(";") || line.StartsWith("#") || string.IsNullOrEmpty(line)) continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim();
                        string val = parts[1].Trim().Trim('"', ' ');
                        var slot = slots.FirstOrDefault(s => s.KeyName.Equals(key, StringComparison.OrdinalIgnoreCase));
                        if (slot != null)
                        {
                            string target = Path.IsPathRooted(val) ? val : Path.Combine(iniDir, val);
                            if (File.Exists(target))
                            {
                                slot.FilePath = target;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static CursorSlot? FindSlotByKeyword(List<CursorSlot> slots, string text)
        {
            foreach (var kvp in KeywordMap)
            {
                if (kvp.Value.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return slots.FirstOrDefault(s => s.KeyName == kvp.Key);
                }
            }
            return null;
        }

        private static bool IsMatchByKeyword(string keyName, string fileName)
        {
            if (KeywordMap.TryGetValue(keyName, out var keywords))
            {
                return keywords.Any(k => fileName.Contains(k, StringComparison.OrdinalIgnoreCase) || fileName.Equals(k, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }
    }
}
