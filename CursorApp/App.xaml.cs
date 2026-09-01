using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace CursorManager
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;
        private const string MutexName = "CursorManager_SingleInstance_Mutex_2026";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        private const int SW_RESTORE = 9;

        public static string? StartupFolder { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Another instance is already running; bring it to front and exit
                try
                {
                    IntPtr existingHwnd = FindWindow(null, "鼠標一鍵套用器");
                    if (existingHwnd != IntPtr.Zero)
                    {
                        ShowWindowAsync(existingHwnd, SW_RESTORE);
                        SetForegroundWindow(existingHwnd);
                    }
                }
                catch { }

                Shutdown();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                MessageBox.Show("未攔截的錯誤: " + (args.ExceptionObject?.ToString() ?? "未知"), "啟動錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show("UI錯誤: " + args.Exception.Message, "程式錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);

            if (e.Args.Length > 0 && Directory.Exists(e.Args[0]))
            {
                StartupFolder = e.Args[0];
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutex.Dispose();
                }
                catch { }
            }
            base.OnExit(e);
        }
    }
}
