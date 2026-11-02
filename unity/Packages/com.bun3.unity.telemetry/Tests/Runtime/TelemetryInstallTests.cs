using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Telemetry.Tests
{
    public class TelemetryInstallTests
    {
        sealed class CountSink : ITelemetrySink
        {
            public readonly List<TelemetryRecord> Written = new();
            public int Ticks;
            public bool Remote => false;
            public void Write(TelemetryRecord record) => Written.Add(record);
            public void Tick(double now) => Ticks++;
            public UniTask FlushAsync(TimeSpan timeout) => UniTask.CompletedTask;
        }

        sealed class FakeCapture : ITelemetryCapture
        {
            public int Installed, Uninstalled;
            public void Install() => Installed++;
            public void Uninstall() => Uninstalled++;
        }

        [TearDown]
        public void TearDown() => Telemetry.Uninstall();

        [UnityTest]
        public IEnumerator Install_TicksSinksEachFrame()
        {
            var sink = new CountSink();
            Telemetry.Install(new TelemetryBuilder(null).Add(sink, TelemetryTier.All));
            yield return null;
            Assert.That(sink.Ticks, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator Uninstall_RemovesRunnerAndHub()
        {
            Telemetry.Install(new TelemetryBuilder(null));
            Assert.That(GameObject.Find("Bun3.Telemetry"), Is.Not.Null);
            Telemetry.Uninstall();
            yield return null;
            Assert.That(GameObject.Find("Bun3.Telemetry"), Is.Null);
            Assert.That(Telemetry.Hub, Is.Null);
        }

        [UnityTest]
        public IEnumerator Reinstall_DoesNotDuplicateCaptures()
        {
            Telemetry.Install(new TelemetryBuilder(null));
            var sink = new CountSink();
            Telemetry.Install(new TelemetryBuilder(null).Add(sink, TelemetryTier.All));
            Debug.LogWarning("reinstall-probe");
            yield return null;
            Assert.That(sink.Written.FindAll(r => r.Name == "engine_log" && Equals(Array.Find(r.Props, p => p.Key == "message").Value, "reinstall-probe")),
                Has.Count.EqualTo(1));
            Assert.That(Object.FindObjectsByType<TelemetryRunner>(), Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Install_InstallsBuilderCaptures()
        {
            var capture = new FakeCapture();
            Telemetry.Install(new TelemetryBuilder(null).Capture(capture));
            yield return null;
            Assert.That(capture.Installed, Is.EqualTo(1));
            Telemetry.Uninstall();
            Assert.That(capture.Uninstalled, Is.EqualTo(1));
        }
    }
}
