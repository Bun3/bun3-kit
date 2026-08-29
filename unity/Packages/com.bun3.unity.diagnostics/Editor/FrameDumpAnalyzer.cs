using System;
using System.Collections.Generic;
using System.Linq;

namespace Bun3.Unity.Diagnostics
{
    /// <summary>A key with an aggregated call count.</summary>
    [Serializable]
    public sealed class CountEntry
    {
        /// <summary>Aggregation key (shader name, break cause, pair, or path prefix).</summary>
        public string key = "";

        /// <summary>Number of events aggregated under the key.</summary>
        public int count;
    }

    /// <summary>A span where two shaders alternate A-B-A-B, defeating batching.</summary>
    [Serializable]
    public sealed class InterleaveSpan
    {
        /// <summary>Event index where the span starts.</summary>
        public int startIndex;

        /// <summary>Event index where the span ends (inclusive).</summary>
        public int endIndex;

        /// <summary>First alternating shader.</summary>
        public string shaderA = "";

        /// <summary>Second alternating shader.</summary>
        public string shaderB = "";

        /// <summary>Number of shader switches inside the span.</summary>
        public int switchCount;

        /// <summary>Draw calls beyond the two the span could batch down to.</summary>
        public int wastedCalls;
    }

    /// <summary>A run of consecutive events sharing one shader.</summary>
    [Serializable]
    public sealed class ShaderRun
    {
        /// <summary>Event index where the run starts.</summary>
        public int startIndex;

        /// <summary>Number of consecutive events in the run.</summary>
        public int length;

        /// <summary>Shader shared by the run.</summary>
        public string shader = "";
    }

    /// <summary>Aggregated batching analysis of one Frame Debugger capture.</summary>
    [Serializable]
    public sealed class FrameDumpAnalysis
    {
        /// <summary>Total captured events.</summary>
        public int totalEvents;

        /// <summary>Call counts per shader, descending.</summary>
        public List<CountEntry> callsByShader = new List<CountEntry>();

        /// <summary>Call counts per batch-break cause, descending.</summary>
        public List<CountEntry> callsByBreakCause = new List<CountEntry>();

        /// <summary>Call counts per "shader | break cause" pair, descending.</summary>
        public List<CountEntry> callsByShaderAndBreakCause = new List<CountEntry>();

        /// <summary>Spans where two shaders alternate A-B-A-B.</summary>
        public List<InterleaveSpan> interleaves = new List<InterleaveSpan>();

        /// <summary>Longest consecutive same-shader runs, descending by length.</summary>
        public List<ShaderRun> longestRuns = new List<ShaderRun>();

        /// <summary>Number of draws with exactly four vertices (missing-atlas signal).</summary>
        public int singleQuadDrawCount;

        /// <summary>Call counts per two-segment hierarchy path prefix, descending.</summary>
        public List<CountEntry> callsByPathPrefix = new List<CountEntry>();
    }

    /// <summary>Pure analysis over captured <see cref="FrameEvent"/> lists; no editor state.</summary>
    public static class FrameDumpAnalyzer
    {
        /// <summary>Minimum shader switches for a span to be reported as interleaved.</summary>
        public const int InterleaveMinSwitches = 4;

        const int TopRunCount = 10;

        /// <summary>Runs every aggregation and detector over the events.</summary>
        public static FrameDumpAnalysis Analyze(IReadOnlyList<FrameEvent> events)
        {
            return new FrameDumpAnalysis
            {
                totalEvents = events.Count,
                callsByShader = CountBy(events, e => e.shader),
                callsByBreakCause = CountBy(events, e => e.batchBreakCause),
                callsByShaderAndBreakCause = CountBy(
                    events,
                    e => string.IsNullOrEmpty(e.shader) ? "" : $"{e.shader} | {e.batchBreakCause}"),
                interleaves = DetectInterleaves(events),
                longestRuns = LongestRuns(events),
                singleQuadDrawCount = events.Count(e => e.vertexCount == 4),
                callsByPathPrefix = CountBy(events, e => PathPrefix(e.gameObjectPath)),
            };
        }

        static List<CountEntry> CountBy(IReadOnlyList<FrameEvent> events, Func<FrameEvent, string> keyOf)
        {
            var counts = new Dictionary<string, int>();
            foreach (var e in events)
            {
                var key = keyOf(e);
                if (string.IsNullOrEmpty(key))
                    continue;
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
            }

            return counts
                .Select(p => new CountEntry { key = p.Key, count = p.Value })
                .OrderByDescending(x => x.count)
                .ThenBy(x => x.key, StringComparer.Ordinal)
                .ToList();
        }

        static List<InterleaveSpan> DetectInterleaves(IReadOnlyList<FrameEvent> events)
        {
            return new List<InterleaveSpan>();
        }

        static List<ShaderRun> LongestRuns(IReadOnlyList<FrameEvent> events)
        {
            return new List<ShaderRun>();
        }

        static string PathPrefix(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            int first = path.IndexOf('/');
            if (first < 0)
                return path;
            int second = path.IndexOf('/', first + 1);
            return second < 0 ? path : path.Substring(0, second);
        }
    }
}
