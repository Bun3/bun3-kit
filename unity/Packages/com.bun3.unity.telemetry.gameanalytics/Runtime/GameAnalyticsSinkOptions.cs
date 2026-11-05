using System;

namespace Bun3.Unity.Telemetry.GameAnalytics
{
    /// <summary>Options of <see cref="GameAnalyticsSink"/>. Defaults to the analytics tier.</summary>
    [Serializable]
    public sealed class GameAnalyticsSinkOptions : TelemetrySinkOptions
    {
        /// <summary>GameAnalytics game key. An empty key keeps the sink detached.</summary>
        public string GameKey;

        /// <summary>GameAnalytics secret key. An empty key keeps the sink detached.</summary>
        public string SecretKey;

        /// <summary>Creates options that receive the analytics tier.</summary>
        public GameAnalyticsSinkOptions() => Tiers = TelemetryTier.Analytics;
    }
}
