namespace Bun3.Unity.Telemetry.GameAnalytics
{
    /// <summary>Builder extension that attaches <see cref="GameAnalyticsSink"/>.</summary>
    public static class TelemetryBuilderGameAnalyticsExtensions
    {
        /// <summary>
        /// Attaches a <see cref="GameAnalyticsSink"/> when the environment has <see cref="GameAnalyticsSinkOptions"/> with
        /// both keys and at least one tier. Consent changes are read from the installed hub's <see cref="TelemetryHub.RemoteAllowed"/>.
        /// </summary>
        /// <param name="builder">The builder.</param>
        /// <param name="map">The game's mapping, or null to send every record as a design event named after it.</param>
        /// <param name="api">The SDK calls, or null for the GameAnalytics Unity SDK.</param>
        public static TelemetryBuilder AddGameAnalytics(this TelemetryBuilder builder, GameAnalyticsMap map = null, IGameAnalyticsApi api = null)
        {
            var options = builder.Environment?.Get<GameAnalyticsSinkOptions>();
            if (options == null || string.IsNullOrEmpty(options.GameKey) || string.IsNullOrEmpty(options.SecretKey)) return builder;
            var sink = new GameAnalyticsSink(options.GameKey, options.SecretKey, map, api ?? new GameAnalyticsSdkApi(), () => Telemetry.Hub?.RemoteAllowed ?? false);
            return builder.Add(sink, options.Tiers);
        }
    }
}
