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
        /// Target monitor for the fit. True (default) pins the overlay to the primary
        /// monitor's work area — the OS or Unity can place the window on a secondary
        /// display at startup, and fitting "wherever it happens to be" would keep it
        /// there. False fits the monitor the window currently occupies.
        /// </summary>
        public static bool FitToPrimary { get; set; } = true;

        /// <summary>
        /// Fits the window to the target monitor's work area when it is not already
        /// there. Maintenance-style like the topmost pin: Unity resizes its own window
        /// during startup (and display changes), so a one-shot fit loses the race —
        /// call this periodically instead. A matching window issues no native write at
        /// all, so it is safe alongside cursor-clip-sensitive apps.
        /// </summary>
        public static bool EnsureFitted()
        {
#if BUN3_OVERLAY_SUPPORTED
            var hwnd = GameWindow.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            Win32Native.Rect work;
            if (FitToPrimary)
            {
                if (!Win32Native.SystemParametersInfo(Win32Native.SPI_GETWORKAREA, 0, out work, 0))
                {
                    return false;
                }
            }
            else
            {
                var monitor = Win32Native.MonitorFromWindow(hwnd, Win32Native.MONITOR_DEFAULTTONEAREST);
                var info = new Win32Native.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<Win32Native.MonitorInfo>() };
                if (monitor == IntPtr.Zero || !Win32Native.GetMonitorInfo(monitor, ref info))
                {
                    return false;
                }
                work = info.Work;
            }
            if (Win32Native.GetWindowRect(hwnd, out var current)
                && current.Left == work.Left && current.Top == work.Top
                && current.Right == work.Right && current.Bottom == work.Bottom)
            {
                return true; // already fitted — no write
            }

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
