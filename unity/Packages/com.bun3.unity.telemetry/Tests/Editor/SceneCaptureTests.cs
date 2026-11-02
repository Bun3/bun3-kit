using NUnit.Framework;

namespace Bun3.Unity.Telemetry.Tests
{
    public class SceneCaptureTests
    {
        TelemetryHub previousHub;
        ListSink sink;

        [SetUp]
        public void SetUp()
        {
            previousHub = Telemetry.Hub;
            sink = new ListSink();
            Telemetry.Hub = new TelemetryBuilder(null).Add(sink, TelemetryTier.All).Build();
        }

        [TearDown]
        public void TearDown()
        {
            SceneCapture.Uninstall();
            Telemetry.Hub = previousHub;
        }

        [Test]
        public void Changed_RecordsFromAndTo()
        {
            SceneCapture.OnChanged("Menu", "Game");
            var r = sink.Written[0];
            Assert.AreEqual("scene_changed", r.Name);
            Assert.AreEqual(TelemetryCategory.Scene, r.Category);
            CollectionAssert.Contains(r.Props, ("from", (object)"Menu"));
            CollectionAssert.Contains(r.Props, ("to", (object)"Game"));
        }
    }
}
