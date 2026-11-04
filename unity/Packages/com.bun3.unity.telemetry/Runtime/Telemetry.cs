using System;
using System.Collections.Generic;
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

        static readonly List<ITelemetryCapture> captures = new();
        static GameObject host;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Application.quitting -= Quitting;
            captures.Clear();
            host = null;
            Hub = null;
            ConsentChanged = null;
        }

        /// <summary>
        /// Builds the hub and starts the automatic records: engine log, scene changes, UI interaction and the builder's captures.
        /// Also ticks the hub every frame and flushes it on quit. An existing installation is removed first.
        /// </summary>
        public static void Install(TelemetryBuilder builder)
        {
            Uninstall();
            Hub = builder.Build();
            EngineLogCapture.Install(builder.LogPrefixes);
            SceneCapture.Install();
            foreach (var c in builder.Captures)
            {
                c.Install();
                captures.Add(c);
            }
            host = new GameObject("Bun3.Telemetry");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<TelemetryRunner>();
            host.AddComponent<UiInteractionCapture>();
            Application.quitting += Quitting;
        }

        /// <summary>Reverses <see cref="Install"/> and clears the hub.</summary>
        public static void Uninstall()
        {
            Application.quitting -= Quitting;
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            host = null;
            for (var i = captures.Count - 1; i >= 0; i--) captures[i].Uninstall();
            captures.Clear();
            SceneCapture.Uninstall();
            EngineLogCapture.Uninstall();
            Hub = null;
        }

        static void Quitting() => FlushAsync(TimeSpan.FromSeconds(2)).Forget();

        /// <summary>Records an event on the active hub. Call it on the main thread only.</summary>
        public static void Track(TelemetryCategory category, string name, params (string Key, object Value)[] props) =>
            Hub?.Track(category, name, props);

        /// <summary>Flushes every sink of the active hub.</summary>
        public static UniTask FlushAsync(TimeSpan timeout) =>
            Hub != null ? Hub.FlushAsync(timeout) : UniTask.CompletedTask;

        /// <summary>Tells subscribers to re-read the consent answer.</summary>
        public static void NotifyConsentChanged() => ConsentChanged?.Invoke();
    }
}
