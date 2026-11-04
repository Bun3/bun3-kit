using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Records active scene changes.</summary>
    static class SceneCapture
    {
        static string last;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Uninstall();

        internal static void Install()
        {
            Uninstall();
            last = SceneManager.GetActiveScene().name;
            SceneManager.activeSceneChanged += Changed;
        }

        internal static void Uninstall()
        {
            SceneManager.activeSceneChanged -= Changed;
            last = null;
        }

        // The previous scene argument may already be invalid, so the remembered name is used.
        static void Changed(Scene previous, Scene next) => OnChanged(last, next.name);

        internal static void OnChanged(string from, string to)
        {
            last = to;
            Telemetry.Track(TelemetryCategory.Scene, "scene_changed", ("from", from), ("to", to));
        }
    }
}
