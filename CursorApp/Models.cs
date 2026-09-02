using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;

namespace CursorManager
{
    public static class WindowsCursorSlots
    {
        public const int StandardCount = 17;

        public static readonly string[] RegistryKeyOrder =
        {
            "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam",
            "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW",
            "SizeAll", "UpArrow", "Hand", "Person", "Pin"
        };
    }

    public static class ThemeGroupNames
    {
        public const string Ungrouped = "未分組";
        public const string Temporary = "未存入庫";
        public const string LegacyRoot = "自訂鼠標";

        public static bool IsRootLevel(string? group) =>
            group is Ungrouped or Temporary or LegacyRoot;
    }

    public class CursorSlot : INotifyPropertyChanged
    {
        public string KeyName { get; set; } = string.Empty;       // Registry Key Name, e.g. "Arrow"
        public string DisplayName { get; set; } = string.Empty;   // Chinese Name, e.g. "標準選擇 (Normal)"
        public string EnglishName { get; set; } = string.Empty;   // e.g. "Normal Select"
        public string TagKeyword { get; set; } = string.Empty;    // Default Tag, e.g. "nomal select"
        public int Order { get; set; }

        /// <summary>Extra .ani/.cur in the folder that does not map to any of the 17 Windows cursor roles.</summary>
        public bool IsExtra { get; set; }

        private string _filePath = string.Empty;
        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                    OnPropertyChanged(nameof(FileName));
                    OnPropertyChanged(nameof(HasFile));
                }
            }
        }

        public string FileName => string.IsNullOrEmpty(FilePath) ? "(未分配 / 保持預設)" : System.IO.Path.GetFileName(FilePath);
        public bool HasFile => !string.IsNullOrEmpty(FilePath) && System.IO.File.Exists(FilePath);

        /// <summary>Human-readable Windows cursor role for the slot card (includes registry key for standard slots).</summary>
        public string WindowsRoleDisplay
        {
            get
            {
                if (IsExtra)
                    return "無對應 Windows 功能";

                if (!string.IsNullOrEmpty(EnglishName) && !string.IsNullOrEmpty(KeyName))
                    return $"Windows：{EnglishName}（{KeyName}）";

                return string.IsNullOrEmpty(EnglishName) ? KeyName : EnglishName;
            }
        }

        public string SlotTooltip
        {
            get
            {
                if (IsExtra)
                    return $"「{DisplayName}」無法對應到 Windows 的 {WindowsCursorSlots.StandardCount} 種鼠標角色（如 Normal Select、Link Select 等）。\n僅供預覽，套用時不會寫入系統。";

                if (!string.IsNullOrEmpty(KeyName))
                    return $"對應 Windows 鼠標：{DisplayName} / {EnglishName}\n登錄鍵：{KeyName}";

                return DisplayName;
            }
        }

        private ImageSource? _previewImage;
        public ImageSource? PreviewImage
        {
            get => _previewImage;
            set
            {
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
            }
        }

        // Animation sequence data for dynamic preview
        public AniFrameSequence? AniSequence { get; set; }
        public int CurrentFrameIndex { get; set; }
        public int NextFrameCountdown { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CharacterThemeItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        private string _group = string.Empty;
        public string Group
        {
            get => _group;
            set
            {
                if (_group != value)
                {
                    _group = value;
                    OnPropertyChanged(nameof(Group));
                    OnPropertyChanged(nameof(GroupDisplay));
                }
            }
        }

        public string FolderPath { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public string PreviewFilePath { get; set; } = string.Empty;
        public ImageSource? PreviewImage { get; set; }

        /// <summary>
        /// External folder not copied into the cursor library (session-only list entry).
        /// </summary>
        public bool IsTemporary { get; set; }

        private bool _isCurrentlyInUse;
        /// <summary>
        /// True when this theme is the one currently applied to Windows.
        /// </summary>
        public bool IsCurrentlyInUse
        {
            get => _isCurrentlyInUse;
            set
            {
                if (_isCurrentlyInUse != value)
                {
                    _isCurrentlyInUse = value;
                    OnPropertyChanged(nameof(IsCurrentlyInUse));
                }
            }
        }

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite != value)
                {
                    _isFavorite = value;
                    OnPropertyChanged(nameof(IsFavorite));
                    OnPropertyChanged(nameof(FavoriteIcon));
                }
            }
        }

        public string FavoriteIcon => IsFavorite ? "⭐" : "☆";

        public DateTime? LastUsedUtc { get; set; }
        public DateTime? FolderModifiedUtc { get; set; }

        public string GroupDisplay => IsTemporary ? "未存入庫" : Group;

        /// <summary>Leaf nodes in the theme TreeView; always expanded (ignored).</summary>
        public bool IsExpanded { get; set; } = true;

        public string FileCountDisplay => $"{FileCount}/{WindowsCursorSlots.StandardCount}";

        private bool _isSelectedForBatch;
        public bool IsSelectedForBatch
        {
            get => _isSelectedForBatch;
            set
            {
                if (_isSelectedForBatch != value)
                {
                    _isSelectedForBatch = value;
                    OnPropertyChanged(nameof(IsSelectedForBatch));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => Name;
    }

    public class ThemeGroupNode : INotifyPropertyChanged
    {
        private bool _isExpanded = true;

        public string Name { get; set; } = string.Empty;

        public System.Collections.ObjectModel.ObservableCollection<CharacterThemeItem> Themes { get; } = new();

        public string HeaderDisplay => $"{Name} ({Themes.Count})";

        public bool? IsGroupBatchChecked
        {
            get
            {
                if (Themes.Count == 0)
                    return false;

                int selected = Themes.Count(t => t.IsSelectedForBatch);
                if (selected == 0)
                    return false;
                if (selected == Themes.Count)
                    return true;
                return null;
            }
            set
            {
                bool selectAll = value == true;
                foreach (var theme in Themes)
                    theme.IsSelectedForBatch = selectAll;

                OnPropertyChanged(nameof(IsGroupBatchChecked));
            }
        }

        public void RefreshGroupBatchChecked()
        {
            OnPropertyChanged(nameof(IsGroupBatchChecked));
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
