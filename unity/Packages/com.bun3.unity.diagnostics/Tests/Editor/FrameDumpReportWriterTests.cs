using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class FrameDumpReportWriterTests
    {
        static List<FrameEvent> SampleEvents() => new List<FrameEvent>
        {
            new FrameEvent { index = 0, eventType = "Draw Mesh", shader = "A", vertexCount = 4 },
            new FrameEvent { index = 1, eventType = "Draw Mesh", shader = "B", batchBreakCause = "Different materials" },
        };

        [Test]
        public void Markdown_ContainsSummarySectionsAndEventLines()
        {
            var events = SampleEvents();

            var md = FrameDumpReportWriter.ToMarkdown("2026-08-29 12:00:00", events, FrameDumpAnalyzer.Analyze(events));

            StringAssert.Contains("# Frame Debugger Dump 2026-08-29 12:00:00", md);
            StringAssert.Contains("## Calls by shader", md);
            StringAssert.Contains("## Calls by batch-break cause", md);
            StringAssert.Contains("## Interleaved spans", md);
            StringAssert.Contains("## Longest same-shader runs", md);
            StringAssert.Contains("## Calls by path prefix", md);
            StringAssert.Contains("Single-quad draws (vtx=4): 1", md);
            StringAssert.Contains("[0] Draw Mesh shader=A", md);
            StringAssert.Contains("break=Different materials", md);
        }

        [Test]
        public void Json_RoundTripsThroughJsonUtility()
        {
            var events = SampleEvents();

            var json = FrameDumpReportWriter.ToJson("t", events, FrameDumpAnalyzer.Analyze(events));
            var doc = JsonUtility.FromJson<FrameDumpDocument>(json);

            Assert.AreEqual("t", doc.timestamp);
            Assert.AreEqual(2, doc.events.Count);
            Assert.AreEqual("A", doc.events[0].shader);
            Assert.AreEqual(2, doc.analysis.totalEvents);
        }
    }
}
