using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CursorManager
{
    public enum ThemeSortMode
    {
        Name,
        Date,
        Recent
    }

    public enum ThemeFilterMode
    {
        All,
        Favorites,
        Recent
    }

    public sealed class ThemeEntryMetadata
    {
        public bool IsFavorite { get; set; }
        public DateTime? LastUsedUtc { get; set; }
    }

    public sealed class ThemeMetadataFile
    {
        public Dictionary<string, ThemeEntryMetadata> Themes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> RecentPaths { get; set; } = new();
        public string SortMode { get; set; } = ThemeSortMode.Name.ToString();
        public string FilterMode { get; set; } = ThemeFilterMode.All.ToString();
    }

    public static class ThemeMetadataStore
    {
        private const int MaxRecent = 30;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static ThemeMetadataFile _cache = new();
        private static bool _loaded;

        public static string MetadataFilePath => Path.Combine(AppPaths.DataRoot, "themes-metadata.json");

        public static ThemeSortMode SortMode { get; private set; } = ThemeSortMode.Name;
        public static ThemeFilterMode FilterMode { get; private set; } = ThemeFilterMode.All;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                if (!File.Exists(MetadataFilePath))
                    return;

                var json = File.ReadAllText(MetadataFilePath);
                var data = JsonSerializer.Deserialize<ThemeMetadataFile>(json, JsonOptions);
                if (data == null) return;

                _cache = data;
                SortMode = ParseSortMode(data.SortMode);
                FilterMode = ParseFilterMode(data.FilterMode);
            }
            catch { }
        }

        public static void ApplyTo(CharacterThemeItem item)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(item.FolderPath))
                return;

            if (_cache.Themes.TryGetValue(item.FolderPath, out var meta))
            {
                item.IsFavorite = meta.IsFavorite;
                item.LastUsedUtc = meta.LastUsedUtc;
            }
            else
            {
                item.IsFavorite = false;
                item.LastUsedUtc = null;
            }

            try
            {
                item.FolderModifiedUtc = Directory.Exists(item.FolderPath)
                    ? Directory.GetLastWriteTimeUtc(item.FolderPath)
                    : null;
            }
            catch
            {
                item.FolderModifiedUtc = null;
            }
        }

        public static void RecordApplied(string folderPath)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(folderPath))
                return;

            var meta = GetOrCreate(folderPath);
            meta.LastUsedUtc = DateTime.UtcNow;

            _cache.RecentPaths.RemoveAll(p => p.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
            _cache.RecentPaths.Insert(0, folderPath);
            if (_cache.RecentPaths.Count > MaxRecent)
                _cache.RecentPaths = _cache.RecentPaths.Take(MaxRecent).ToList();

            Save();
        }

        public static void SetFavorite(string folderPath, bool isFavorite)
        {
            EnsureLoaded();
            var meta = GetOrCreate(folderPath);
            meta.IsFavorite = isFavorite;
            Save();
        }

        public static void RenamePath(string oldPath, string newPath)
        {
            EnsureLoaded();
            if (_cache.Themes.TryGetValue(oldPath, out var meta))
            {
                _cache.Themes.Remove(oldPath);
                _cache.Themes[newPath] = meta;
            }

            for (int i = 0; i < _cache.RecentPaths.Count; i++)
            {
                if (_cache.RecentPaths[i].Equals(oldPath, StringComparison.OrdinalIgnoreCase))
                    _cache.RecentPaths[i] = newPath;
            }

            Save();
        }

        public static void RemovePath(string folderPath)
        {
            EnsureLoaded();
            _cache.Themes.Remove(folderPath);
            _cache.RecentPaths.RemoveAll(p => p.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public static void SetSortMode(ThemeSortMode mode)
        {
            EnsureLoaded();
            SortMode = mode;
            _cache.SortMode = mode.ToString();
            Save();
        }

        public static void SetFilterMode(ThemeFilterMode mode)
        {
            EnsureLoaded();
            FilterMode = mode;
            _cache.FilterMode = mode.ToString();
            Save();
        }

        public static IReadOnlyList<string> GetRecentPaths()
        {
            EnsureLoaded();
            return _cache.RecentPaths;
        }

        public static bool IsRecent(string folderPath)
        {
            EnsureLoaded();
            return _cache.RecentPaths.Any(p => p.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
        }

        private static ThemeEntryMetadata GetOrCreate(string folderPath)
        {
            if (!_cache.Themes.TryGetValue(folderPath, out var meta))
            {
                meta = new ThemeEntryMetadata();
                _cache.Themes[folderPath] = meta;
            }
            return meta;
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataRoot);
                var json = JsonSerializer.Serialize(_cache, JsonOptions);
                File.WriteAllText(MetadataFilePath, json);
            }
            catch { }
        }

        private static ThemeSortMode ParseSortMode(string? value)
        {
            return Enum.TryParse<ThemeSortMode>(value, true, out var mode) ? mode : ThemeSortMode.Name;
        }

        private static ThemeFilterMode ParseFilterMode(string? value)
        {
            return Enum.TryParse<ThemeFilterMode>(value, true, out var mode) ? mode : ThemeFilterMode.All;
        }
    }
}
