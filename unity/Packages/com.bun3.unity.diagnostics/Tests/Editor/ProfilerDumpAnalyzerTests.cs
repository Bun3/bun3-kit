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

        [Test]
        public void Analyze_SpikesIncludeWorstFivePlusBudgetExceeders()
        {
            var frames = new List<ProfilerFrameSample>();
            for (int i = 0; i < 8; i++)
                frames.Add(F(i, 10f)); // calm frames
            frames.Add(F(100, 40f));   // over budget
            frames.Add(F(101, 50f));   // over budget

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(5, a.spikes.Count); // worst 5 (two exceeders are inside the worst five)
            Assert.AreEqual(101, a.spikes[0].frameIndex);
            Assert.AreEqual(100, a.spikes[1].frameIndex);
            Assert.AreEqual(10f, a.spikes[2].cpuMs);
        }

        [Test]
        public void Analyze_SpikeListIsCappedAtTen()
        {
            var frames = Enumerable.Range(0, 30).Select(i => F(i, 100f + i)).ToList(); // all over budget

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(10, a.spikes.Count);
            Assert.AreEqual(129f, a.spikes[0].cpuMs);
        }

        [Test]
        public void Analyze_SpikeCarriesTopMarkersBySelfTime()
        {
            var spike = F(7, 60f, markers: new[]
            {
                M("Cheap", 1f), M("Heavy", 30f), M("Mid", 10f),
            });
            var frames = new List<ProfilerFrameSample> { spike, F(8, 5f) };

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(7, a.spikes[0].frameIndex);
            Assert.AreEqual("Heavy", a.spikes[0].topMarkers[0].name);
            Assert.AreEqual("Mid", a.spikes[0].topMarkers[1].name);
        }

        [Test]
        public void Analyze_AggregatesMarkersAcrossFrames()
        {
            var frames = new List<ProfilerFrameSample>
            {
                F(0, 10f, markers: new[] { M("Update", 4f, calls: 2, gc: 100) }),
                F(1, 12f, markers: new[] { M("Update", 6f, calls: 3, gc: 50), M("Render", 8f) }),
            };

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            var update = a.topMarkersBySelfTime.First(m => m.name == "Update");
            Assert.AreEqual(10f, update.totalSelfMs, 0.001f);
            Assert.AreEqual(5f, update.avgSelfMsPerFrame, 0.001f);
            Assert.AreEqual(6f, update.maxSelfMs, 0.001f);
            Assert.AreEqual(5, update.callCount);
            Assert.AreEqual(150L, update.gcAllocBytes);
        }

        [Test]
        public void Analyze_GcListFiltersZeroAllocMarkers()
        {
            var frames = new List<ProfilerFrameSample>
            {
                F(0, 10f, markers: new[] { M("Alloc", 1f, gc: 500), M("Clean", 9f) }),
            };

            var a = ProfilerDumpAnalyzer.Analyze(frames, 33.3f);

            Assert.AreEqual(1, a.topMarkersByGcAlloc.Count);
            Assert.AreEqual("Alloc", a.topMarkersByGcAlloc[0].name);
            Assert.AreEqual("Clean", a.topMarkersBySelfTime[0].name);
        }
    }
}
