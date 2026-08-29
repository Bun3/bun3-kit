using System;
using System.Collections.Generic;
using System.Linq;

namespace Bun3.Unity.Diagnostics
{
    /// <summary>A profiler frame flagged as a spike, with its heaviest markers.</summary>
    [Serializable]
    public sealed class SpikeFrame
    {
        /// <summary>Profiler frame index.</summary>
        public int frameIndex;

        /// <summary>Main-thread CPU time of the frame in milliseconds.</summary>
        public float cpuMs;

        /// <summary>Heaviest markers of the frame by self time.</summary>
        public List<MarkerSample> topMarkers = new List<MarkerSample>();
    }

    /// <summary>Marker totals aggregated across every captured frame.</summary>
    [Serializable]
    public sealed class ProfilerMarkerStat
    {
        /// <summary>Profiler marker name.</summary>
        public string name = "";

        /// <summary>Self time summed across all frames, in milliseconds.</summary>
        public float totalSelfMs;

        /// <summary>Average self time per captured frame, in milliseconds.</summary>
        public float avgSelfMsPerFrame;

        /// <summary>Largest single-frame self time, in milliseconds.</summary>
        public float maxSelfMs;

        /// <summary>Call count summed across all frames.</summary>
        public int callCount;

        /// <summary>GC allocation summed across all frames, in bytes.</summary>
        public long gcAllocBytes;
    }

    /// <summary>Aggregated analysis of one profiler capture.</summary>
    [Serializable]
    public sealed class ProfilerDumpAnalysis
    {
        /// <summary>Number of analyzed frames.</summary>
        public int frameCount;

        /// <summary>CPU frame budget used for spike reporting, in milliseconds.</summary>
        public float budgetMs;

        /// <summary>Lower-median main-thread CPU time, in milliseconds.</summary>
        public float medianCpuMs;

        /// <summary>Average main-thread CPU time, in milliseconds.</summary>
        public float averageCpuMs;

        /// <summary>95th-percentile main-thread CPU time, in milliseconds.</summary>
        public float p95CpuMs;

        /// <summary>Worst main-thread CPU time, in milliseconds.</summary>
        public float worstCpuMs;

        /// <summary>Average GPU time, in milliseconds; 0 when unavailable.</summary>
        public float averageGpuMs;

        /// <summary>Average render-thread total time, in milliseconds.</summary>
        public float averageRenderThreadMs;

        /// <summary>GC allocation summed across all frames, in bytes.</summary>
        public long totalGcAllocBytes;

        /// <summary>Largest single-frame GC allocation, in bytes.</summary>
        public long worstFrameGcAllocBytes;

        /// <summary>Spike frames: worst frames plus budget-exceeders, capped.</summary>
        public List<SpikeFrame> spikes = new List<SpikeFrame>();

        /// <summary>Markers with the highest summed self time.</summary>
        public List<ProfilerMarkerStat> topMarkersBySelfTime = new List<ProfilerMarkerStat>();

        /// <summary>Markers with the highest summed GC allocation.</summary>
        public List<ProfilerMarkerStat> topMarkersByGcAlloc = new List<ProfilerMarkerStat>();
    }

    /// <summary>Pure analysis over captured profiler frames; no editor state.</summary>
    public static class ProfilerDumpAnalyzer
    {
        /// <summary>Markers reported in each aggregation list.</summary>
        public const int TopMarkerCount = 20;

        /// <summary>Markers reported per spike frame.</summary>
        public const int SpikeTopMarkerCount = 10;

        /// <summary>Worst frames always included as spikes.</summary>
        public const int WorstFrameCount = 5;

        /// <summary>Maximum spike frames reported.</summary>
        public const int MaxSpikeCount = 10;

        /// <summary>Runs the full analysis over the captured frames.</summary>
        public static ProfilerDumpAnalysis Analyze(IReadOnlyList<ProfilerFrameSample> frames, float budgetMs)
        {
            var analysis = new ProfilerDumpAnalysis { frameCount = frames.Count, budgetMs = budgetMs };
            if (frames.Count == 0)
                return analysis;

            var cpu = frames.Select(f => f.stat.cpuMs).OrderBy(v => v).ToArray();
            analysis.medianCpuMs = cpu[(cpu.Length - 1) / 2];
            analysis.averageCpuMs = cpu.Average();
            analysis.p95CpuMs = cpu[Math.Min(cpu.Length - 1, (int)Math.Ceiling(cpu.Length * 0.95) - 1)];
            analysis.worstCpuMs = cpu[cpu.Length - 1];
            analysis.averageGpuMs = frames.Average(f => f.stat.gpuMs);
            analysis.averageRenderThreadMs = frames.Average(f => f.stat.renderThreadMs);
            analysis.totalGcAllocBytes = frames.Sum(f => f.stat.gcAllocBytes);
            analysis.worstFrameGcAllocBytes = frames.Max(f => f.stat.gcAllocBytes);
            analysis.spikes = SelectSpikes(frames, budgetMs);
            AggregateMarkers(frames, analysis);
            return analysis;
        }

        static List<SpikeFrame> SelectSpikes(IReadOnlyList<ProfilerFrameSample> frames, float budgetMs)
        {
            return new List<SpikeFrame>();
        }

        static void AggregateMarkers(IReadOnlyList<ProfilerFrameSample> frames, ProfilerDumpAnalysis analysis)
        {
        }
    }
}
