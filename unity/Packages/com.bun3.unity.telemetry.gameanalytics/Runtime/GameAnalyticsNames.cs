using System;
using Cysharp.Text;

namespace Bun3.Unity.Telemetry.GameAnalytics
{
    /// <summary>Makes ids acceptable to GameAnalytics, which drops events whose id breaks its rules.</summary>
    internal static class GameAnalyticsNames
    {
        internal const int MaxSegmentLength = 64;
        internal const string Empty = "none";

        /// <summary>
        /// Replaces characters outside <c>[A-Za-z0-9 _-.()!?]</c> with <c>_</c>, cuts each segment to 64 characters,
        /// turns empty segments into <c>none</c> and keeps at most <paramref name="maxSegments"/> segments.
        /// </summary>
        internal static string Clean(string id, int maxSegments, out bool changed)
        {
            if (string.IsNullOrEmpty(id))
            {
                changed = true;
                return Empty;
            }
            var parts = id.Split(':');
            var count = Math.Min(parts.Length, maxSegments);
            changed = parts.Length > maxSegments;
            using var sb = ZString.CreateStringBuilder();
            for (var i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(':');
                var part = parts[i];
                if (part.Length == 0)
                {
                    sb.Append(Empty);
                    changed = true;
                    continue;
                }
                var length = Math.Min(part.Length, MaxSegmentLength);
                if (part.Length > MaxSegmentLength) changed = true;
                for (var j = 0; j < length; j++)
                {
                    var c = part[j];
                    if (Allowed(c)) sb.Append(c);
                    else
                    {
                        sb.Append('_');
                        changed = true;
                    }
                }
            }
            return sb.ToString();
        }

        static bool Allowed(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
            c == ' ' || c == '-' || c == '_' || c == '.' || c == '(' || c == ')' || c == '!' || c == '?';
    }
}
