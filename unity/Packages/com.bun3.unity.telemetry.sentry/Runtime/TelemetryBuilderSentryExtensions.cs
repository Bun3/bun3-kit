using System.Collections.Generic;

namespace Bun3.Unity.Telemetry.Sentry
{
    /// <summary>Builder extension that attaches <see cref="SentrySink"/>.</summary>
    public static class TelemetryBuilderSentryExtensions
    {
        /// <summary>Attaches a <see cref="SentrySink"/> when the environment has Sentry options with a DSN and at least one tier.</summary>
        /// <param name="builder">The builder.</param>
        /// <param name="tagKeys">Keys of <see cref="TelemetryContext.Extra"/> mirrored as Sentry tags, or null.</param>
        /// <param name="hook">Optional per-record handler, or null.</param>
        /// <param name="api">The Sentry API, or null for the SDK.</param>
        public static TelemetryBuilder AddSentry(this TelemetryBuilder builder, IReadOnlyList<string> tagKeys = null,
            SentryRecordHook hook = null, ISentryApi api = null)
        {
            var options = builder.Environment?.Get<SentrySinkOptions>();
            if (options == null || string.IsNullOrEmpty(options.Dsn)) return builder;
            return builder.Add(new SentrySink(api ?? new SentryApi(), tagKeys, hook), options.Tiers);
        }
    }
}
