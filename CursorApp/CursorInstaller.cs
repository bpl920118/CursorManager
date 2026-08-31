using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HololiveCursorApp
{
    public static class CursorInstaller
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr hcur, uint id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CopyIcon(IntPtr hIcon);

        private const uint SPI_SETCURSORS = 0x0057;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        private const uint IMAGE_CURSOR = 2;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const uint LR_DEFAULTSIZE = 0x0040;

        // OCR (OEM Cursor Resource) IDs for live cursor replacement
        private static readonly Dictionary<string, uint> OcrCursorIds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arrow"] = 32512,       // OCR_NORMAL
            ["IBeam"] = 32513,       // OCR_IBEAM
            ["Wait"] = 32514,        // OCR_WAIT
            ["Crosshair"] = 32515,   // OCR_CROSS
            ["UpArrow"] = 32516,     // OCR_UP
            ["SizeNWSE"] = 32642,    // OCR_SIZENWSE (Diagonal 1)
            ["SizeNESW"] = 32643,    // OCR_SIZENESW (Diagonal 2)
            ["SizeWE"] = 32644,      // OCR_SIZEWE
            ["SizeNS"] = 32645,      // OCR_SIZENS
            ["SizeAll"] = 32646,     // OCR_SIZEALL
            ["No"] = 32648,          // OCR_NO
            ["Hand"] = 32649,        // OCR_HAND
            ["AppStarting"] = 32650, // OCR_APPSTARTING
            ["Help"] = 32651         // OCR_HELP
        };

        private static readonly string[] StandardCursorKeys = new[]
        {
            "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam",
            "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW",
            "SizeAll", "UpArrow", "Hand"
        };

        public static bool ApplyCursors(IEnumerable<CursorSlot> slots, string themeName = "Custom Cursor")
        {
            try
            {
                // Prepare installed paths map directly from slots
                var installedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var slot in slots)
                {
                    if (!string.IsNullOrEmpty(slot.FilePath) && File.Exists(slot.FilePath))
                    {
                        installedPaths[slot.KeyName] = Path.GetFullPath(slot.FilePath);
                    }
                }

                // 1. Write to Registry: HKCU\Control Panel\Cursors
                try
                {
                    using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors", true))
                    {
                        if (key != null)
                        {
                            key.SetValue("", themeName, RegistryValueKind.String);
                            key.SetValue("Scheme Source", 2, RegistryValueKind.DWord);

                            foreach (var keyName in StandardCursorKeys)
                            {
                                if (installedPaths.TryGetValue(keyName, out var targetPath) && !string.IsNullOrEmpty(targetPath))
                                {
                                    key.SetValue(keyName, targetPath, RegistryValueKind.String);
                                }
                                else
                                {
                                    key.SetValue(keyName, "", RegistryValueKind.String);
                                }
                            }
                        }
                    }
                }
                catch { }

                // 2. Save Scheme in HKCU\Control Panel\Cursors\Schemes
                try
                {
                    using (RegistryKey? schemesKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Cursors\Schemes", true))
                    {
                        if (schemesKey != null)
                        {
                            var schemeParts = StandardCursorKeys.Select(k => installedPaths.TryGetValue(k, out var p) ? p : "");
                            string schemeValue = string.Join(",", schemeParts);
                            schemesKey.SetValue(themeName, schemeValue, RegistryValueKind.String);
                        }
                    }
                }
                catch { }

                // 3. Broadcast system parameters change
                try
                {
                    SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                }
                catch { }

                // 4. Direct in-memory live cursor replacement via SetSystemCursor (Guarantees immediate Windows DWM response for all resize cursors)
                foreach (var kvp in OcrCursorIds)
                {
                    if (installedPaths.TryGetValue(kvp.Key, out var filePath) && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        try
                        {
                            IntPtr hCur = LoadImage(IntPtr.Zero, filePath, IMAGE_CURSOR, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                            if (hCur != IntPtr.Zero)
                            {
                                SetSystemCursor(hCur, kvp.Value);
                            }
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
                    if (key != null)
                    {
                        key.SetValue("", "Windows Default");
                        key.SetValue("Scheme Source", 0, RegistryValueKind.DWord);

                        foreach (var keyName in StandardCursorKeys)
                        {
                            key.SetValue(keyName, "", RegistryValueKind.String);
                        }
                    }
                }

                // Broadcast change to restore defaults
                return SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            catch
            {
                return false;
            }
        }
    }
}
