using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CursorManager
{
    public static class CursorInstaller
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);

        private const uint SPI_SETCURSORS = 0x0057;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        private const uint IMAGE_CURSOR = 2;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const uint LR_DEFAULTSIZE = 0x0040;

        private static readonly Dictionary<string, uint> OcrCursorIds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arrow"] = 32512,
            ["IBeam"] = 32513,
            ["Wait"] = 32514,
            ["Crosshair"] = 32515,
            ["UpArrow"] = 32516,
            ["SizeNWSE"] = 32642,
            ["SizeNESW"] = 32643,
            ["SizeWE"] = 32644,
            ["SizeNS"] = 32645,
            ["SizeAll"] = 32646,
            ["No"] = 32648,
            ["Hand"] = 32649,
            ["AppStarting"] = 32650,
            ["Help"] = 32651,
            ["NWPen"] = 32631,
            ["Person"] = 32672,
            ["Pin"] = 32671
        };

        private static readonly string[] StandardCursorKeys = WindowsCursorSlots.RegistryKeyOrder;

        public static bool ApplyCursors(
            IEnumerable<CursorSlot> slots,
            string themeName = "自訂鼠標",
            int sizePx = MousePointerSizeHelper.DefaultPx,
            string scaleMode = MousePointerSizeHelper.DefaultMode)
        {
            try
            {
                int baseSize = MousePointerSizeHelper.GetBaseSize(sizePx);
                int accessibilityLevel = MousePointerSizeHelper.PxToNearestLevel(baseSize);
                scaleMode = MousePointerSizeHelper.NormalizeMode(scaleMode);
                bool forced = scaleMode == MousePointerSizeHelper.ModeForced;

                var installedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var slot in slots)
                {
                    if (slot.IsExtra)
                        continue;

                    if (!string.IsNullOrEmpty(slot.FilePath) && File.Exists(slot.FilePath))
                        installedPaths[slot.KeyName] = Path.GetFullPath(slot.FilePath);
                }

                try
                {
                    using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors", true))
                    {
                        if (key != null)
                        {
                            key.SetValue("", themeName, RegistryValueKind.String);
                            key.SetValue("Scheme Source", 2, RegistryValueKind.DWord);
                            key.SetValue("CursorBaseSize", baseSize, RegistryValueKind.DWord);

                            foreach (var keyName in StandardCursorKeys)
                            {
                                if (installedPaths.TryGetValue(keyName, out var targetPath) && !string.IsNullOrEmpty(targetPath))
                                    key.SetValue(keyName, targetPath, RegistryValueKind.String);
                                else
                                    key.SetValue(keyName, "", RegistryValueKind.String);
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    using (RegistryKey? accessibility = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility", true))
                    {
                        accessibility?.SetValue("CursorSize", accessibilityLevel, RegistryValueKind.DWord);
                    }
                }
                catch { }

                try
                {
                    using (RegistryKey? schemesKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors\Schemes", true))
                    {
                        if (schemesKey != null)
                        {
                            var schemeParts = StandardCursorKeys.Select(k => installedPaths.TryGetValue(k, out var p) ? p : "");
                            schemesKey.SetValue(themeName, string.Join(",", schemeParts), RegistryValueKind.String);
                        }
                    }
                }
                catch { }

                try
                {
                    SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                }
                catch { }

                foreach (var kvp in OcrCursorIds)
                {
                    if (!installedPaths.TryGetValue(kvp.Key, out var filePath) || string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                        continue;

                    try
                    {
                        IntPtr hCur;
                        if (forced)
                            hCur = LoadImage(IntPtr.Zero, filePath, IMAGE_CURSOR, baseSize, baseSize, LR_LOADFROMFILE);
                        else
                            hCur = LoadImage(IntPtr.Zero, filePath, IMAGE_CURSOR, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

                        if (hCur != IntPtr.Zero)
                            SetSystemCursor(hCur, kvp.Value);
                    }
                    catch { }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Apply only global size (keep current scheme paths). Used when changing size without reloading theme files.
        /// </summary>
        public static bool ApplyPointerSizeOnly(int sizePx, string scaleMode, IEnumerable<CursorSlot>? slotsForForced = null)
        {
            try
            {
                int baseSize = MousePointerSizeHelper.GetBaseSize(sizePx);
                int accessibilityLevel = MousePointerSizeHelper.PxToNearestLevel(baseSize);
                scaleMode = MousePointerSizeHelper.NormalizeMode(scaleMode);

                try
                {
                    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", true))
                    {
                        key?.SetValue("CursorBaseSize", baseSize, RegistryValueKind.DWord);
                    }
                }
                catch { }

                try
                {
                    using (RegistryKey? accessibility = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility", true))
                    {
                        accessibility?.SetValue("CursorSize", accessibilityLevel, RegistryValueKind.DWord);
                    }
                }
                catch { }

                SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

                if (scaleMode == MousePointerSizeHelper.ModeForced && slotsForForced != null)
                {
                    foreach (var slot in slotsForForced)
                    {
                        if (string.IsNullOrEmpty(slot.FilePath) || !File.Exists(slot.FilePath))
                            continue;
                        if (!OcrCursorIds.TryGetValue(slot.KeyName, out uint ocrId))
                            continue;

                        try
                        {
                            IntPtr hCur = LoadImage(IntPtr.Zero, slot.FilePath, IMAGE_CURSOR, baseSize, baseSize, LR_LOADFROMFILE);
                            if (hCur != IntPtr.Zero)
                                SetSystemCursor(hCur, ocrId);
                        }
                        catch { }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool RestoreDefaultCursors()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", true))
                {
                    if (key == null)
                        return false;

                    key.SetValue("", "Windows Default");
                    key.SetValue("Scheme Source", 0, RegistryValueKind.DWord);

                    foreach (var keyName in StandardCursorKeys)
                        key.SetValue(keyName, "", RegistryValueKind.String);
                }

                // SPI_SETCURSORS often returns false on Windows even when the refresh succeeds.
                try
                {
                    SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                }
                catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True when the active Arrow cursor still points under the last applied theme folder.
        /// </summary>
        public static bool IsAppliedSchemeStillActive(string? appliedFolderPath)
        {
            if (string.IsNullOrWhiteSpace(appliedFolderPath) || !Directory.Exists(appliedFolderPath))
                return false;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors", false);
                string? arrow = key?.GetValue("Arrow") as string;
                if (string.IsNullOrWhiteSpace(arrow))
                    return false;

                string fullArrow = Path.GetFullPath(Environment.ExpandEnvironmentVariables(arrow));
                string fullFolder = Path.GetFullPath(appliedFolderPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return fullArrow.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || fullArrow.StartsWith(fullFolder + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetDirectoryName(fullArrow), fullFolder, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
