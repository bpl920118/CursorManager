using System;
using System.Windows;

namespace CursorManager
{
    /// <summary>
    /// Base dialog that stays above its owner only while the owner window is active.
    /// </summary>
    public class ThemedDialogWindow : Window
    {
        private Window? _ownerWindow;
        private EventHandler? _ownerActivatedHandler;
        private EventHandler? _ownerDeactivatedHandler;

        protected void AttachToOwner(Window? owner)
        {
            if (owner == null)
                return;

            _ownerWindow = owner;
            Owner = owner;
            ShowInTaskbar = false;

            if (WindowStartupLocation == WindowStartupLocation.Manual)
                WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _ownerActivatedHandler = (_, _) => SyncTopmostWithOwner();
            _ownerDeactivatedHandler = (_, _) => Topmost = false;

            owner.Activated += _ownerActivatedHandler;
            owner.Deactivated += _ownerDeactivatedHandler;

            Activated += OnDialogActivated;
            Deactivated += OnDialogDeactivated;
            Loaded += (_, _) => SyncTopmostWithOwner();
            Closed += OnDialogClosed;
        }

        private void OnDialogActivated(object? sender, EventArgs e)
        {
            SyncTopmostWithOwner();
        }

        private void OnDialogDeactivated(object? sender, EventArgs e)
        {
            Topmost = false;
        }

        private void SyncTopmostWithOwner()
        {
            Topmost = _ownerWindow?.IsActive == true;
        }

        private void OnDialogClosed(object? sender, EventArgs e)
        {
            if (_ownerWindow != null)
            {
                if (_ownerActivatedHandler != null)
                    _ownerWindow.Activated -= _ownerActivatedHandler;
                if (_ownerDeactivatedHandler != null)
                    _ownerWindow.Deactivated -= _ownerDeactivatedHandler;
            }

            Activated -= OnDialogActivated;
            Deactivated -= OnDialogDeactivated;
            Closed -= OnDialogClosed;
        }
    }
}
