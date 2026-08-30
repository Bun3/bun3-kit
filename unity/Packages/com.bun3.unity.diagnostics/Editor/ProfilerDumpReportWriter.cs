using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Bun3.Unity.Diagnostics
{
    // JSON document shape: { timestamp, source, frames[], analysis } — per-frame stats live here;
    // the Markdown report carries only the analysis sections.
    [Serializable]
    internal sealed class ProfilerDumpDocument
    {
        public string timestamp = "";
        public string source = "";
        public List<ProfilerFrameStat> frames = new List<ProfilerFrameStat>();
        public ProfilerDumpAnalysis analysis = new ProfilerDumpAnalysis();
    }

    internal static class ProfilerDumpReportWriter
    {
        internal static string ToJson(string timestamp, string source, List<ProfilerFrameStat> frames, ProfilerDumpAnalysis analysis) =>
            JsonUtility.ToJson(
                new ProfilerDumpDocument { timestamp = timestamp, source = source, frames = frames, analysis = analysis },
                true);

        internal static string ToMarkdown(string timestamp, string source, IReadOnlyList<ProfilerFrameStat> frames, ProfilerDumpAnalysis analysis)
        {
            var sb = new StringBuilder(1 << 18);
            sb.AppendLine($"# Profiler Dump {timestamp}");
            sb.AppendLine();
            sb.AppendLine($"- Source: {source}");
            sb.AppendLine($"- Frames: {analysis.frameCount}");
            sb.AppendLine($"- CPU ms: median {analysis.medianCpuMs:F1} / avg {analysis.averageCpuMs:F1} / p95 {analysis.p95CpuMs:F1} / worst {analysis.worstCpuMs:F1} (budget {analysis.budgetMs:F1})");
            sb.AppendLine($"- GPU avg: {analysis.averageGpuMs:F1} ms, render thread avg: {analysis.averageRenderThreadMs:F1} ms");
            sb.AppendLine($"- GC alloc: total {FormatBytes(analysis.totalGcAllocBytes)}, avg {FormatBytes(analysis.averageFrameGcAllocBytes)}/frame, worst frame {FormatBytes(analysis.worstFrameGcAllocBytes)}");

            sb.AppendLine();
            sb.AppendLine("## Spike frames");
            if (analysis.spikes.Count == 0)
                sb.AppendLine("(none)");
            foreach (var s in analysis.spikes)
            {
                sb.AppendLine($"- [frame {s.frameIndex}] {s.cpuMs:F1} ms");
                foreach (var m in s.topMarkers)
                    sb.AppendLine($"  - {m.name}: {m.selfMs:F1} ms self, {m.calls} calls, {FormatBytes(m.gcAllocBytes)}");
            }

            sb.AppendLine();
            sb.AppendLine("## Top markers by self time");
            if (analysis.topMarkersBySelfTime.Count == 0)
                sb.AppendLine("(none)");
            foreach (var m in analysis.topMarkersBySelfTime)
                sb.AppendLine($"- {m.name}: total {m.totalSelfMs:F1} ms, avg {m.avgSelfMsPerFrame:F2} ms/frame, max {m.maxSelfMs:F1} ms, {m.callCount} calls");

            sb.AppendLine();
            sb.AppendLine("## Top markers by GC alloc");
            if (analysis.topMarkersByGcAlloc.Count == 0)
                sb.AppendLine("(none)");
            foreach (var m in analysis.topMarkersByGcAlloc)
                sb.AppendLine($"- {m.name}: {FormatBytes(m.gcAllocBytes)} total, {m.callCount} calls");

            return sb.ToString();
        }

        static string FormatBytes(long bytes)
        {
            if (bytes >= 1 << 20)
                return $"{bytes / (float)(1 << 20):F1} MB";
            if (bytes >= 1 << 10)
                return $"{bytes / (float)(1 << 10):F1} KB";
            return $"{bytes} B";
        }
    }
}
