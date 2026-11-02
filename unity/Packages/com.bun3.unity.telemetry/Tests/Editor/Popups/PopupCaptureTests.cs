using System;
using System.Collections.Generic;
using System.Linq;
using Bun3.Unity.UI.Popups;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Telemetry.Popups.Tests
{
    public class PopupCaptureTests
    {
        sealed class TestPopup : Popup { }

        sealed class RecordingSink : ITelemetrySink
        {
            public readonly List<TelemetryRecord> Written = new();
            public bool Remote { get; set; }
            public void Write(TelemetryRecord record) => Written.Add(record);
            public void Tick(double now) { }
            public UniTask FlushAsync(TimeSpan timeout) => UniTask.CompletedTask;
        }

        TelemetryHub previousHub;
        RecordingSink sink;
        PopupStack idleStack;
        readonly List<GameObject> created = new();

        [SetUp]
        public void SetUp()
        {
            previousHub = Telemetry.Hub;
            sink = new RecordingSink();
            Telemetry.Hub = new TelemetryBuilder(null).Add(sink, TelemetryTier.All).Build();
            idleStack = new PopupStack((key, ct) => UniTask.FromResult<Popup>(null));
        }

        [TearDown]
        public void TearDown()
        {
            Telemetry.Hub = previousHub;
            idleStack.Dispose();
            foreach (var go in created)
                if (go) UnityEngine.Object.DestroyImmediate(go);
            created.Clear();
        }

        Popup NewPopup(string name = "popup")
        {
            var go = new GameObject(name);
            created.Add(go);
            return go.AddComponent<TestPopup>();
        }

        static object Prop(TelemetryRecord r, string key) => r.Props.FirstOrDefault(p => p.Key == key).Value;

        [Test]
        public void OpenThenClose_RecordsSecondsOpen()
        {
            var times = new Queue<double>(new[] { 10d, 12.5 });
            var capture = new PopupCapture(idleStack, () => times.Dequeue());
            var popup = NewPopup();
            capture.HandleOpened(popup);
            capture.HandleClosed(popup);
            Assert.AreEqual(2, sink.Written.Count);
            Assert.AreEqual("popup_opened", sink.Written[0].Name);
            Assert.AreEqual(TelemetryCategory.Popup, sink.Written[0].Category);
            Assert.AreEqual("popup_closed", sink.Written[1].Name);
            Assert.AreEqual(2.5, Convert.ToDouble(Prop(sink.Written[1], "seconds_open")), 1e-9);
        }

        [Test]
        public void CloseWithoutOpen_OmitsSeconds()
        {
            var capture = new PopupCapture(idleStack, () => 5);
            capture.HandleClosed(NewPopup());
            Assert.AreEqual(1, sink.Written.Count);
            Assert.AreEqual("popup_closed", sink.Written[0].Name);
            Assert.IsNull(Prop(sink.Written[0], "seconds_open"));
        }

        [Test]
        public void ClockThrows_HandlersDoNotThrow()
        {
            var capture = new PopupCapture(idleStack, () => throw new InvalidOperationException("clock"));
            var popup = NewPopup();
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: clock");
            Assert.DoesNotThrow(() => capture.HandleOpened(popup));
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: clock");
            Assert.DoesNotThrow(() => capture.HandleClosed(popup));
        }

        [Test]
        public void InstallUninstall_SubscribesOnce()
        {
            var manager = new PopupManagerBuilder((key, ct) =>
            {
                var go = new GameObject(key.Name);
                created.Add(go);
                return UniTask.FromResult<Popup>(go.AddComponent<TestPopup>());
            }).Build();
            try
            {
                var capture = new PopupCapture(manager.Stack);
                capture.Install();
                capture.Install();
                manager.Stack.Push("p1");
                Assert.AreEqual(1, sink.Written.Count(r => r.Name == "popup_opened"));
                Assert.AreEqual("p1", Prop(sink.Written[0], "popup"));
                manager.Stack.Pop();
                Assert.AreEqual(1, sink.Written.Count(r => r.Name == "popup_closed"));

                capture.Uninstall();
                sink.Written.Clear();
                manager.Stack.Push("p2");
                manager.Stack.Pop();
                Assert.AreEqual(0, sink.Written.Count);
            }
            finally
            {
                manager.Dispose();
            }
        }
    }
}
