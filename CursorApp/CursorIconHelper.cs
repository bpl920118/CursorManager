using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorManager
{
    public class AniFrameSequence
    {
        public List<ImageSource> Frames { get; set; } = new();
        public List<int> FrameRatesInJiffies { get; set; } = new(); // 1 Jiffy = 1/60s (~16.6ms)
        public int TotalFrames => Frames.Count;
    }

    public static class CursorIconHelper
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint IMAGE_CURSOR = 2;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const uint LR_DEFAULTSIZE = 0x0040;

        // In-memory icon cache for static preview
        private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);
        // In-memory ANI sequence cache for animation playback
        private static readonly ConcurrentDictionary<string, AniFrameSequence?> AniCache = new(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache()
        {
            IconCache.Clear();
            AniCache.Clear();
        }

        // Preview render sizes (native load, 1:1 or 2x integer display only)
        public const int SidebarPreviewSize = 32;
        public const int SlotPreviewLoadSize = 32;

        public static ImageSource? LoadCursorImage(string filePath, int size = SlotPreviewLoadSize)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            string cacheKey = $"{filePath}_{size}";
            return IconCache.GetOrAdd(cacheKey, _ => LoadCursorImageInternal(filePath, size));
        }

        public static AniFrameSequence? LoadAniSequence(string filePath, int size = SlotPreviewLoadSize)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            if (!filePath.EndsWith(".ani", StringComparison.OrdinalIgnoreCase))
                return null;

            string cacheKey = $"{filePath}_{size}";
            return AniCache.GetOrAdd(cacheKey, _ => ParseAniFile(filePath, size));
        }

        private static ImageSource? LoadCursorImageInternal(string filePath, int size)
        {
            try
            {
                // Prefer native resolution so WPF can upscale with NearestNeighbor without blur.
                IntPtr hCursor = LoadImage(IntPtr.Zero, filePath, IMAGE_CURSOR, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                if (hCursor == IntPtr.Zero)
                {
                    hCursor = LoadImage(IntPtr.Zero, filePath, IMAGE_CURSOR, size, size, LR_LOADFROMFILE);
                }

                if (hCursor != IntPtr.Zero)
                {
                    try
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(
                            hCursor,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze(); // Make cross-thread accessible
                        return bs;
                    }
                    finally
                    {
                        DestroyIcon(hCursor);
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Parses a standard RIFF ANI file to extract all frame icons and rates.
        /// </summary>
        private static AniFrameSequence? ParseAniFile(string filePath, int size)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                if (data.Length < 12) return null;

                // Check RIFF header
                if (Encoding.ASCII.GetString(data, 0, 4) != "RIFF" || Encoding.ASCII.GetString(data, 8, 4) != "ACON")
                    return null;

                int pos = 12;
                int frameCount = 0;
                int defaultJifRate = 10; // Default: 10 jiffies (~166ms)
                List<int> rateList = new();
                List<byte[]> iconBlocks = new();

                while (pos + 8 <= data.Length)
                {
                    string chunkId = Encoding.ASCII.GetString(data, pos, 4);
                    int chunkSize = BitConverter.ToInt32(data, pos + 4);
                    pos += 8;

                    if (pos + chunkSize > data.Length) break;

                    if (chunkId == "anih" && chunkSize >= 36)
                    {
                        // cbSize (4), cFrames (4), cSteps (4), cx (4), cy (4), cBitCount (4), cPlanes (4), JifRate (4), flags (4)
                        frameCount = BitConverter.ToInt32(data, pos + 4);
                        defaultJifRate = BitConverter.ToInt32(data, pos + 28);
                        if (defaultJifRate <= 0) defaultJifRate = 10;
                    }
                    else if (chunkId == "rate")
                    {
                        int count = chunkSize / 4;
                        for (int i = 0; i < count; i++)
                        {
                            int r = BitConverter.ToInt32(data, pos + i * 4);
                            rateList.Add(r > 0 ? r : defaultJifRate);
                        }
                    }
                    else if (chunkId == "LIST")
                    {
                        if (chunkSize >= 4 && Encoding.ASCII.GetString(data, pos, 4) == "fram")
                        {
                            int framPos = pos + 4;
                            int framEnd = pos + chunkSize;

                            while (framPos + 8 <= framEnd)
                            {
                                string subId = Encoding.ASCII.GetString(data, framPos, 4);
                                int subSize = BitConverter.ToInt32(data, framPos + 4);
                                framPos += 8;

                                if (subId == "icon" && framPos + subSize <= framEnd)
                                {
                                    byte[] iconData = new byte[subSize];
                                    Array.Copy(data, framPos, iconData, 0, subSize);
                                    iconBlocks.Add(iconData);
                                }

                                framPos += subSize;
                                if (framPos % 2 != 0) framPos++; // WORD aligned
                            }
                        }
                    }

                    pos += chunkSize;
                    if (pos % 2 != 0) pos++; // WORD aligned
                }

                if (iconBlocks.Count == 0)
                {
                    // If no icon chunk found, fallback to single frame
                    var singleImg = LoadCursorImageInternal(filePath, size);
                    if (singleImg != null)
                    {
                        return new AniFrameSequence
                        {
                            Frames = new List<ImageSource> { singleImg },
                            FrameRatesInJiffies = new List<int> { defaultJifRate }
                        };
                    }
                    return null;
                }

                var seq = new AniFrameSequence();
                string tempDir = Path.Combine(Path.GetTempPath(), "CursorManagerAniPreview");
                Directory.CreateDirectory(tempDir);

                for (int i = 0; i < iconBlocks.Count; i++)
                {
                    string tempIconPath = Path.Combine(tempDir, $"frame_{Guid.NewGuid():N}.cur");
                    try
                    {
                        File.WriteAllBytes(tempIconPath, iconBlocks[i]);
                        var frameImg = LoadCursorImageInternal(tempIconPath, size);
                        if (frameImg != null)
                        {
                            seq.Frames.Add(frameImg);
                            int r = (i < rateList.Count) ? rateList[i] : defaultJifRate;
                            seq.FrameRatesInJiffies.Add(r);
                        }
                    }
                    finally
                    {
                        try { File.Delete(tempIconPath); } catch { }
                    }
                }

                return seq.Frames.Count > 0 ? seq : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
