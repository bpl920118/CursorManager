using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CursorManager
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
            // Do not use bare "hand" here — it false-matches "Handwriting" (NWPen slot).
            ["Hand"] = new[] { "link", "pointing_hand", "hand2", "openhand", "grab" },
            ["Person"] = new[] { "person", "personselect", "person_select", "selectperson" },
            ["Pin"] = new[] { "pin", "location", "locationselect", "location_select", "selectlocation", "aero_pin" }
        };

        // Standard Windows animated cursor pack basenames (see install.inf [Strings])
        private static readonly Dictionary<string, string> ExactBasenameToSlotKey = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Normal"] = "Arrow",
            ["Help"] = "Help",
            ["Working"] = "AppStarting",
            ["Busy"] = "Wait",
            ["Precision"] = "Crosshair",
            ["Text"] = "IBeam",
            ["Handwriting"] = "NWPen",
            ["Unavailable"] = "No",
            ["Vertical"] = "SizeNS",
            ["Horizontal"] = "SizeWE",
            ["Diagonal1"] = "SizeNWSE",
            ["Diagonal2"] = "SizeNESW",
            ["Move"] = "SizeAll",
            ["Alternate"] = "UpArrow",
            ["Link"] = "Hand",
            ["Person"] = "Person",
            ["Pin"] = "Pin"
        };

        private static readonly Dictionary<string, string[]> InfTagMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arrow"] = new[] { "pointer", "arrow", "main", "normal", "cur_01" },
            ["Help"] = new[] { "help", "helpptr", "cur_02" },
            ["AppStarting"] = new[] { "work", "appstarting", "working", "cur_03" },
            ["Wait"] = new[] { "busy", "wait", "cur_04" },
            ["Crosshair"] = new[] { "cross", "crosshair", "precision", "cur_05" },
            ["IBeam"] = new[] { "beam", "ibeam", "text", "cur_06" },
            // Windows cursor INF convention: %hand% = handwriting (NWPen), %link% = link select (Hand)
            ["NWPen"] = new[] { "pen", "handwriting", "nwpen", "hand", "cur_07" },
            ["No"] = new[] { "unavailiable", "unavailable", "no", "notallowed", "cur_08" },
            ["SizeNS"] = new[] { "vert", "sizens", "ns-resize", "cur_09" },
            ["SizeWE"] = new[] { "horz", "sizewe", "ew-resize", "cur_10" },
            ["SizeNWSE"] = new[] { "dgn1", "sizenwse", "nwse-resize", "cur_11" },
            ["SizeNESW"] = new[] { "dgn2", "sizenesw", "nesw-resize", "cur_12" },
            ["SizeAll"] = new[] { "move", "sizeall", "cur_13" },
            ["UpArrow"] = new[] { "alternate", "uparrow", "up", "cur_14" },
            ["Hand"] = new[] { "link", "cur_15" },
            ["Person"] = new[] { "person", "cur_16" },
            ["Pin"] = new[] { "pin", "location", "cur_17" }
        };

        private static readonly Regex TagRegex = new(@"\[(.*?)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NumberPrefixRegex = new(@"(?:_|\b)(0[1-9]|1[0-7])(?:\b|_|\[)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                new CursorSlot { Order = 15, KeyName = "Hand",        DisplayName = "連結選取",       EnglishName = "Link Select" },
                new CursorSlot { Order = 16, KeyName = "Person",      DisplayName = "選取人員",       EnglishName = "Person Select" },
                new CursorSlot { Order = 17, KeyName = "Pin",         DisplayName = "選取位置",       EnglishName = "Location Select" }
            };
        }

        public static List<CursorSlot> MatchFolder(string folderPath, bool loadAniSequences = true)
        {
            var slots = MatchFolderSlots(folderPath);
            LoadSlotStaticPreviews(slots);
            if (loadAniSequences)
            {
                LoadSlotAniSequences(slots);
                ApplyAniPreviewFrames(slots);
            }
            return slots;
        }

        /// <summary>
        /// How many of the 17 Windows cursor slots are filled for this folder (excludes unknown extras).
        /// </summary>
        public static int CountMatchedSlots(string folderPath)
        {
            return MatchFolderSlots(folderPath).Count(s =>
                !s.IsExtra && !string.IsNullOrEmpty(s.FilePath) && File.Exists(s.FilePath));
        }

        public static List<CursorSlot> MatchFolderSlots(string folderPath)
        {
            var slots = CreateDefaultSlots();
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return slots;

            // Search for cursor files (.ani / .cur only; skip PNG/SVG etc.)
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".ani", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".cur", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
                return slots;

            // 0. Match from .inf or scheme_map.ini if present, then fill remaining slots by filename
            string schemeMapFile = Path.Combine(folderPath, "scheme_map.ini");
            if (File.Exists(schemeMapFile))
            {
                MatchFromSchemeMapFile(schemeMapFile, slots, folderPath);
            }

            var infFiles = Directory.GetFiles(folderPath, "*.inf", SearchOption.AllDirectories);
            if (infFiles.Length > 0)
            {
                MatchFromInfFile(infFiles[0], slots, folderPath);
            }

            var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in slots)
            {
                if (!string.IsNullOrEmpty(slot.FilePath))
                    matchedFiles.Add(slot.FilePath);
            }

            // 0b. Exact basename matching for standard Windows cursor pack names
            foreach (var file in files)
            {
                if (matchedFiles.Contains(file)) continue;

                string basename = Path.GetFileNameWithoutExtension(file);
                if (ExactBasenameToSlotKey.TryGetValue(basename, out var slotKey))
                {
                    var slot = slots.FirstOrDefault(s => s.KeyName == slotKey);
                    if (slot != null && string.IsNullOrEmpty(slot.FilePath))
                    {
                        slot.FilePath = file;
                        matchedFiles.Add(file);
                    }
                }
            }

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
            AppendExtraFileSlots(slots, files);
            return slots;
        }

        private static void AppendExtraFileSlots(List<CursorSlot> slots, List<string> files)
        {
            var usedPaths = new HashSet<string>(
                slots.Where(s => !string.IsNullOrEmpty(s.FilePath)).Select(s => s.FilePath),
                StringComparer.OrdinalIgnoreCase);

            int extraOrder = 100;
            foreach (var file in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (usedPaths.Contains(file))
                    continue;

                string baseName = Path.GetFileNameWithoutExtension(file);
                slots.Add(new CursorSlot
                {
                    Order = extraOrder++,
                    KeyName = string.Empty,
                    IsExtra = true,
                    DisplayName = baseName,
                    EnglishName = "無對應 Windows 功能",
                    FilePath = file
                });
            }
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

        public static void LoadSlotAniSequences(List<CursorSlot> slots)
        {
            foreach (var slot in slots)
            {
                if (string.IsNullOrEmpty(slot.FilePath) ||
                    !slot.FilePath.EndsWith(".ani", StringComparison.OrdinalIgnoreCase))
                    continue;

                slot.AniSequence = CursorIconHelper.LoadAniSequence(slot.FilePath, CursorIconHelper.SlotPreviewLoadSize);
                if (slot.AniSequence != null && slot.AniSequence.Frames.Count > 0)
                {
                    slot.CurrentFrameIndex = 0;
                    slot.NextFrameCountdown = slot.AniSequence.FrameRatesInJiffies.Count > 0
                        ? Math.Max(1, slot.AniSequence.FrameRatesInJiffies[0])
                        : 10;
                }
            }
        }

        public static void ApplyAniPreviewFrames(List<CursorSlot> slots)
        {
            foreach (var slot in slots)
            {
                if (slot.AniSequence != null && slot.AniSequence.Frames.Count > 0)
                    slot.PreviewImage = slot.AniSequence.Frames[slot.CurrentFrameIndex];
            }
        }

        private static void LoadSlotStaticPreviews(List<CursorSlot> slots)
        {
            foreach (var slot in slots)
            {
                slot.AniSequence = null;
                slot.CurrentFrameIndex = 0;
                slot.NextFrameCountdown = 1;

                if (!string.IsNullOrEmpty(slot.FilePath))
                {
                    slot.PreviewImage = CursorIconHelper.LoadCursorImage(
                        slot.FilePath, CursorIconHelper.SlotPreviewLoadSize);
                }
                else
                {
                    slot.PreviewImage = null;
                }
            }
        }

        private static void LoadSlotPreviews(List<CursorSlot> slots)
        {
            LoadSlotStaticPreviews(slots);
            LoadSlotAniSequences(slots);
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
                    if (line.StartsWith(";") || line.StartsWith("[") || string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim().Trim('%', ' ');
                        string val = parts[1].Trim().Trim('"', ' ');
                        dict[key] = val;
                    }
                }

                var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in dict)
                {
                    string v = kvp.Value;
                    if (dict.TryGetValue(v.Trim('%'), out string? realVal))
                    {
                        v = realVal;
                    }
                    resolved[kvp.Key] = v;
                }

                // Direct AddReg lines: HKCU,"Control Panel\Cursors",NWPen,...,"%hand%"
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (!line.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) &&
                        !line.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (line.Contains(@"Control Panel\Cursors\Schemes", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!line.Contains(@"Control Panel\Cursors", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fields = SplitInfFields(line);
                    if (fields.Count < 5) continue;

                    string valueName = fields[2];
                    if (string.IsNullOrEmpty(valueName)) continue;

                    var slot = slots.FirstOrDefault(s => s.KeyName.Equals(valueName, StringComparison.OrdinalIgnoreCase));
                    if (slot == null || !string.IsNullOrEmpty(slot.FilePath)) continue;

                    string? fileName = ResolveInfFileName(fields[4], resolved, dict);
                    if (string.IsNullOrEmpty(fileName)) continue;

                    string target = Path.Combine(infDir, fileName);
                    if (File.Exists(target))
                        slot.FilePath = target;
                }

                foreach (var slot in slots)
                {
                    if (!string.IsNullOrEmpty(slot.FilePath)) continue;
                    if (!InfTagMap.TryGetValue(slot.KeyName, out var keys)) continue;

                    foreach (var k in keys)
                    {
                        if (resolved.TryGetValue(k, out var fileName) || dict.TryGetValue(k, out fileName))
                        {
                            fileName = Path.GetFileName(fileName.Replace('/', '\\'));
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

        private static List<string> SplitInfFields(string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString().Trim());
            return result;
        }

        private static string? ResolveInfFileName(string value, Dictionary<string, string> resolved, Dictionary<string, string> dict)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            string result = value.Trim().Trim('"');
            foreach (var map in new[] { resolved, dict })
            {
                foreach (var kvp in map)
                    result = result.Replace($"%{kvp.Key}%", kvp.Value, StringComparison.OrdinalIgnoreCase);
            }

            string name = Path.GetFileName(result.Replace('/', '\\'));
            if (name.StartsWith('%') && name.EndsWith('%') && name.Length > 2)
            {
                string token = name.Trim('%');
                if (resolved.TryGetValue(token, out var mapped) || dict.TryGetValue(token, out mapped))
                    name = Path.GetFileName(mapped.Replace('/', '\\'));
            }

            return string.IsNullOrEmpty(name) || name.Contains('%') ? null : name;
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
                if (kvp.Value.Any(k => KeywordMatchesFileName(k, text)))
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
                return keywords.Any(k => KeywordMatchesFileName(k, fileName));
            }
            return false;
        }

        private static bool KeywordMatchesFileName(string keyword, string fileName)
        {
            if (fileName.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return true;

            // "hand" must not steal Handwriting.ani (NWPen)
            if (keyword.Equals("hand", StringComparison.OrdinalIgnoreCase) &&
                fileName.Contains("handwriting", StringComparison.OrdinalIgnoreCase))
                return false;

            // "pen" must not steal Person.ani (Person slot)
            if (keyword.Equals("pen", StringComparison.OrdinalIgnoreCase) &&
                fileName.Contains("person", StringComparison.OrdinalIgnoreCase))
                return false;

            return fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
    }
}
