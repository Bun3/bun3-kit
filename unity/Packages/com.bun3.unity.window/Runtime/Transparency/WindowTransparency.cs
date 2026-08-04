#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
#define BUN3_OVERLAY_SUPPORTED
#endif

using System;
using UnityEngine;

namespace Bun3.Unity.Window
{
    /// <summary>
    /// Makes the pixels the game does not draw show the desktop behind the window:
    /// removes the border (<c>WS_POPUP</c>), extends the DWM glass frame over the whole
    /// client area, and clears the camera to alpha zero — or, as a fallback, marks one
    /// key color as fully transparent (<c>LWA_COLORKEY</c>).
    ///
    /// Windows standalone players only. In the editor and on other platforms
    /// <see cref="Apply"/> is a safe no-op returning <see cref="TransparencyMethod.None"/>
    /// — a transparent editor window would be broken UX, so visual verification happens
    /// in builds (see the package sample's checklist).
    ///
    /// Apply-once: the first successful <see cref="Apply"/> decides the method for the
    /// process lifetime; switching methods requires an app restart. Main-thread only.
    /// </summary>
    public static class WindowTransparency
    {
        /// <summary>True when transparency can reach the OS (Windows player, never the editor).</summary>
        public static bool IsSupported =>
#if BUN3_OVERLAY_SUPPORTED
            true;
#else
            false;
#endif

        /// <summary>The mechanism actually active. <see cref="TransparencyMethod.None"/> until applied.</summary>
        public static TransparencyMethod ActiveMethod { get; private set; }

        /// <summary>Raised once, after a successful <see cref="Apply"/>.</summary>
#pragma warning disable 0067 // never raised where transparency cannot reach the OS
        public static event Action<TransparencyMethod> Applied;
#pragma warning restore 0067

        /// <summary>
        /// Applies window transparency and configures <paramref name="camera"/> to match
        /// (solid clear color: alpha zero for DWM, the key color for color key).
        /// Returns the method now active; on a second call returns the already-active
        /// method without touching anything. <see cref="TransparencyMethod.None"/> means
        /// unsupported platform, missing camera/window, or total failure.
        /// </summary>
        public static TransparencyMethod Apply(Camera camera, TransparencyPreference preferred, Color colorKey)
        {
#if BUN3_OVERLAY_SUPPORTED
            if (ActiveMethod != TransparencyMethod.None)
            {
                return ActiveMethod;
            }
            var hwnd = GameWindow.Handle;
            if (hwnd == IntPtr.Zero || camera == null)
            {
                return TransparencyMethod.None;
            }

            var dwmOk = ApplyBorderlessDwmBase(hwnd);
            TransparencyMethod method;
            if (preferred != TransparencyPreference.ColorKey && dwmOk)
            {
                ConfigureCamera(camera, Color.clear);
                method = TransparencyMethod.Dwm;
            }
            else
            {
                ConfigureCamera(camera, colorKey);
                ApplyColorKey(hwnd, colorKey);
                method = TransparencyMethod.ColorKey;
            }

            ActiveMethod = method;
            Applied?.Invoke(method);
            return method;
#else
            return TransparencyMethod.None;
#endif
        }

#if BUN3_OVERLAY_SUPPORTED
        /// <summary>
        /// Borderless style + DWM glass over the whole client area. Both methods build on
        /// this (BongoCat does the same); returns whether the DWM extend itself succeeded,
        /// which decides Dwm vs ColorKey.
        /// </summary>
        private static bool ApplyBorderlessDwmBase(IntPtr hwnd)
        {
            Win32Native.SetWindowLongPtr(
                hwnd, Win32Native.GWL_STYLE, new IntPtr(Win32Native.WS_POPUP | Win32Native.WS_VISIBLE));

            var margins = new Win32Native.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            var hr = Win32Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // The style change must be committed even when DWM fails (color-key path
            // keeps the borderless look).
            if (Win32Native.GetWindowRect(hwnd, out var rect))
            {
                Win32Native.SetWindowPos(
                    hwnd, IntPtr.Zero,
                    rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top,
                    Win32Native.SWP_FRAMECHANGED | Win32Native.SWP_SHOWWINDOW | Win32Native.SWP_NOZORDER);
            }

            if (hr != 0)
            {
                Debug.LogWarning($"WindowTransparency | DwmExtendFrameIntoClientArea failed (hr=0x{hr:X8}), falling back to color key.");
                return false;
            }
            return true;
        }

        private static void ApplyColorKey(IntPtr hwnd, Color colorKey)
        {
            var exStyle = (long)Win32Native.GetWindowLongPtr(hwnd, Win32Native.GWL_EXSTYLE);
            Win32Native.SetWindowLongPtr(
                hwnd, Win32Native.GWL_EXSTYLE, new IntPtr(exStyle | Win32Native.WS_EX_LAYERED));
            Win32Native.SetLayeredWindowAttributes(hwnd, ToColorRef(colorKey), 255, Win32Native.LWA_COLORKEY);
        }

        private static void ConfigureCamera(Camera camera, Color clearColor)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = clearColor;
        }

        // COLORREF is 0x00BBGGRR.
        private static uint ToColorRef(Color color)
        {
            Color32 c = color;
            return (uint)(c.r | (c.g << 8) | (c.b << 16));
        }
#endif
    }
}
