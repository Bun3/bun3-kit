using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Telemetry.Tests
{
    public class EngineLogCaptureTests
    {
        const string Line = "[X.Net] a=1";
        TelemetryHub previousHub;
        ListSink sink;

        [SetUp]
        public void SetUp()
        {
            previousHub = Telemetry.Hub;
            sink = new ListSink();
            Telemetry.Hub = new TelemetryBuilder(null).Add(sink, TelemetryTier.All).Build();
            EngineLogCapture.ResetStatics();
            EngineLogCapture.Install(new[] { "[X.Net]" });
        }

        [TearDown]
        public void TearDown()
        {
            EngineLogCapture.ResetStatics();
            Telemetry.Hub = previousHub;
        }

        static object Prop(TelemetryRecord r, string key)
        {
            foreach (var p in r.Props) if (p.Key == key) return p.Value;
            return null;
        }

        [Test]
        public void RepeatedMessage_CappedPerRun()
        {
            for (int i = 0; i < 25; i++) EngineLogCapture.Handle("spam", "", LogType.Warning);
            Assert.AreEqual(EngineLogCapture.MaxPerMessage, sink.Written.Count);
            Assert.AreEqual(true, Prop(sink.Written[EngineLogCapture.MaxPerMessage - 1], "suppressed"));
            Assert.IsNull(Prop(sink.Written[0], "suppressed"));

            // Other messages count separately and prefixed lines are never capped.
            EngineLogCapture.Handle("other", "", LogType.Error);
            for (int i = 0; i < 25; i++) EngineLogCapture.Handle(Line, "", LogType.Log);
            Assert.AreEqual(EngineLogCapture.MaxPerMessage + 26, sink.Written.Count);

            EngineLogCapture.ResetStatics();
            EngineLogCapture.Install(new[] { "[X.Net]" });
            EngineLogCapture.Handle("spam", "", LogType.Warning);
            Assert.AreEqual(EngineLogCapture.MaxPerMessage + 27, sink.Written.Count);
        }

        [Test]
        public void FullCounterTable_LetsNewMessagesThrough()
        {
            for (int i = 0; i < EngineLogCapture.MaxCountedMessages; i++) EngineLogCapture.Handle(i.ToString(), "", LogType.Warning);
            sink.Written.Clear();
            for (int i = 0; i < 25; i++) EngineLogCapture.Handle("late", "", LogType.Warning);
            Assert.AreEqual(25, sink.Written.Count);
            Assert.IsTrue(sink.Written.TrueForAll(r => Prop(r, "suppressed") == null));
        }

        [Test]
        public void Warning_RecordedUnmasked()
        {
            EngineLogCapture.Handle("join ABC234", "", LogType.Warning);
            Assert.AreEqual("engine_log", sink.Written[0].Name);
            Assert.AreEqual("warning", Prop(sink.Written[0], "type"));
            Assert.AreEqual("join ABC234", Prop(sink.Written[0], "message"));
        }

        [Test]
        public void Exception_RecordsStack()
        {
            EngineLogCapture.Handle("boom", "at Join(ABC234)", LogType.Exception);
            Assert.AreEqual("at Join(ABC234)", Prop(sink.Written[0], "stack"));
        }

        [Test]
        public void PlainLog_NotRecorded()
        {
            EngineLogCapture.Handle("x", "", LogType.Log);
            Assert.AreEqual(0, sink.Written.Count);
        }

        [Test]
        public void PrefixLine_RecordedAsLogLine()
        {
            EngineLogCapture.Handle(Line, "", LogType.Log);
            Assert.AreEqual(1, sink.Written.Count);
            Assert.AreEqual("log_line", sink.Written[0].Name);
            Assert.AreEqual(Line, Prop(sink.Written[0], "line"));
        }

        [Test]
        public void ReentrantLog_NotRecordedAgain()
        {
            sink = new ReentrantSink();
            Telemetry.Hub = new TelemetryBuilder(null).Add(sink, TelemetryTier.All).Build();
            EngineLogCapture.Handle("first", "", LogType.Warning);
            Assert.AreEqual(1, sink.Written.Count);
        }

        sealed class ReentrantSink : ListSink
        {
            public override void Write(TelemetryRecord record)
            {
                base.Write(record);
                EngineLogCapture.Handle("inner", "", LogType.Warning);
            }
        }
    }
}
