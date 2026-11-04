using System;

namespace Bun3.Unity.Telemetry.Sentry.Tests
{
    sealed class TestSentryConfiguration : TelemetrySentryConfiguration
    {
        public TelemetryEnvironmentOptions Env;
        public bool Granted;
        public bool Throw;

        protected override TelemetryEnvironmentOptions Environment(bool building) =>
            Throw ? throw new InvalidOperationException("boom") : Env;

        protected override bool Consent() => Granted;
    }
}
