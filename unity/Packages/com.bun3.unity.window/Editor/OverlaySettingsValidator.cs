using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bun3.Unity.Window.Editor
{
    /// <summary>
    /// Transparency silently fails (opaque or black window) when player settings
    /// disagree with what a desktop overlay needs. This turns those misconfigurations
    /// into actionable warnings: on demand via the menu, and automatically before
    /// Windows builds.
    /// </summary>
    public static class OverlaySettingsValidator
    {
        internal struct Snapshot
        {
            public FullScreenMode FullScreenMode;
            public bool RunInBackground;
            public bool UseFlipModelSwapchain;
            public bool AllowHdrDisplaySupport;
            public bool ResizableWindow;
            public GraphicsDeviceType FirstGraphicsApi;
        }

        internal static List<string> CollectWarnings(in Snapshot s)
        {
            var warnings = new List<string>();
            if (s.FullScreenMode != FullScreenMode.Windowed)
            {
                warnings.Add(
                    $"Fullscreen Mode is {s.FullScreenMode}; overlays need Windowed — exclusive fullscreen bypasses DWM composition entirely.");
            }
            if (!s.RunInBackground)
            {
                warnings.Add(
                    "Run In Background is off; an overlay must keep animating while other apps have focus.");
            }
            if (s.FirstGraphicsApi != GraphicsDeviceType.Direct3D11)
            {
                warnings.Add(
                    $"First graphics API is {s.FirstGraphicsApi}; per-pixel (DWM) transparency is only verified on Direct3D11. Other APIs may need the ColorKey method.");
            }
            if (s.UseFlipModelSwapchain)
            {
                warnings.Add(
                    "DXGI Flip Model Swapchain is on; flip-model presentation is believed to discard the alpha channel before DWM composition, breaking per-pixel transparency. Disable it (Player > Resolution and Presentation) or use the ColorKey method.");
            }
            if (s.AllowHdrDisplaySupport)
            {
                warnings.Add(
                    "HDR Display Output is allowed; HDR swapchains do not compose with per-pixel window transparency.");
            }
            if (s.ResizableWindow)
            {
                warnings.Add(
                    "Resizable Window is on; the borderless overlay window has no resize affordance and unexpected resizes distort the overlay (recommended off).");
            }
            return warnings;
        }

        [MenuItem("Bun3/Window/Validate Overlay Settings")]
        public static void Validate()
        {
            var warnings = CollectWarnings(Capture());
            if (warnings.Count == 0)
            {
                Debug.Log("OverlaySettingsValidator | All overlay-relevant player settings look good.");
                return;
            }
            foreach (var warning in warnings)
            {
                Debug.LogWarning("OverlaySettingsValidator | " + warning);
            }
        }

        private static Snapshot Capture()
        {
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
            return new Snapshot
            {
                FullScreenMode = PlayerSettings.fullScreenMode,
                RunInBackground = PlayerSettings.runInBackground,
                UseFlipModelSwapchain = PlayerSettings.useFlipModelSwapchain,
                AllowHdrDisplaySupport = PlayerSettings.allowHDRDisplaySupport,
                ResizableWindow = PlayerSettings.resizableWindow,
                FirstGraphicsApi = apis.Length > 0 ? apis[0] : GraphicsDeviceType.Null,
            };
        }

        private sealed class BuildCheck : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                if (report.summary.platform != BuildTarget.StandaloneWindows64
                    && report.summary.platform != BuildTarget.StandaloneWindows)
                {
                    return;
                }
                foreach (var warning in CollectWarnings(Capture()))
                {
                    Debug.LogWarning("OverlaySettingsValidator | " + warning);
                }
            }
        }
    }
}
