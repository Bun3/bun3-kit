using NUnit.Framework;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class FrameDumpAnalyzerTests
    {
        static FrameEvent E(int index, string shader, string breakCause = "", int vtx = 0, string path = "") =>
            new FrameEvent
            {
                index = index,
                shader = shader,
                batchBreakCause = breakCause,
                vertexCount = vtx,
                gameObjectPath = path,
            };

        [Test]
        public void Analyze_CountsByShaderDescending()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A"), E(1, "B"), E(2, "A") });

            Assert.AreEqual(3, a.totalEvents);
            Assert.AreEqual(2, a.callsByShader.Count);
            Assert.AreEqual("A", a.callsByShader[0].key);
            Assert.AreEqual(2, a.callsByShader[0].count);
            Assert.AreEqual("B", a.callsByShader[1].key);
        }

        [Test]
        public void Analyze_SkipsEmptyKeys()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, ""), E(1, "A") });

            Assert.AreEqual(1, a.callsByShader.Count);
        }

        [Test]
        public void Analyze_CountsShaderBreakCausePairs()
        {
            var a = FrameDumpAnalyzer.Analyze(new[]
            {
                E(0, "A", "Different materials"),
                E(1, "A", "Different materials"),
                E(2, "A", "First batch"),
            });

            Assert.AreEqual("A | Different materials", a.callsByShaderAndBreakCause[0].key);
            Assert.AreEqual(2, a.callsByShaderAndBreakCause[0].count);
        }

        [Test]
        public void Analyze_CountsSingleQuadDraws()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A", vtx: 4), E(1, "A", vtx: 4), E(2, "B", vtx: 600) });

            Assert.AreEqual(2, a.singleQuadDrawCount);
        }

        [Test]
        public void Analyze_AggregatesByTwoSegmentPathPrefix()
        {
            var a = FrameDumpAnalyzer.Analyze(new[]
            {
                E(0, "A", path: "UI/Main/Btn1"),
                E(1, "A", path: "UI/Main/Btn2"),
                E(2, "A", path: "World"),
            });

            Assert.AreEqual("UI/Main", a.callsByPathPrefix[0].key);
            Assert.AreEqual(2, a.callsByPathPrefix[0].count);
            Assert.AreEqual("World", a.callsByPathPrefix[1].key);
        }

        [Test]
        public void Analyze_ReportsAlternatingInterleaveSpan()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A"), E(1, "B"), E(2, "A"), E(3, "B"), E(4, "A") });

            Assert.AreEqual(1, a.interleaves.Count);
            var s = a.interleaves[0];
            Assert.AreEqual(0, s.startIndex);
            Assert.AreEqual(4, s.endIndex);
            Assert.AreEqual("A", s.shaderA);
            Assert.AreEqual("B", s.shaderB);
            Assert.AreEqual(4, s.switchCount);
            Assert.AreEqual(3, s.wastedCalls);
        }

        [Test]
        public void Analyze_IgnoresShortAlternation()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A"), E(1, "B"), E(2, "A"), E(3, "C"), E(4, "C") });

            Assert.IsEmpty(a.interleaves);
        }

        [Test]
        public void Analyze_EmptyShaderBreaksInterleaveSpan()
        {
            var a = FrameDumpAnalyzer.Analyze(new[]
            {
                E(0, "A"), E(1, "B"), E(2, ""), E(3, "A"), E(4, "B"), E(5, "A"),
            });

            Assert.IsEmpty(a.interleaves);
        }

        [Test]
        public void Analyze_FindsLongestRunsDescending()
        {
            var a = FrameDumpAnalyzer.Analyze(new[] { E(0, "A"), E(1, "A"), E(2, "A"), E(3, "B"), E(4, "B") });

            Assert.AreEqual(2, a.longestRuns.Count);
            Assert.AreEqual("A", a.longestRuns[0].shader);
            Assert.AreEqual(3, a.longestRuns[0].length);
            Assert.AreEqual(0, a.longestRuns[0].startIndex);
            Assert.AreEqual("B", a.longestRuns[1].shader);
        }
    }
}
