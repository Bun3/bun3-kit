using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Assembles a <see cref="TelemetryHub"/>. Sinks are attached one line at a time.</summary>
    public sealed class TelemetryBuilder
    {
        readonly List<(ITelemetrySink Sink, TelemetryTier Tiers)> sinks = new();
        readonly List<string> dropKeys = new();
        readonly List<string> logPrefixes = new();
        readonly List<ITelemetryCapture> captures = new();
        TelemetryContextFill fill;
        Func<bool> consent;
        string runId;
        Func<DateTime> utcNow = () => DateTime.UtcNow;
        Func<string> sceneName = () => SceneManager.GetActiveScene().name;

        /// <summary>Creates a builder for an environment. A null environment has no name and no sink options.</summary>
        public TelemetryBuilder(TelemetryEnvironmentOptions environment) => Environment = environment;

        /// <summary>The environment options this builder reads sink options from, or null.</summary>
        public TelemetryEnvironmentOptions Environment { get; }

        /// <summary>Sets the game callback that fills the common properties the kit does not own.</summary>
        public TelemetryBuilder Context(TelemetryContextFill fill)
        {
            this.fill = fill;
            return this;
        }

        /// <summary>Sets the callback that answers whether remote sinks may receive records. Without it, consent is assumed.</summary>
        public TelemetryBuilder Consent(Func<bool> consent)
        {
            this.consent = consent;
            return this;
        }

        /// <summary>Lists property keys that are removed from every record, including the context extras.</summary>
        public TelemetryBuilder DropKeys(params string[] keys)
        {
            if (keys != null) dropKeys.AddRange(keys);
            return this;
        }

        /// <summary>Lists log line prefixes that are recorded as log_line whatever their log level.</summary>
        public TelemetryBuilder EngineLogPrefixes(params string[] prefixes)
        {
            if (prefixes != null) logPrefixes.AddRange(prefixes);
            return this;
        }

        /// <summary>Adds a record source that <see cref="Telemetry.Install"/> installs and uninstalls with the hub. A null capture adds nothing.</summary>
        public TelemetryBuilder Capture(ITelemetryCapture capture)
        {
            if (capture != null) captures.Add(capture);
            return this;
        }

        /// <summary>Sets the run id. A missing or empty id becomes a new GUID.</summary>
        public TelemetryBuilder RunId(string runId)
        {
            this.runId = runId;
            return this;
        }

        /// <summary>Attaches a sink for the given tiers. A null sink or the None tier attaches nothing.</summary>
        public TelemetryBuilder Add(ITelemetrySink sink, TelemetryTier tiers)
        {
            if (sink != null && tiers != TelemetryTier.None) sinks.Add((sink, tiers));
            return this;
        }

        /// <summary>Attaches a <see cref="ConsoleSink"/> when the environment has console options with at least one tier.</summary>
        public TelemetryBuilder AddConsole()
        {
            var options = Environment?.Get<ConsoleSinkOptions>();
            return options == null ? this : Add(new ConsoleSink(), options.Tiers);
        }

        /// <summary>Creates the hub. Reads the application version once.</summary>
        public TelemetryHub Build() => new(
            string.IsNullOrEmpty(runId) ? Guid.NewGuid().ToString("N") : runId,
            Application.version,
            Environment?.Name,
            sceneName,
            fill,
            consent,
            utcNow,
            dropKeys.ToArray(),
            sinks);

        internal string[] LogPrefixes => logPrefixes.ToArray();

        internal IReadOnlyList<ITelemetryCapture> Captures => captures;

        internal TelemetryBuilder WithClock(Func<DateTime> utcNow)
        {
            this.utcNow = utcNow;
            return this;
        }

        internal TelemetryBuilder WithScene(Func<string> sceneName)
        {
            this.sceneName = sceneName;
            return this;
        }
    }
}
