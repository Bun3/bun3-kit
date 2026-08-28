#if (UNITY_STANDALONE_WIN && !UNITY_EDITOR) || UNITY_EDITOR_WIN
#define BUN3_TOPMOST_SUPPORTED
#endif

using System;
using UnityEngine;

namespace Bun3.Unity.Window
{
    /// <summary>
    /// Keeps the game window above all non-topmost windows on Windows by pinning it into
    /// the topmost z-order band (<c>HWND_TOPMOST</c>).
    ///
    /// In a Windows standalone player the pin targets the game's own window. In the
    /// Windows editor it targets the editor main window — the whole editor goes topmost,
    /// which is what makes the feature visually testable in play mode — and the pin is
    /// automatically released when play mode exits. On non-Windows platforms every method
    /// is a safe no-op: <see cref="IsEnabled"/> still tracks the requested state so UI
    /// bindings behave identically everywhere, but no native call is made.
    ///
    /// Windows can silently drop or bypass the pin — clicking the taskbar, another app
    /// asserting its own topmost window, or explorer restarts all cause drift — the
    /// package's player-loop tick (see <c>WindowOverlayBootstrap</c>) closes that gap by
    /// calling <see cref="EnforceOnce"/> every <see cref="EnforceIntervalSeconds"/> while
    /// enabled. No scene setup is required.
    ///
    /// All members are main-thread only.
    /// </summary>
    public static class AlwaysOnTop
    {
        /// <summary>Seconds between automatic enforcement checks (unscaled). 0 checks every frame.</summary>
        public static float EnforceIntervalSeconds { get; set; } = 0.25f;

        /// <summary>
        /// While another application confines the cursor with <c>ClipCursor</c> (games
        /// lock the mouse to their window or its center this way), freeze all native
        /// window writes: <c>SetWindowPos</c> and <c>SetWindowLong</c> from a background
        /// window each wipe the clip, letting the pointer escape onto other monitors
        /// mid-game. Nothing is unpinned — the overlay just goes write-silent until the
        /// clip has been released for a grace period. <see cref="IsEnabled"/> is
        /// unaffected. Default on; opt out for overlays that must keep enforcing above
        /// clipping apps.
        /// </summary>
        public static bool YieldToCursorClip { get; set; } = true;

        /// <summary>True while the pin is temporarily held down for a cursor-clipping app.</summary>
        public static bool IsYieldingToCursorClip { get; private set; }

        private const float ClipReleaseGraceSeconds = 1f;
        private static float _clipLastSeenTime;

#if BUN3_TOPMOST_SUPPORTED
        private static IntPtr _lastForeground;
#endif

        /// <summary>True when the pin actually reaches the OS (Windows player or Windows editor).</summary>
        public static bool IsSupported =>
#if BUN3_TOPMOST_SUPPORTED
            true;
#else
            false;
#endif

        /// <summary>The requested state. Tracked on every platform, applied only where supported.</summary>
        public static bool IsEnabled { get; private set; }

        /// <summary>Raised once per state transition, after the new state has been applied.</summary>
        public static event Action<bool> EnabledChanged;

        /// <summary>
        /// Requests always-on-top on or off. Applies <c>HWND_TOPMOST</c>/<c>HWND_NOTOPMOST</c>
        /// immediately where supported, then raises <see cref="EnabledChanged"/>.
        /// Calling with the current state is a no-op.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled)
            {
                return;
            }
            IsEnabled = enabled;
            Apply(enabled);
            EnabledChanged?.Invoke(enabled);
        }

        /// <summary>
        /// Queries the OS for the window's actual <c>WS_EX_TOPMOST</c> style bit.
        /// Always false where unsupported. May disagree with <see cref="IsEnabled"/>
        /// when Windows has dropped the pin — that gap is what enforcement closes.
        /// </summary>
        public static bool IsEffectivelyTopMost()
        {
#if BUN3_TOPMOST_SUPPORTED
            var hwnd = GameWindow.Handle;
            return hwnd != IntPtr.Zero
                && (Win32Native.GetWindowLong(hwnd, Win32Native.GWL_EXSTYLE) & Win32Native.WS_EX_TOPMOST) != 0;
#else
            return false;
#endif
        }

        /// <summary>
        /// Re-asserts the pin if drift is detected: the topmost style bit was lost, the
        /// foreground window changed (another topmost window may now sit above ours), or
        /// the taskbar is foreground. Cheap enough to call every frame. Returns true when
        /// a re-assert was issued and accepted by the OS; false when disabled, unsupported,
        /// or no drift was detected.
        /// </summary>
        public static bool EnforceOnce()
        {
#if BUN3_TOPMOST_SUPPORTED
            if (!IsEnabled)
            {
                return false;
            }
            var hwnd = GameWindow.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            UpdateCursorClipYield();
            if (IsYieldingToCursorClip)
            {
                return false;
            }

            var foreground = Win32Native.GetForegroundWindow();
            var foregroundChanged = foreground != _lastForeground;
            _lastForeground = foreground;

            var lostTopMostBit =
                (Win32Native.GetWindowLong(hwnd, Win32Native.GWL_EXSTYLE) & Win32Native.WS_EX_TOPMOST) == 0;
            var taskbarHwnd = GameWindow.TaskbarHandle;
            var taskbarIsForeground = taskbarHwnd != IntPtr.Zero && foreground == taskbarHwnd;

            if (!lostTopMostBit && !foregroundChanged && !taskbarIsForeground)
            {
                return false;
            }
            return SetTopMost(hwnd, topMost: true);
#else
            return false;
#endif
        }

        /// <summary>
        /// Tracks whether some app currently confines the cursor. While it does, every
        /// native window write must be frozen — releasing the pin, re-pinning, or any
        /// style change each wipe the clip (they are what let the pointer escape a
        /// game's mouse lock in the first place). So the yield does NOTHING: no unpin,
        /// no writes; enforcement and policy simply pause until the clip has been gone
        /// for a grace period. Called every frame by the package tick, and by
        /// <see cref="EnforceOnce"/> for standalone callers.
        /// </summary>
        public static void UpdateCursorClipYield()
        {
#if BUN3_TOPMOST_SUPPORTED
            if (!YieldToCursorClip)
            {
                IsYieldingToCursorClip = false;
                return;
            }

            if (Win32Native.CursorIsClipped())
            {
                _clipLastSeenTime = Time.unscaledTime;
                IsYieldingToCursorClip = true;
            }
            else if (IsYieldingToCursorClip && Time.unscaledTime - _clipLastSeenTime >= ClipReleaseGraceSeconds)
            {
                IsYieldingToCursorClip = false;
                // Stale foreground comparison would fire an instant re-pin; reset it so
                // enforcement resumes from drift checks alone.
                _lastForeground = Win32Native.GetForegroundWindow();
            }
#endif
        }

        private static void Apply(bool topMost)
        {
#if BUN3_TOPMOST_SUPPORTED
            var hwnd = GameWindow.Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetTopMost(hwnd, topMost);
            }
#endif
        }

#if BUN3_TOPMOST_SUPPORTED
        private static bool SetTopMost(IntPtr hwnd, bool topMost)
        {
            return Win32Native.SetWindowPos(
                hwnd,
                topMost ? Win32Native.HWND_TOPMOST : Win32Native.HWND_NOTOPMOST,
                0, 0, 0, 0,
                Win32Native.SWP_NOMOVE | Win32Native.SWP_NOSIZE | Win32Native.SWP_NOACTIVATE);
        }
#endif

#if UNITY_EDITOR
        // A pinned editor must never outlive the play session it was pinned in.
        [UnityEditor.InitializeOnLoadMethod]
        private static void ReleasePinOnPlayModeExit()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                {
                    SetEnabled(false);
                }
            };
        }
#endif
    }
}
