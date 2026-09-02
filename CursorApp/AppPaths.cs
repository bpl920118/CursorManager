using System;
using System.IO;

namespace CursorManager
{
    /// <summary>
    /// Resolves writable paths for config and user data.
    /// Portable runs keep files next to the exe; installed runs use %AppData%\CursorManager.
    /// </summary>
    public static class AppPaths
    {
        private const string AppFolderName = "CursorManager";
        private const string ConfigFileName = "config.ini";
        private const string CursorsDataFolderName = "CursorsData";

        private static bool _initialized;
        private static string? _dataRoot;

        public static string InstallDirectory { get; } = NormalizeDir(AppDomain.CurrentDomain.BaseDirectory);

        public static string DataRoot
        {
            get
            {
                if (_dataRoot == null)
                    _dataRoot = IsDirectoryWritable(InstallDirectory)
                        ? InstallDirectory
                        : NormalizeDir(Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            AppFolderName));
                return _dataRoot;
            }
        }

        public static bool IsInstalledMode =>
            !string.Equals(InstallDirectory, DataRoot, StringComparison.OrdinalIgnoreCase);

        public static string ConfigFilePath => Path.Combine(DataRoot, ConfigFileName);

        public static string DefaultCursorsDataFolder => Path.Combine(DataRoot, CursorsDataFolderName);

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                Directory.CreateDirectory(DataRoot);
            }
            catch { }

            if (!IsInstalledMode)
                return;

            MigrateFile(Path.Combine(InstallDirectory, ConfigFileName), ConfigFilePath);

            string legacyData = Path.Combine(InstallDirectory, CursorsDataFolderName);
            if (Directory.Exists(legacyData) && !Directory.Exists(DefaultCursorsDataFolder))
            {
                try
                {
                    CopyDirectory(legacyData, DefaultCursorsDataFolder);
                }
                catch { }
            }
        }

        public static string? FindLegacyConfigPath()
        {
            string legacy = Path.Combine(InstallDirectory, ConfigFileName);
            return File.Exists(legacy) ? legacy : null;
        }

        private static string NormalizeDir(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsDirectoryWritable(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return false;

            try
            {
                string probe = Path.Combine(directory, $".write_probe_{Guid.NewGuid():N}");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void MigrateFile(string source, string destination)
        {
            if (!File.Exists(source) || File.Exists(destination))
                return;

            try
            {
                File.Copy(source, destination);
            }
            catch { }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                if (!File.Exists(destFile))
                    File.Copy(file, destFile);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
            }
        }
    }
}
