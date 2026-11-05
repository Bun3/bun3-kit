using System;
using GameAnalyticsSDK;
using GameAnalyticsSDK.Events;
using UnityEngine;
using GA = GameAnalyticsSDK.GameAnalytics;
using GASettings = GameAnalyticsSDK.Setup.Settings;

namespace Bun3.Unity.Telemetry.GameAnalytics
{
    /// <summary>
    /// <see cref="IGameAnalyticsApi"/> backed by the GameAnalytics Unity SDK. Needs the SDK settings asset at
    /// <c>Resources/GameAnalytics/Settings</c>; keys, build, dimension values and reporting flags are written into the
    /// loaded settings object at runtime, so the asset itself can stay empty.
    /// </summary>
    public sealed class GameAnalyticsSdkApi : IGameAnalyticsApi
    {
        string environment;

        static GASettings Settings => GA.SettingsGA ?? throw new InvalidOperationException("GameAnalytics settings asset missing at Resources/GameAnalytics/Settings");

        // The SDK picks keys and build by the running platform's entry in the settings lists.
        static int PlatformIndex()
        {
            var settings = Settings;
            var index = settings.Platforms.IndexOf(Application.platform);
            if (index >= 0) return index;
            settings.AddPlatform(Application.platform);
            return settings.Platforms.Count - 1;
        }

        /// <inheritdoc />
        public void SetCustomId(string id) => GA.SetCustomId(id);

        /// <inheritdoc />
        public void SetBuild(string build)
        {
            if (string.IsNullOrEmpty(build)) return;
            var settings = Settings;
            var index = PlatformIndex();
            settings.EnsureBuildNumberAutoDetectList();
            settings.BuildNumberAutoDetect[index] = false;
            settings.Build[index] = build;
        }

        /// <inheritdoc />
        public void SetEnvironment(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            environment = value;
            var allowed = Settings.CustomDimensions01;
            if (!allowed.Contains(value)) allowed.Add(value);
            // Set before Initialize so the session start can carry it; Initialize sets it again (see there).
            GA_Setup.SetAvailableCustomDimensions01(allowed);
            GA.SetCustomDimension01(value);
        }

        /// <inheritdoc />
        public void SetKeys(string gameKey, string secretKey) => GASettings.UpdateKeys(PlatformIndex(), gameKey, secretKey);

        /// <inheritdoc />
        public void DisableErrorReporting()
        {
            var s = Settings;
            s.SubmitErrors = false;
            s.NativeErrorReporting = false;
            s.SubmitFpsAverage = false;
            s.SubmitFpsCritical = false;
            s.EnableFPSHistogram = false;
            s.EnableMemoryHistogram = false;
            s.EnableMemoryTracking = false;
            s.EnableHardwareTracking = false;
            s.EnableSDKInitEvent = false;
            s.InfoLogBuild = false;
        }

        /// <inheritdoc />
        public void Initialize()
        {
            // The component ends the session on quit and must exist once; its Awake keeps it across scenes.
            if (UnityEngine.Object.FindAnyObjectByType<GA>() == null) new GameObject("GameAnalytics").AddComponent<GA>();
            GA.Initialize();
            // Initialize re-applies the allowed dimension values from the settings object; set the value after it too.
            if (environment != null) GA.SetCustomDimension01(environment);
        }

        /// <inheritdoc />
        public void SetEnabled(bool enabled) => GA.SetEnabledEventSubmission(enabled);

        /// <inheritdoc />
        public void Design(string id, float? value)
        {
            if (value is float v) GA.NewDesignEvent(id, v);
            else GA.NewDesignEvent(id);
        }

        /// <inheritdoc />
        public void Progression(GameAnalyticsEventKind status, string p1, string p2, string p3)
        {
            var s = status switch
            {
                GameAnalyticsEventKind.ProgressionStart => GAProgressionStatus.Start,
                GameAnalyticsEventKind.ProgressionComplete => GAProgressionStatus.Complete,
                GameAnalyticsEventKind.ProgressionFail => GAProgressionStatus.Fail,
                _ => throw new ArgumentOutOfRangeException(nameof(status)),
            };
            if (p2 == null) GA.NewProgressionEvent(s, p1);
            else if (p3 == null) GA.NewProgressionEvent(s, p1, p2);
            else GA.NewProgressionEvent(s, p1, p2, p3);
        }
    }
}
