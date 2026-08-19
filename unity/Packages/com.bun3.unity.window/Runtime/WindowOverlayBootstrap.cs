using Bun3.Unity.Core.PlayerLoop;
using UnityEngine;

namespace Bun3.Unity.Window
{
    /// <summary>
    /// Scene-object-free runtime wiring: after the first scene loads, applies
    /// <see cref="WindowOverlaySettings"/> (if a <c>Resources/Bun3WindowOverlaySettings</c>
    /// asset exists) and inserts one player-loop tick that enforces the always-on-top pin
    /// and evaluates the click-through policy. The tick also re-asserts the pin when the
    /// application regains focus. Runs only where some feature can reach the OS.
    /// </summary>
    internal static class WindowOverlayBootstrap
    {
        /// <summary>Player-loop marker type for the overlay tick.</summary>
        internal static class WindowOverlayTick
        {
        }

        private static float _nextEnforceTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            ApplySettings(Resources.Load<WindowOverlaySettings>(WindowOverlaySettings.ResourceName));

            if (!AlwaysOnTop.IsSupported && !ClickThrough.IsSupported)
            {
                return;
            }

            Application.focusChanged -= OnFocusChanged;
            Application.focusChanged += OnFocusChanged;

            // Re-registering after a previous session (domain reload disabled) is fine:
            // PlayerLoopSystemHelper removed the old tick on Application.quitting.
            if (!PlayerLoopSystemHelper.IsInserted(typeof(WindowOverlayTick)))
            {
                PlayerLoopSystemHelper.InsertSystemBefore(
                    typeof(WindowOverlayTick),
                    Tick,
                    typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
            }
        }

        private static void ApplySettings(WindowOverlaySettings settings)
        {
            if (settings == null)
            {
                return;
            }
            AlwaysOnTop.EnforceIntervalSeconds = settings.EnforceInterval;
            AlwaysOnTop.SetEnabled(settings.AlwaysOnTopEnabled);
            if (settings.TransparencyEnabled)
            {
                WindowTransparency.Apply(Camera.main, settings.PreferredMethod, settings.ColorKey);
            }
            if (settings.HitTest != null)
            {
                ClickThrough.HitTest = settings.HitTest;
            }
            ClickThrough.AutoByPointer = settings.AutoClickThrough;
        }

        private static void Tick()
        {
            if (AlwaysOnTop.IsEnabled && Time.unscaledTime >= _nextEnforceTime)
            {
                _nextEnforceTime = Time.unscaledTime + AlwaysOnTop.EnforceIntervalSeconds;
                AlwaysOnTop.EnforceOnce();
            }
            ClickThrough.TickPolicy();
        }

        private static void OnFocusChanged(bool hasFocus)
        {
            if (hasFocus)
            {
                AlwaysOnTop.EnforceOnce();
            }
        }
    }
}
