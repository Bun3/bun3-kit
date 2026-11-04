using NUnit.Framework;

namespace Bun3.Unity.Telemetry.Tests
{
    public class HistorySinkTests
    {
        static TelemetryRecord Rec(string name, long seq, TelemetryCategory category = TelemetryCategory.Activity) =>
            new() { Name = name, Category = category, Seq = seq };

        [Test] public void StartsClosed_DropsRecords()
        {
            var sink = new HistorySink();
            sink.Write(Rec("a", 1));
            Assert.AreEqual(0, sink.Find().Count);
            Assert.IsFalse(sink.IsOpen);
            Assert.IsFalse(sink.Remote);
        }

        [Test] public void Open_ClearsAndAccepts()
        {
            var sink = new HistorySink();
            sink.Open();
            sink.Write(Rec("a", 1));
            Assert.IsTrue(sink.IsOpen);
            Assert.AreEqual(1, sink.Find().Count);
        }

        [Test] public void OpenWhileOpen_Clears()
        {
            var sink = new HistorySink();
            sink.Open();
            sink.Write(Rec("a", 1));
            sink.Open();
            Assert.AreEqual(0, sink.Find().Count);
            Assert.IsTrue(sink.IsOpen);
        }

        [Test] public void Close_ClearsAndStops()
        {
            var sink = new HistorySink();
            sink.Open();
            sink.Write(Rec("a", 1));
            sink.Close();
            sink.Write(Rec("b", 2));
            Assert.AreEqual(0, sink.Find().Count);
            Assert.IsFalse(sink.IsOpen);
        }

        [Test] public void CloseWhileClosed_DoesNotThrow()
        {
            var sink = new HistorySink();
            Assert.DoesNotThrow(sink.Close);
            Assert.DoesNotThrow(sink.Close);
        }

        [Test] public void OverCapacity_DropsDebugFirst()
        {
            var sink = new HistorySink(3);
            sink.Open();
            sink.Write(Rec("a1", 1, TelemetryCategory.Analytics));
            sink.Write(Rec("ui", 2, TelemetryCategory.Ui));
            sink.Write(Rec("a2", 3, TelemetryCategory.Analytics));
            sink.Write(Rec("a3", 4, TelemetryCategory.Analytics));
            var all = sink.Find();
            Assert.AreEqual(3, all.Count);
            foreach (var r in all) Assert.AreNotEqual("ui", r.Name);
        }

        [Test] public void OverCapacity_NoDebug_DropsOldest()
        {
            var sink = new HistorySink(2);
            sink.Open();
            sink.Write(Rec("a", 1));
            sink.Write(Rec("b", 2));
            sink.Write(Rec("c", 3));
            var all = sink.Find();
            Assert.AreEqual("b", all[0].Name);
            Assert.AreEqual("c", all[1].Name);
        }

        [Test] public void Find_FiltersByName_SortedBySeq()
        {
            var sink = new HistorySink();
            sink.Open();
            sink.Write(Rec("room_entered", 3));
            sink.Write(Rec("ui_click", 1, TelemetryCategory.Ui));
            sink.Write(Rec("mission_completed", 2));
            var found = sink.Find("room_entered", "mission_completed");
            Assert.AreEqual(2, found.Count);
            Assert.AreEqual("mission_completed", found[0].Name);
            Assert.AreEqual("room_entered", found[1].Name);
            Assert.AreEqual(3, sink.Find().Count);
        }
    }
}
