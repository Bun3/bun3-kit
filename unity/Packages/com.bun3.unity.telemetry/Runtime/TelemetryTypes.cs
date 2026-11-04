using System;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Label of a record. Analytics stays one bucket; debugging is split by origin. Never used to filter collection.</summary>
    public enum TelemetryCategory : byte
    {
        /// <summary>Product analytics.</summary>
        Analytics,
        /// <summary>UI interaction.</summary>
        Ui,
        /// <summary>Popup open and close.</summary>
        Popup,
        /// <summary>Scene changes.</summary>
        Scene,
        /// <summary>World interaction.</summary>
        Interaction,
        /// <summary>Engine log lines.</summary>
        EngineLog,
        /// <summary>Frequent in-game actions.</summary>
        Activity,
    }

    /// <summary>Delivery tiers a sink can subscribe to.</summary>
    [Flags]
    public enum TelemetryTier : byte
    {
        /// <summary>No tier; the sink receives nothing.</summary>
        None = 0,
        /// <summary>Analytics records.</summary>
        Analytics = 1,
        /// <summary>Debugging records.</summary>
        Debug = 2,
        /// <summary>Frequent in-game action records.</summary>
        Activity = 4,
        /// <summary>Every tier.</summary>
        All = Analytics | Debug | Activity,
    }

    /// <summary>Maps categories to tiers and wire names.</summary>
    public static class TelemetryCategories
    {
        /// <summary>The tier a category is delivered in.</summary>
        public static TelemetryTier Tier(TelemetryCategory c) => c switch
        {
            TelemetryCategory.Analytics => TelemetryTier.Analytics,
            TelemetryCategory.Activity => TelemetryTier.Activity,
            _ => TelemetryTier.Debug,
        };

        /// <summary>The wire name of a tier.</summary>
        public static string TierName(TelemetryTier t) => t switch
        {
            TelemetryTier.Analytics => "analytics",
            TelemetryTier.Activity => "activity",
            _ => "debug",
        };

        /// <summary>The wire name of a category.</summary>
        public static string Name(TelemetryCategory c) => c switch
        {
            TelemetryCategory.Analytics => "analytics",
            TelemetryCategory.Ui => "ui",
            TelemetryCategory.Popup => "popup",
            TelemetryCategory.Scene => "scene",
            TelemetryCategory.Interaction => "interaction",
            TelemetryCategory.EngineLog => "engine_log",
            TelemetryCategory.Activity => "activity",
            _ => "unknown",
        };
    }

    /// <summary>Lets the game fill the common properties it owns. The kit has already set run id, build, environment and scene.</summary>
    public delegate void TelemetryContextFill(ref TelemetryContext context);

    /// <summary>Properties attached to every record.</summary>
    public struct TelemetryContext
    {
        /// <summary>Stable id of the player or install.</summary>
        public string DistinctId;
        /// <summary>Id of this application run.</summary>
        public string RunId;
        /// <summary>Application version.</summary>
        public string Build;
        /// <summary>Environment name.</summary>
        public string Environment;
        /// <summary>Active scene name at record time, or null when it could not be read.</summary>
        public string Scene;
        /// <summary>Language code.</summary>
        public string Language;
        /// <summary>Game-defined extra properties. Entries with a null value are not written.</summary>
        public (string Key, object Value)[] Extra;

        /// <summary>The value of the first <see cref="Extra"/> entry with this key, or null.</summary>
        public object Get(string key) => TelemetryRecord.Find(Extra, key);
    }

    /// <summary>One telemetry record as handed to sinks.</summary>
    public sealed class TelemetryRecord
    {
        /// <summary>Event name.</summary>
        public string Name;
        /// <summary>Record category.</summary>
        public TelemetryCategory Category;
        /// <summary>Record time in UTC.</summary>
        public DateTime Utc;
        /// <summary>Increasing sequence number per hub, starting at 1.</summary>
        public long Seq;
        /// <summary>Common properties.</summary>
        public TelemetryContext Context;
        /// <summary>Event properties.</summary>
        public (string Key, object Value)[] Props;

        /// <summary>The value of the first property with this key, or null.</summary>
        public object Prop(string key) => Find(Props, key);

        internal static object Find((string Key, object Value)[] pairs, string key)
        {
            if (pairs == null) return null;
            foreach (var (k, v) in pairs)
                if (k == key) return v;
            return null;
        }
    }

    /// <summary>A destination for records. The hub calls every member on the main thread only.</summary>
    public interface ITelemetrySink
    {
        /// <summary>True when records leave the device; remote sinks are gated by consent.</summary>
        bool Remote { get; }
        /// <summary>Receives one record.</summary>
        void Write(TelemetryRecord record);
        /// <summary>Periodic update with the current time in seconds.</summary>
        void Tick(double now);
        /// <summary>Completes pending work, waiting at most the timeout.</summary>
        UniTask FlushAsync(TimeSpan timeout);
    }
}
