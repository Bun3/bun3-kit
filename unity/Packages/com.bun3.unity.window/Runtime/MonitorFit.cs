#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
#define BUN3_OVERLAY_SUPPORTED
#endif

using System;

namespace Bun3.Unity.Window
{
    /// <summary>
    /// Sizes the game window to its monitor's work area (excludes the taskbar), the
    /// standard shape for a desktop overlay. Win32-only resize on purpose:
    /// <c>Screen.SetResolution</c> recreates the render surface at a nondeterministic
    /// time and wipes the DWM transparency state — in windowed mode Unity follows the
    /// window's client size by itself. Windows standalone players only; call again on
    /// monitor/work-area changes (e.g. taskbar moved).
    /// </summary>
    public static class MonitorFit
    {
        /// <summary>True when the resize actually reaches the OS (Windows player, never the editor).</summary>
        public static bool IsSupported =>
#if BUN3_OVERLAY_SUPPORTED
            true;
#else
            false;
#endif

        /// <summary>
        /// Fits the window to the work area of the monitor it currently occupies.
        /// Returns false when the window handle or monitor cannot be resolved yet —
        /// safe to retry next frame.
        /// </summary>
        public static bool FitToWorkArea()
        {
#if BUN3_OVERLAY_SUPPORTED
            var hwnd = GameWindow.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var monitor = Win32Native.MonitorFromWindow(hwnd, Win32Native.MONITOR_DEFAULTTONEAREST);
            var info = new Win32Native.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.MonitorInfo>() };
            if (monitor == IntPtr.Zero || !Win32Native.GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            var work = info.Work;
            return Win32Native.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top,
                Win32Native.SWP_NOZORDER | Win32Native.SWP_FRAMECHANGED | Win32Native.SWP_SHOWWINDOW | Win32Native.SWP_NOACTIVATE);
#else
            return false;
#endif
        }
    }
}
