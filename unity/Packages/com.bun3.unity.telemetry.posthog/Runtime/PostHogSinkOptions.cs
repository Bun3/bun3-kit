using System;

namespace Bun3.Unity.Telemetry.PostHog
{
    /// <summary>Options of <see cref="PostHogSink"/>.</summary>
    [Serializable]
    public sealed class PostHogSinkOptions : TelemetrySinkOptions
    {
        /// <summary>PostHog ingestion host.</summary>
        public string Host = "https://eu.i.posthog.com";

        /// <summary>PostHog project API key. An empty key keeps the sink detached.</summary>
        public string Key;
    }
}
