using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class ProfilerDumpAnalyzerTests
    {
        internal static ProfilerFrameSample F(int index, float cpuMs, long gc = 0, float renderMs = 0, params MarkerSample[] markers)
        {
            var f = new ProfilerFrameSample();
            f.stat.frameIndex = index;
            f.stat.cpuMs = cpuMs;
            f.stat.gcAllocBytes = gc;
            f.stat.renderThreadMs = renderMs;
            f.markers.AddRange(markers);
            return f;
        }

        internal static MarkerSample M(string name, float selfMs, int calls = 1, long gc = 0) =>
            new MarkerSample { name = name, selfMs = selfMs, calls = calls, gcAllocBytes = gc };

        [Test]
        public void Analyze_EmptyInputYieldsEmptyAnalysis()
        {
            var a = ProfilerDumpAnalyzer.Analyze(new List<ProfilerFrameSample>(), 33.3f);

            Assert.AreEqual(0, a.frameCount);
            Assert.AreEqual(0f, a.worstCpuMs);
            Assert.IsEmpty(a.spikes);
        }

        [Test]
        public void Analyze_ComputesCpuOverviewStats()
        {
            var frames = Enumerable.Range(1, 20).Select(i => F(i, i)).ToList(); // 1..20 ms

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(20, a.frameCount);
            Assert.AreEqual(10f, a.medianCpuMs);      // lower median of 1..20
            Assert.AreEqual(10.5f, a.averageCpuMs, 0.001f);
            Assert.AreEqual(19f, a.p95CpuMs);          // ceil(20*0.95)-1 = index 18 -> value 19
            Assert.AreEqual(20f, a.worstCpuMs);
        }

        [Test]
        public void Analyze_ComputesGcAndRenderThreadStats()
        {
            var frames = new List<ProfilerFrameSample>
            {
                F(0, 10f, gc: 100, renderMs: 2f),
                F(1, 12f, gc: 300, renderMs: 4f),
            };

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(400L, a.totalGcAllocBytes);
            Assert.AreEqual(300L, a.worstFrameGcAllocBytes);
            Assert.AreEqual(3f, a.averageRenderThreadMs, 0.001f);
        }
    }
}
