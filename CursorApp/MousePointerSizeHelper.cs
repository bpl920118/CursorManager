using System;

namespace CursorManager
{
    /// <summary>
    /// Global mouse pointer size in pixels (independent of theme).
    /// System mode = registry CursorBaseSize; Forced = LoadImage pixel size.
    /// </summary>
    public static class MousePointerSizeHelper
    {
        public const int MinPx = 16;
        public const int MaxPx = 96;
        public const int DefaultPx = 32;

        /// <summary>Legacy discrete levels (1–5) kept for config migration.</summary>
        public const int MinLevel = 1;
        public const int MaxLevel = 5;
        public const int DefaultLevel = 1;

        public const string ModeSystem = "System";
        public const string ModeForced = "Forced";
        public const string DefaultMode = ModeSystem;

        public static int NormalizePx(int px)
        {
            if (px < MinPx) return MinPx;
            if (px > MaxPx) return MaxPx;
            return px;
        }

        /// <summary>Accepts legacy levels 1–5 or absolute pixel values.</summary>
        public static int ParseSize(string? raw, int fallback = DefaultPx)
        {
            if (!int.TryParse(raw, out int value))
                return NormalizePx(fallback);

            // Legacy gear levels stored as 1–5
            if (value >= MinLevel && value <= MaxLevel)
                return LevelToPx(value);

            return NormalizePx(value);
        }

        public static int ParseLevel(string? raw, int fallback = DefaultLevel)
        {
            // Backward-compatible alias used by older call sites / config
            return PxToNearestLevel(ParseSize(raw, LevelToPx(fallback)));
        }

        public static int NormalizeLevel(int level)
        {
            if (level < MinLevel) return MinLevel;
            if (level > MaxLevel) return MaxLevel;
            return level;
        }

        public static string NormalizeMode(string? mode)
        {
            if (string.Equals(mode, ModeForced, StringComparison.OrdinalIgnoreCase))
                return ModeForced;
            return ModeSystem;
        }

        public static int LevelToPx(int level) => 32 + (NormalizeLevel(level) - 1) * 16;

        public static int PxToNearestLevel(int px)
        {
            px = NormalizePx(px);
            double t = (px - MinPx) / (double)(MaxPx - MinPx);
            int level = (int)Math.Round(t * (MaxLevel - MinLevel)) + MinLevel;
            return NormalizeLevel(level);
        }

        /// <summary>CursorBaseSize / forced pixel edge.</summary>
        public static int GetBaseSize(int sizeOrLevel)
        {
            // Values 1–5 are treated as legacy levels; everything else as pixels.
            if (sizeOrLevel >= MinLevel && sizeOrLevel <= MaxLevel)
                return LevelToPx(sizeOrLevel);
            return NormalizePx(sizeOrLevel);
        }

        public static string GetPxLabel(int px) => $"{NormalizePx(px)} PX";

        public static string GetLevelLabel(int sizeOrLevel) => GetPxLabel(GetBaseSize(sizeOrLevel));
    }
}
