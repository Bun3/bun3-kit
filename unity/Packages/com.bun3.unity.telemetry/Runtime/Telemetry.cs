using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Record entry point. Every call is ignored while no hub is installed.</summary>
    public static class Telemetry
    {
        /// <summary>The active hub, or null.</summary>
        public static TelemetryHub Hub { get; set; }

        /// <summary>Raised when the consent answer may have changed.</summary>
        public static event Action ConsentChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Hub = null;
            ConsentChanged = null;
        }

        /// <summary>Records an event on the active hub.</summary>
        public static void Track(TelemetryCategory category, string name, params (string Key, object Value)[] props) =>
            Hub?.Track(category, name, props);

        /// <summary>Flushes every sink of the active hub.</summary>
        public static UniTask FlushAsync(TimeSpan timeout) =>
            Hub != null ? Hub.FlushAsync(timeout) : UniTask.CompletedTask;

        /// <summary>Tells subscribers to re-read the consent answer.</summary>
        public static void NotifyConsentChanged() => ConsentChanged?.Invoke();
    }
}
