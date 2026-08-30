using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class ProfilerDumpReportWriterTests
    {
        static (List<ProfilerFrameStat> frames, ProfilerDumpAnalysis analysis) Sample()
        {
            var samples = new List<ProfilerFrameSample>
            {
                ProfilerDumpAnalyzerTests.F(0, 40f, gc: 2048, markers: new[] { ProfilerDumpAnalyzerTests.M("Heavy", 30f, gc: 2048) }),
                ProfilerDumpAnalyzerTests.F(1, 10f),
            };
            var analysis = ProfilerDumpAnalyzer.Analyze(samples, 33.3f);
            var frames = new List<ProfilerFrameStat> { samples[0].stat, samples[1].stat };
            return (frames, analysis);
        }

        [Test]
        public void Markdown_ContainsOverviewSpikesAndMarkerSections()
        {
            var (frames, analysis) = Sample();

            var md = ProfilerDumpReportWriter.ToMarkdown("2026-08-30 12:00:00", "live", frames, analysis);

            StringAssert.Contains("# Profiler Dump 2026-08-30 12:00:00", md);
            StringAssert.Contains("Source: live", md);
            StringAssert.Contains("## Spike frames", md);
            StringAssert.Contains("[frame 0] 40.0 ms", md);
            StringAssert.Contains("Heavy: 30.0 ms self", md);
            StringAssert.Contains("## Top markers by self time", md);
            StringAssert.Contains("## Top markers by GC alloc", md);
            StringAssert.Contains("2.0 KB", md);
        }

        [Test]
        public void Json_RoundTripsThroughJsonUtility()
        {
            var (frames, analysis) = Sample();

            var json = ProfilerDumpReportWriter.ToJson("t", "live", frames, analysis);
            var doc = JsonUtility.FromJson<ProfilerDumpDocument>(json);

            Assert.AreEqual("t", doc.timestamp);
            Assert.AreEqual("live", doc.source);
            Assert.AreEqual(2, doc.frames.Count);
            Assert.AreEqual(40f, doc.frames[0].cpuMs);
            Assert.AreEqual(2, doc.analysis.frameCount);
            Assert.AreEqual(2, doc.analysis.spikes.Count); // both frames sit inside the worst-5 window
        }
    }
}
