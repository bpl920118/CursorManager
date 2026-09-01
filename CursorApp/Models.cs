using System;
using System.ComponentModel;
using System.Windows.Media;

namespace CursorManager
{
    public class CursorSlot : INotifyPropertyChanged
    {
        public string KeyName { get; set; } = string.Empty;       // Registry Key Name, e.g. "Arrow"
        public string DisplayName { get; set; } = string.Empty;   // Chinese Name, e.g. "標準選擇 (Normal)"
        public string EnglishName { get; set; } = string.Empty;   // e.g. "Normal Select"
        public string TagKeyword { get; set; } = string.Empty;    // Default Tag, e.g. "nomal select"
        public int Order { get; set; }

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

        public string GroupDisplay => IsTemporary ? "未存入庫" : Group;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public override string ToString() => Name;
    }
}
