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

        // Preview decode cap: keep native pixels when ≤ this size; downscale larger frames with alpha-safe HQ.
        // UI shows them 1:1 (Stretch=None) so we never do non-integer NearestNeighbor upscales.
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
                byte[] data = File.ReadAllBytes(filePath);

                // ANI: decode first embedded icon frame (correct BGRA / alpha)
                if (filePath.EndsWith(".ani", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseAniIconBlocks(data, out _, out var iconBlocks, out _) &&
                        iconBlocks.Count > 0)
                    {
                        var frame = DecodeIconBlock(iconBlocks[0], size);
                        if (frame != null) return frame;
                    }
                }
                // CUR / ICO: decode file bytes directly
                else if (filePath.EndsWith(".cur", StringComparison.OrdinalIgnoreCase) ||
                         filePath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    var frame = DecodeIconBlock(data, size);
                    if (frame != null) return frame;
                }
            }
            catch { }

            // Fallback: Win32 LoadImage (may tint / flatten alpha on some 32bpp cursors)
            return LoadCursorViaWin32(filePath, size);
        }

        private static ImageSource? LoadCursorViaWin32(string filePath, int size)
        {
            try
            {
                IntPtr hCursor = LoadImage(IntPtr.Zero, filePath, IMAGE_CURSOR, size, size, LR_LOADFROMFILE);
                if (hCursor == IntPtr.Zero)
                    hCursor = LoadImage(IntPtr.Zero, filePath, IMAGE_CURSOR, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

                if (hCursor != IntPtr.Zero)
                {
                    try
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(
                            hCursor,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        return ScaleToFit(bs, size);
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
                if (!TryParseAniIconBlocks(data, out int defaultJifRate, out var iconBlocks, out var rateList))
                    return null;

                if (iconBlocks.Count == 0)
                {
                    var singleImg = LoadCursorViaWin32(filePath, size);
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
                for (int i = 0; i < iconBlocks.Count; i++)
                {
                    try
                    {
                        var frameImg = DecodeIconBlock(iconBlocks[i], size);
                        if (frameImg != null)
                        {
                            seq.Frames.Add(frameImg);
                            int r = (i < rateList.Count) ? rateList[i] : defaultJifRate;
                            seq.FrameRatesInJiffies.Add(r);
                        }
                    }
                    catch { }
                }

                return seq.Frames.Count > 0 ? seq : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryParseAniIconBlocks(
            byte[] data,
            out int defaultJifRate,
            out List<byte[]> iconBlocks,
            out List<int> rateList)
        {
            defaultJifRate = 10;
            iconBlocks = new List<byte[]>();
            rateList = new List<int>();

            if (data.Length < 12) return false;
            if (Encoding.ASCII.GetString(data, 0, 4) != "RIFF" ||
                Encoding.ASCII.GetString(data, 8, 4) != "ACON")
                return false;

            int pos = 12;
            while (pos + 8 <= data.Length)
            {
                string chunkId = Encoding.ASCII.GetString(data, pos, 4);
                int chunkSize = BitConverter.ToInt32(data, pos + 4);
                pos += 8;

                if (chunkSize < 0 || pos + chunkSize > data.Length) break;

                if (chunkId == "anih" && chunkSize >= 36)
                {
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

                            if (subSize < 0 || framPos + subSize > framEnd) break;

                            if (subId == "icon")
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

            return true;
        }

        /// <summary>
        /// Decodes a .cur / .ico blob (ICONDIR + image) into a BGRA BitmapSource.
        /// Avoids CreateBitmapSourceFromHIcon color/alpha distortion on 32bpp cursors.
        /// </summary>
        private static ImageSource? DecodeIconBlock(byte[] iconData, int targetSize)
        {
            if (iconData == null || iconData.Length < 22) return null;

            short reserved = BitConverter.ToInt16(iconData, 0);
            short type = BitConverter.ToInt16(iconData, 2);
            short count = BitConverter.ToInt16(iconData, 4);
            if (reserved != 0 || (type != 1 && type != 2) || count < 1)
                return null;

            int entryIndex = SelectBestIconEntry(iconData, count, targetSize);
            int entryOffset = 6 + entryIndex * 16;
            if (entryOffset + 16 > iconData.Length) return null;

            int bytesInRes = BitConverter.ToInt32(iconData, entryOffset + 8);
            int imageOffset = BitConverter.ToInt32(iconData, entryOffset + 12);
            if (imageOffset < 0 || bytesInRes <= 0 ||
                imageOffset + bytesInRes > iconData.Length)
                return null;

            // PNG-compressed icon (Vista+)
            if (imageOffset + 8 <= iconData.Length &&
                iconData[imageOffset] == 0x89 &&
                iconData[imageOffset + 1] == 0x50 &&
                iconData[imageOffset + 2] == 0x4E &&
                iconData[imageOffset + 3] == 0x47)
            {
                try
                {
                    using var ms = new MemoryStream(iconData, imageOffset, bytesInRes, writable: false);
                    var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0) return null;
                    BitmapSource png = decoder.Frames[0];
                    png.Freeze();
                    return ScaleToFit(png, targetSize);
                }
                catch
                {
                    return null;
                }
            }

            // Classic DIB (BITMAPINFOHEADER + XOR [+ AND])
            if (imageOffset + 40 > iconData.Length) return null;
            int biSize = BitConverter.ToInt32(iconData, imageOffset);
            if (biSize < 40) return null;

            int biWidth = BitConverter.ToInt32(iconData, imageOffset + 4);
            int biHeight = BitConverter.ToInt32(iconData, imageOffset + 8); // XOR+AND for icons
            short biBitCount = BitConverter.ToInt16(iconData, imageOffset + 14);

            int xorHeight = Math.Abs(biHeight) / 2;
            if (xorHeight <= 0) xorHeight = Math.Abs(biHeight);
            if (biWidth <= 0 || xorHeight <= 0) return null;

            if (biBitCount == 32)
            {
                var bmp = DecodeBgra32Dib(iconData, imageOffset + biSize, biWidth, xorHeight);
                if (bmp != null)
                    return ScaleToFit(bmp, targetSize);
            }

            if (biBitCount == 1 || biBitCount == 4 || biBitCount == 8)
            {
                var bmp = DecodeIndexedDib(
                    iconData, imageOffset, biSize, biWidth, xorHeight, biBitCount);
                if (bmp != null)
                    return ScaleToFit(bmp, targetSize);
            }

            // Unusual layouts: fall back via a unique temp .cur
            return DecodeIconBlockViaTempFile(iconData, targetSize);
        }

        private static int SelectBestIconEntry(byte[] iconData, int count, int targetSize)
        {
            int best = 0;
            int bestScore = int.MinValue;
            for (int i = 0; i < count; i++)
            {
                int entryOffset = 6 + i * 16;
                if (entryOffset + 16 > iconData.Length) break;

                int w = iconData[entryOffset];
                int h = iconData[entryOffset + 1];
                if (w == 0) w = 256;
                if (h == 0) h = 256;
                int bpp = BitConverter.ToInt16(iconData, entryOffset + 6);
                if (bpp == 0) bpp = 32;

                // Prefer exact/near target size, then higher bit depth, then larger
                int sizeDiff = Math.Abs(Math.Max(w, h) - Math.Max(targetSize, 1));
                int score = (bpp * 1000) - (sizeDiff * 10) + Math.Max(w, h);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private static BitmapSource? DecodeBgra32Dib(byte[] data, int pixelOffset, int width, int height)
        {
            long needed = (long)width * height * 4;
            if (pixelOffset < 0 || pixelOffset + needed > data.Length || width <= 0 || height <= 0)
                return null;

            int stride = width * 4;
            byte[] pixels = new byte[stride * height];

            // DIBs are stored bottom-up
            for (int y = 0; y < height; y++)
            {
                int srcRow = pixelOffset + (height - 1 - y) * stride;
                int dstRow = y * stride;
                Buffer.BlockCopy(data, srcRow, pixels, dstRow, stride);
            }

            // If the alpha channel is entirely empty, treat pixels as opaque
            // (some cursors store transparency only in the AND mask).
            bool anyAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) { anyAlpha = true; break; }
            }
            if (!anyAlpha)
            {
                for (int i = 3; i < pixels.Length; i += 4)
                    pixels[i] = 255;
            }
            else
            {
                // Clear RGB under fully transparent pixels so HQ downscales don't bleed white halos.
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] == 0)
                    {
                        pixels[i] = 0;
                        pixels[i + 1] = 0;
                        pixels[i + 2] = 0;
                    }
                }
            }

            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// Decodes 1/4/8 bpp icon XOR + palette + 1bpp AND mask into BGRA32.
        /// Avoids CreateBitmapSourceFromHIcon alpha speckles on palette cursors (e.g. Staff Cursor).
        /// </summary>
        private static BitmapSource? DecodeIndexedDib(
            byte[] data, int imageOffset, int biSize, int width, int height, short bitCount)
        {
            if (width <= 0 || height <= 0 || biSize < 40) return null;

            int clrUsed = BitConverter.ToInt32(data, imageOffset + 32);
            int paletteEntries = clrUsed > 0 ? clrUsed : (1 << bitCount);
            if (paletteEntries <= 0 || paletteEntries > 256) return null;

            int paletteOffset = imageOffset + biSize;
            long paletteBytes = (long)paletteEntries * 4;
            if (paletteOffset < 0 || paletteOffset + paletteBytes > data.Length)
                return null;

            int xorStride = ((width * bitCount + 31) / 32) * 4;
            long xorBytes = (long)xorStride * height;
            int xorOffset = paletteOffset + (int)paletteBytes;
            if (xorOffset + xorBytes > data.Length) return null;

            int andStride = ((width + 31) / 32) * 4;
            long andBytes = (long)andStride * height;
            int andOffset = xorOffset + (int)xorBytes;
            bool hasAnd = andOffset + andBytes <= data.Length;

            byte[] pixels = new byte[width * height * 4];
            int dstStride = width * 4;

            for (int y = 0; y < height; y++)
            {
                int srcY = height - 1 - y; // bottom-up
                int xorRow = xorOffset + srcY * xorStride;
                int andRow = hasAnd ? andOffset + srcY * andStride : 0;
                int dstRow = y * dstStride;

                for (int x = 0; x < width; x++)
                {
                    int index;
                    if (bitCount == 8)
                    {
                        index = data[xorRow + x];
                    }
                    else if (bitCount == 4)
                    {
                        byte packed = data[xorRow + (x / 2)];
                        index = ((x & 1) == 0) ? (packed >> 4) : (packed & 0x0F);
                    }
                    else // 1 bpp
                    {
                        byte packed = data[xorRow + (x / 8)];
                        index = (packed >> (7 - (x & 7))) & 1;
                    }

                    if (index < 0) index = 0;
                    if (index >= paletteEntries) index = paletteEntries - 1;

                    int pal = paletteOffset + index * 4;
                    byte b = data[pal];
                    byte g = data[pal + 1];
                    byte r = data[pal + 2];

                    bool transparent = false;
                    if (hasAnd)
                    {
                        byte maskByte = data[andRow + (x / 8)];
                        transparent = ((maskByte >> (7 - (x & 7))) & 1) != 0;
                    }

                    int di = dstRow + x * 4;
                    if (transparent)
                    {
                        pixels[di] = 0;
                        pixels[di + 1] = 0;
                        pixels[di + 2] = 0;
                        pixels[di + 3] = 0;
                    }
                    else
                    {
                        pixels[di] = b;
                        pixels[di + 1] = g;
                        pixels[di + 2] = r;
                        pixels[di + 3] = 255;
                    }
                }
            }

            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, dstStride, 0);
            bmp.Freeze();
            return bmp;
        }

        private static ImageSource? DecodeIconBlockViaTempFile(byte[] iconData, int targetSize)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "CursorManagerAniPreview");
            Directory.CreateDirectory(tempDir);
            string tempIconPath = Path.Combine(tempDir, $"frame_{Guid.NewGuid():N}.cur");
            try
            {
                File.WriteAllBytes(tempIconPath, iconData);
                return LoadCursorViaWin32(tempIconPath, targetSize);
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(tempIconPath); } catch { }
            }
        }

        private static ImageSource ScaleToFit(BitmapSource src, int targetSize)
        {
            if (targetSize <= 0) return src;

            // Keep native pixels when they already fit; UI uses Stretch=None (1:1, no jagged upscale).
            if (src.PixelWidth <= targetSize && src.PixelHeight <= targetSize)
                return src;

            double scale = Math.Min(
                (double)targetSize / src.PixelWidth,
                (double)targetSize / src.PixelHeight);

            int w = Math.Max(1, (int)Math.Round(src.PixelWidth * scale));
            int h = Math.Max(1, (int)Math.Round(src.PixelHeight * scale));

            // HighQuality downscale via DrawingVisual (Fant) — TransformedBitmap bleeds RGB-under-alpha.
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(src, new Rect(0, 0, w, h));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();

            // Convert to Bgra32 for consistent Image binding / cache freeness
            var converted = new FormatConvertedBitmap(rtb, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        }
    }
}
