namespace Bun3.Unity.Telemetry.PostHog
{
    /// <summary>Builder extension that attaches <see cref="PostHogSink"/>.</summary>
    public static class TelemetryBuilderPostHogExtensions
    {
        /// <summary>Attaches a <see cref="PostHogSink"/> when the environment has PostHog options with a key and at least one tier. Sends 20 records or every 10 seconds, keeping at most 500 queued.</summary>
        /// <param name="builder">The builder.</param>
        /// <param name="transport">The HTTP transport, or null for UnityWebRequest.</param>
        public static TelemetryBuilder AddPostHog(this TelemetryBuilder builder, IPostHogTransport transport = null)
        {
            var options = builder.Environment?.Get<PostHogSinkOptions>();
            if (options == null || string.IsNullOrEmpty(options.Key)) return builder;
            var host = string.IsNullOrEmpty(options.Host) ? new PostHogSinkOptions().Host : options.Host;
            return builder.Add(new PostHogSink(host, options.Key, 20, 10f, 500, transport ?? new UnityWebRequestPostHogTransport()), options.Tiers);
        }
    }
}
