#if (UNITY_STANDALONE_WIN && !UNITY_EDITOR) || UNITY_EDITOR_WIN
#define BUN3_WIN_NATIVE
#endif

#if BUN3_WIN_NATIVE
using System;

namespace Bun3.Unity.Window
{
    /// <summary>
    /// Resolves and caches the window handle every feature in this package operates on:
    /// the game's own window in the player, the editor main window in the editor.
    /// Main-thread only. The handle is re-resolved when it goes stale (window recreated).
    /// </summary>
    internal static class GameWindow
    {
        private static IntPtr _hwnd;
        private static IntPtr _taskbarHwnd;

        public static IntPtr Handle
        {
            get
            {
                if (_hwnd != IntPtr.Zero && Win32Native.IsWindow(_hwnd))
                {
                    return _hwnd;
                }
#if UNITY_EDITOR
                // The editor process owns many top-level windows (tooltips, utility panels),
                // so thread enumeration is unreliable there; the process main window is the
                // editor main window. Fall back to the focused window of the main thread.
                _hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (_hwnd == IntPtr.Zero)
                {
                    _hwnd = Win32Native.GetActiveWindow();
                }
#else
                _hwnd = Win32Native.FindOwnTopLevelWindow();
#endif
                if (_hwnd != IntPtr.Zero && _taskbarHwnd == IntPtr.Zero)
                {
                    _taskbarHwnd = Win32Native.FindWindow("Shell_TrayWnd", null);
                }
                return _hwnd;
            }
        }

        /// <summary>Taskbar handle, resolved lazily alongside <see cref="Handle"/>. May be zero.</summary>
        public static IntPtr TaskbarHandle => _taskbarHwnd;
    }
}
#endif
