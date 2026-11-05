using System;
using System.Collections.Generic;

namespace Bun3.Unity.Telemetry.GameAnalytics
{
    /// <summary>The kind of a GameAnalytics event.</summary>
    public enum GameAnalyticsEventKind : byte
    {
        /// <summary>A design event: an id of 1 to 5 colon-separated segments and an optional value.</summary>
        Design,
        /// <summary>A progression start: an id of 1 to 3 colon-separated segments.</summary>
        ProgressionStart,
        /// <summary>A progression completion.</summary>
        ProgressionComplete,
        /// <summary>A progression failure.</summary>
        ProgressionFail,
    }

    /// <summary>One GameAnalytics event produced from a telemetry record.</summary>
    public readonly struct GameAnalyticsEvent
    {
        /// <summary>The event kind.</summary>
        public readonly GameAnalyticsEventKind Kind;
        /// <summary>Colon-separated id: 1 to 5 segments for design events, 1 to 3 for progression events.</summary>
        public readonly string Id;
        /// <summary>The numeric value of a design event, or null. Ignored for progression events.</summary>
        public readonly double? Value;

        GameAnalyticsEvent(GameAnalyticsEventKind kind, string id, double? value)
        {
            Kind = kind;
            Id = id;
            Value = value;
        }

        /// <summary>A design event with an optional value.</summary>
        public static GameAnalyticsEvent Design(string id, double? value = null) => new(GameAnalyticsEventKind.Design, id, value);

        /// <summary>A progression event. <paramref name="status"/> must be one of the progression kinds.</summary>
        public static GameAnalyticsEvent Progression(GameAnalyticsEventKind status, string id)
        {
            if (status == GameAnalyticsEventKind.Design) throw new ArgumentException("A progression needs a progression status.", nameof(status));
            return new(status, id, null);
        }
    }

    /// <summary>
    /// Converts a record into GameAnalytics events. Return false to use the default conversion (a design event named after the record).
    /// Return true with no events added to skip the record, or add events to send them in order. The sink clears <paramref name="output"/> before each call.
    /// </summary>
    public delegate bool GameAnalyticsMap(TelemetryRecord record, List<GameAnalyticsEvent> output);
}
