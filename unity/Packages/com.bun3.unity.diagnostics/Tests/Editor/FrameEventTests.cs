using System.Collections.Generic;
using NUnit.Framework;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class FrameEventTests
    {
        [Test]
        public void FromFields_MapsKnownFields()
        {
            var fields = new Dictionary<string, string>
            {
                ["OriginalShaderName"] = "Sprites/Default",
                ["PassName"] = "Pass 0",
                ["VertexCount"] = "4",
                ["DrawCallCount"] = "1",
                ["InstanceCount"] = "2",
                ["BatchBreakCause"] = "2",
                ["RenderTargetName"] = "BackBuffer",
            };
            var causes = new[] { "a", "b", "Different materials" };

            var e = FrameEvent.FromFields(7, "Draw Mesh", fields, causes, "Root/Child");

            Assert.AreEqual(7, e.index);
            Assert.AreEqual("Draw Mesh", e.eventType);
            Assert.AreEqual("Sprites/Default", e.shader);
            Assert.AreEqual("Pass 0", e.pass);
            Assert.AreEqual(4, e.vertexCount);
            Assert.AreEqual(1, e.drawCallCount);
            Assert.AreEqual(2, e.instanceCount);
            Assert.AreEqual("Different materials", e.batchBreakCause);
            Assert.AreEqual("BackBuffer", e.renderTarget);
            Assert.AreEqual("Root/Child", e.gameObjectPath);
        }

        [Test]
        public void FromFields_MissingFieldsFallBackToDefaults()
        {
            var e = FrameEvent.FromFields(0, "Clear", new Dictionary<string, string>(), null, "");

            Assert.AreEqual("", e.shader);
            Assert.AreEqual(0, e.vertexCount);
            Assert.AreEqual("", e.batchBreakCause);
            Assert.AreEqual("", e.gameObjectPath);
        }

        [Test]
        public void FromFields_OutOfRangeBreakCauseKeepsRawValue()
        {
            var fields = new Dictionary<string, string> { ["BatchBreakCause"] = "9" };

            var e = FrameEvent.FromFields(0, "Draw", fields, new[] { "a" }, "");

            Assert.AreEqual("9", e.batchBreakCause);
        }

        [Test]
        public void FromFields_PrefersRenderTargetRenderTexture()
        {
            var fields = new Dictionary<string, string>
            {
                ["RenderTargetRenderTexture"] = "RT",
                ["RenderTargetName"] = "BB",
            };

            var e = FrameEvent.FromFields(0, "Draw", fields, null, "");

            Assert.AreEqual("RT", e.renderTarget);
        }
    }
}
