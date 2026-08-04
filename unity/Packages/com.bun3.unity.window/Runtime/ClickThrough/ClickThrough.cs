#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
#define BUN3_OVERLAY_SUPPORTED
#endif

using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Bun3.Unity.Window
{
    /// <summary>
    /// Makes mouse events pass through the game window to whatever is behind it
    /// (<c>WS_EX_TRANSPARENT</c>).
    ///
    /// Two layers in one facade: <see cref="SetEnabled"/> is the raw, cached toggle, and
    /// the policy properties (<see cref="ForceClickThrough"/>, <see cref="AutoByPointer"/>,
    /// <see cref="HitTest"/>) feed the package's player-loop tick, which computes
    /// <c>Force || (Auto &amp;&amp; pointer not over interactive content)</c> every frame.
    /// With both policy switches off the tick leaves the state alone — manual
    /// <see cref="SetEnabled"/> control.
    ///
    /// Windows standalone players only. Never applied in the editor: a click-through
    /// editor window could no longer be clicked at all. On unsupported platforms
    /// <see cref="IsEnabled"/> still tracks the requested state.
    ///
    /// While enabled the window cannot regain focus by being clicked — keyboard-driven
    /// features must not assume focus. Main-thread only.
    /// </summary>
    public static class ClickThrough
    {
        /// <summary>True when the toggle actually reaches the OS (Windows player, never the editor).</summary>
        public static bool IsSupported =>
#if BUN3_OVERLAY_SUPPORTED
            true;
#else
            false;
#endif

        /// <summary>The requested state. Tracked on every platform, applied only where supported.</summary>
        public static bool IsEnabled { get; private set; }

        /// <summary>Raised once per state transition, after the new state has been applied.</summary>
        public static event Action<bool> EnabledChanged;

        /// <summary>Unconditional click-through regardless of pointer position ("gaming mode").</summary>
        public static bool ForceClickThrough { get; set; }

        /// <summary>Per-frame pointer polling on/off. Both this and force off = manual control.</summary>
        public static bool AutoByPointer { get; set; }

        /// <summary>The hit test consulted in auto mode. Null is treated as "nothing is hit".</summary>
        public static IPointerHitTest HitTest { get; set; } = new EventSystemHitTest();

        // Test seam; defaults to the real pointer.
        internal static Func<Vector2> PointerPositionSource { get; set; } = ReadPointerPosition;

        /// <summary>
        /// Turns click-through on or off. Calling with the current state is a no-op —
        /// safe to invoke every frame without redundant native style writes.
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
        /// Evaluates the policy and pushes the result — called by the package's
        /// player-loop tick; public for callers scheduling it themselves.
        /// Does nothing when both policy switches are off.
        /// </summary>
        public static void TickPolicy()
        {
            if (!ForceClickThrough && !AutoByPointer)
            {
                return;
            }
            SetEnabled(ComputePolicy());
        }

        internal static bool ComputePolicy()
        {
            if (ForceClickThrough)
            {
                return true;
            }
            if (!AutoByPointer)
            {
                return false;
            }
            var overInteractive = HitTest != null && HitTest.IsHit(PointerPositionSource());
            return !overInteractive;
        }

        private static Vector2 ReadPointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        private static void Apply(bool enabled)
        {
#if BUN3_OVERLAY_SUPPORTED
            var hwnd = GameWindow.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            var exStyle = (long)Win32Native.GetWindowLongPtr(hwnd, Win32Native.GWL_EXSTYLE);
            if (enabled)
            {
                exStyle |= Win32Native.WS_EX_TRANSPARENT | Win32Native.WS_EX_LAYERED;
            }
            else
            {
                exStyle &= ~Win32Native.WS_EX_TRANSPARENT;
                // WS_EX_LAYERED must survive when color-key transparency owns it.
                if (WindowTransparency.ActiveMethod != TransparencyMethod.ColorKey)
                {
                    exStyle &= ~Win32Native.WS_EX_LAYERED;
                }
            }
            Win32Native.SetWindowLongPtr(hwnd, Win32Native.GWL_EXSTYLE, new IntPtr(exStyle));
#endif
        }

#if UNITY_EDITOR
        // Keep tracked state and policy switches from leaking across play sessions when
        // domain reload is disabled.
        [UnityEditor.InitializeOnLoadMethod]
        private static void ResetOnPlayModeExit()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                {
                    ForceClickThrough = false;
                    AutoByPointer = false;
                    SetEnabled(false);
                }
            };
        }
#endif
    }
}
