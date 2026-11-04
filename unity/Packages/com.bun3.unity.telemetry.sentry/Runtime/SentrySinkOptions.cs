using System;

namespace Bun3.Unity.Telemetry.Sentry
{
    /// <summary>Options of <see cref="SentrySink"/>.</summary>
    [Serializable]
    public sealed class SentrySinkOptions : TelemetrySinkOptions
    {
        /// <summary>Sentry DSN. An empty DSN keeps the sink detached and the SDK disabled.</summary>
        public string Dsn;

        /// <summary>Enables the SDK debug logging.</summary>
        public bool Debug;
    }
}
