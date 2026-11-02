using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.Telemetry
{
    /// <summary>
    /// Records Unity log output. Warnings and above become engine_log, capped per message text per run
    /// (the last allowed record carries suppressed=true). Lines starting with a configured prefix become log_line and are never capped.
    /// </summary>
    static class EngineLogCapture
    {
        internal const int MaxPerMessage = 20, MaxCountedMessages = 256;
        static readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);
        static string[] prefixes = Array.Empty<string>();
        static bool handling;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Uninstall();
            handling = false;
            counts.Clear();
        }

        // ponytail: logMessageReceived only delivers main-thread logs. Switch to logMessageReceivedThreaded with a lock if other threads must be recorded.
        internal static void Install(string[] linePrefixes)
        {
            Uninstall();
            prefixes = linePrefixes ?? Array.Empty<string>();
            Application.logMessageReceived += Handle;
        }

        internal static void Uninstall()
        {
            Application.logMessageReceived -= Handle;
            prefixes = Array.Empty<string>();
        }

        internal static void Handle(string condition, string stackTrace, LogType type)
        {
            // Logs raised while recording (hub warnings) are not recorded again.
            if (handling) return;
            var line = HasPrefix(condition);
            if (!line && type == LogType.Log) return;
            var last = false;
            if (!line && !Count(condition, out last)) return;
            handling = true;
            try
            {
                if (line)
                {
                    Telemetry.Track(TelemetryCategory.EngineLog, "log_line", ("line", condition));
                    return;
                }
                var kind = type.ToString().ToLowerInvariant();
                var stack = type == LogType.Exception ? stackTrace : null;
                if (last)
                    Telemetry.Track(TelemetryCategory.EngineLog, "engine_log", ("type", kind), ("message", condition),
                        ("stack", stack), ("suppressed", true));
                else
                    Telemetry.Track(TelemetryCategory.EngineLog, "engine_log", ("type", kind), ("message", condition),
                        ("stack", stack));
            }
            finally { handling = false; }
        }

        static bool HasPrefix(string condition)
        {
            if (condition == null) return false;
            foreach (var p in prefixes)
                if (!string.IsNullOrEmpty(p) && condition.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        // Decides whether to record. Once the table is full, new messages are recorded without being counted.
        static bool Count(string condition, out bool last)
        {
            last = false;
            if (condition == null) return true;
            if (counts.TryGetValue(condition, out var n))
            {
                if (n >= MaxPerMessage) return false;
                counts[condition] = ++n;
                last = n == MaxPerMessage;
                return true;
            }
            if (counts.Count < MaxCountedMessages) counts[condition] = 1;
            return true;
        }
    }
}
