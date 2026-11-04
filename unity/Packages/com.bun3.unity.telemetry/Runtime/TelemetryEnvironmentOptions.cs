using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Sink options for one environment, authored in the game's own settings.</summary>
    [Serializable]
    public sealed class TelemetryEnvironmentOptions
    {
        /// <summary>Environment name written to every record.</summary>
        public string Name;

        /// <summary>One options entry per sink.</summary>
        [SerializeReference, SubclassSelector] public List<TelemetrySinkOptions> Sinks = new();

        /// <summary>The first options entry of this type, or null.</summary>
        public T Get<T>() where T : TelemetrySinkOptions
        {
            if (Sinks == null) return null;
            for (var i = 0; i < Sinks.Count; i++)
                if (Sinks[i] is T t) return t;
            return null;
        }
    }

    /// <summary>Base of the per-sink options.</summary>
    [Serializable]
    public abstract class TelemetrySinkOptions
    {
        /// <summary>Tiers the sink receives. None keeps the sink detached.</summary>
        public TelemetryTier Tiers;
    }

    /// <summary>Options of <see cref="ConsoleSink"/>.</summary>
    [Serializable]
    public sealed class ConsoleSinkOptions : TelemetrySinkOptions { }
}
