using System;
using System.Collections.Generic;

namespace CursorManager
{
    public static class UiScaleHelper
    {
        public const string Small = "Small";
        public const string Normal = "Normal";
        public const string Large = "Large";
        public const string DefaultPreset = Normal;

        public const string AppFontFamily = "Segoe UI Variable, Segoe UI, Microsoft JhengHei UI, sans-serif";

        private const double BaseScale = 1.0;

        private static readonly Dictionary<string, double> PresetMultipliers = new(StringComparer.OrdinalIgnoreCase)
        {
            [Small] = 0.90,
            [Normal] = 1.00,
            [Large] = 1.15,
        };

        public static string NormalizePreset(string? preset)
        {
            if (string.IsNullOrWhiteSpace(preset))
                return DefaultPreset;

            // Backward compatibility with removed XLarge preset
            if (preset.Equals("XLarge", StringComparison.OrdinalIgnoreCase) ||
                preset.Equals("ExtraLarge", StringComparison.OrdinalIgnoreCase))
            {
                return Large;
            }

            foreach (var key in PresetMultipliers.Keys)
            {
                if (key.Equals(preset, StringComparison.OrdinalIgnoreCase))
                    return key;
            }

            return DefaultPreset;
        }

        public static double GetEffectiveScale(string? preset)
        {
            string normalized = NormalizePreset(preset);
            return BaseScale * PresetMultipliers[normalized];
        }
    }
}
