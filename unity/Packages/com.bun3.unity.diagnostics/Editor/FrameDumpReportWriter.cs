using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Bun3.Unity.Diagnostics
{
    // JSON document shape: { timestamp, events[], analysis } — the structured twin of the
    // Markdown report, serialized with JsonUtility (no external JSON dependency).
    [Serializable]
    internal sealed class FrameDumpDocument
    {
        public string timestamp = "";
        public List<FrameEvent> events = new List<FrameEvent>();
        public FrameDumpAnalysis analysis = new FrameDumpAnalysis();
    }

    internal static class FrameDumpReportWriter
    {
        const int TopEntryCount = 15;

        internal static string ToJson(string timestamp, List<FrameEvent> events, FrameDumpAnalysis analysis) =>
            JsonUtility.ToJson(
                new FrameDumpDocument { timestamp = timestamp, events = events, analysis = analysis },
                true);

        internal static string ToMarkdown(string timestamp, IReadOnlyList<FrameEvent> events, FrameDumpAnalysis analysis)
        {
            var sb = new StringBuilder(1 << 20);
            sb.AppendLine($"# Frame Debugger Dump {timestamp}");
            sb.AppendLine();
            sb.AppendLine($"- Total events: {analysis.totalEvents}");
            sb.AppendLine($"- Single-quad draws (vtx=4): {analysis.singleQuadDrawCount}");
            if (analysis.callsByBreakCause.Count > 0)
                sb.AppendLine($"- Top batch breaker: {analysis.callsByBreakCause[0].key} ({analysis.callsByBreakCause[0].count} calls)");

            CountSection(sb, "Calls by shader", analysis.callsByShader);
            CountSection(sb, "Calls by batch-break cause", analysis.callsByBreakCause);
            CountSection(sb, "Calls by shader x break cause", analysis.callsByShaderAndBreakCause);

            sb.AppendLine();
            sb.AppendLine("## Interleaved spans (A-B-A-B)");
            if (analysis.interleaves.Count == 0)
                sb.AppendLine("(none)");
            foreach (var s in analysis.interleaves)
                sb.AppendLine($"- [{s.startIndex}..{s.endIndex}] {s.shaderA} <-> {s.shaderB}: {s.switchCount} switches, ~{s.wastedCalls} wasted calls");

            sb.AppendLine();
            sb.AppendLine("## Longest same-shader runs");
            if (analysis.longestRuns.Count == 0)
                sb.AppendLine("(none)");
            foreach (var r in analysis.longestRuns)
                sb.AppendLine($"- [{r.startIndex}..{r.startIndex + r.length - 1}] {r.shader}: {r.length} calls");

            CountSection(sb, "Calls by path prefix", analysis.callsByPathPrefix);

            sb.AppendLine();
            sb.AppendLine("## Events");
            foreach (var e in events)
                sb.AppendLine(
                    $"[{e.index}] {e.eventType} shader={e.shader} pass={e.pass} vtx={e.vertexCount}" +
                    $" draws={e.drawCallCount} inst={e.instanceCount} break={e.batchBreakCause}" +
                    $" rt={e.renderTarget} go={e.gameObjectPath}");

            return sb.ToString();
        }

        static void CountSection(StringBuilder sb, string title, List<CountEntry> entries)
        {
            sb.AppendLine();
            sb.AppendLine($"## {title}");
            if (entries.Count == 0)
            {
                sb.AppendLine("(none)");
                return;
            }

            int shown = Math.Min(entries.Count, TopEntryCount);
            for (int i = 0; i < shown; i++)
                sb.AppendLine($"- {entries[i].key}: {entries[i].count}");
            if (entries.Count > shown)
                sb.AppendLine($"- ... {entries.Count - shown} more");
        }
    }
}
