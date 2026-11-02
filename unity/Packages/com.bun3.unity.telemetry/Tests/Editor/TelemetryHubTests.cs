using System;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Telemetry.Tests
{
    public class TelemetryHubTests
    {
        static readonly DateTime Now = new(2026, 10, 29, 1, 2, 3, DateTimeKind.Utc);
        static readonly Regex Prefix = new(@"^\[Bun3\.Telemetry\]");
        TelemetryHub previousHub;

        [SetUp] public void SetUp() => previousHub = Telemetry.Hub;
        [TearDown] public void TearDown() => Telemetry.Hub = previousHub;

        static TelemetryBuilder Base() =>
            new TelemetryBuilder(new TelemetryEnvironmentOptions { Name = "dev" })
                .WithClock(() => Now)
                .WithScene(() => "Menu");

        sealed class ThrowingSink : ListSink
        {
            public override void Write(TelemetryRecord record) => throw new InvalidOperationException("boom");
        }

        [Test]
        public void SinkReceivesOnlyItsTiers()
        {
            var local = new ListSink();
            var ph = new ListSink();
            var hub = Base().Add(local, TelemetryTier.Analytics | TelemetryTier.Debug).Add(ph, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Ui, "x");
            Assert.That(local.Written.Count, Is.EqualTo(1));
            Assert.That(ph.Written, Is.Empty);
        }

        [Test]
        public void RoutesByTier()
        {
            var analytics = new ListSink();
            var activity = new ListSink();
            var hub = Base().Add(analytics, TelemetryTier.Analytics).Add(activity, TelemetryTier.Activity).Build();
            hub.Track(TelemetryCategory.Ui, "ui");
            hub.Track(TelemetryCategory.Activity, "act");
            Assert.That(analytics.Written, Is.Empty);
            Assert.That(activity.Written.Count, Is.EqualTo(1));
            Assert.That(activity.Written[0].Name, Is.EqualTo("act"));
        }

        [Test]
        public void RecordCarriesContextSeqAndUtc()
        {
            var sink = new ListSink();
            var hub = Base()
                .Context((ref TelemetryContext c) => c.DistinctId = "d1")
                .Add(sink, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            hub.Track(TelemetryCategory.Analytics, "b");
            Assert.That(sink.Written[0].Seq, Is.EqualTo(1));
            Assert.That(sink.Written[1].Seq, Is.EqualTo(2));
            Assert.That(sink.Written[1].Context.DistinctId, Is.EqualTo("d1"));
            Assert.That(sink.Written[1].Utc, Is.EqualTo(Now));
            Assert.That(sink.Written[0].Name, Is.EqualTo("a"));
        }

        [Test]
        public void KitFillsRunIdBuildEnvironmentScene()
        {
            var sink = new ListSink();
            var hub = Base().RunId("r1").Add(sink, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            var c = sink.Written[0].Context;
            Assert.That(c.RunId, Is.EqualTo("r1"));
            Assert.That(c.Environment, Is.EqualTo("dev"));
            Assert.That(c.Scene, Is.EqualTo("Menu"));
            Assert.That(c.Build, Is.EqualTo(Application.version));
        }

        [Test]
        public void MissingRunId_IsGenerated()
        {
            var sink = new ListSink();
            var hub = Base().Add(sink, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            Assert.That(sink.Written[0].Context.RunId, Has.Length.EqualTo(32));
        }

        [Test]
        public void ContextFillThrows_RecordKept()
        {
            var sink = new ListSink();
            var hub = Base().RunId("r1")
                .Context((ref TelemetryContext c) => throw new InvalidOperationException("fill"))
                .Add(sink, TelemetryTier.Analytics).Build();
            LogAssert.Expect(LogType.Warning, Prefix);
            hub.Track(TelemetryCategory.Analytics, "a");
            hub.Track(TelemetryCategory.Analytics, "b");
            LogAssert.NoUnexpectedReceived();
            Assert.That(sink.Written.Count, Is.EqualTo(2));
            Assert.That(sink.Written[0].Context.RunId, Is.EqualTo("r1"));
            Assert.That(sink.Written[0].Context.Environment, Is.EqualTo("dev"));
        }

        [Test]
        public void SceneReadThrows_RecordKeptWithoutScene()
        {
            var sink = new ListSink();
            var hub = Base().WithScene(() => throw new InvalidOperationException("scene"))
                .Add(sink, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            Assert.That(sink.Written.Count, Is.EqualTo(1));
            Assert.That(sink.Written[0].Context.Scene, Is.Null);
        }

        [Test]
        public void NullExtraValues_AreRemoved()
        {
            var sink = new ListSink();
            var hub = Base()
                .Context((ref TelemetryContext c) => c.Extra = new (string, object)[] { ("a", 1), ("b", null) })
                .Add(sink, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            Assert.That(sink.Written[0].Context.Extra.Length, Is.EqualTo(1));
            Assert.That(sink.Written[0].Context.Get("a"), Is.EqualTo(1));
            Assert.That(sink.Written[0].Context.Get("b"), Is.Null);
        }

        [Test]
        public void DropKeysRemovedAndWarnedOnce()
        {
            var sink = new ListSink();
            var hub = Base().DropKeys("chat_text", "token").Add(sink, TelemetryTier.Analytics).Build();
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Bun3\.Telemetry\].*chat_text"));
            hub.Track(TelemetryCategory.Analytics, "a", ("chat_text", "hi"), ("nickname", "n"));
            hub.Track(TelemetryCategory.Analytics, "b", ("chat_text", "again"));
            LogAssert.NoUnexpectedReceived();
            var props = sink.Written[0].Props;
            Assert.That(props.Length, Is.EqualTo(1));
            Assert.That(props[0].Key, Is.EqualTo("nickname"));
            Assert.That(sink.Written[1].Props, Is.Empty);
        }

        [Test]
        public void ConsentOffSkipsRemoteSinksAndTicks()
        {
            var local = new ListSink();
            var remote = new ListSink { Remote = true };
            var hub = Base().Consent(() => false).Add(local, TelemetryTier.Analytics).Add(remote, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            hub.Tick(1);
            Assert.That(remote.Written, Is.Empty);
            Assert.That(remote.Ticks, Is.EqualTo(0));
            Assert.That(local.Written.Count, Is.EqualTo(1));
            Assert.That(local.Ticks, Is.EqualTo(1));
        }

        [Test]
        public void RemoteSinkSkippedWithoutConsent()
        {
            var local = new ListSink();
            var remote = new ListSink { Remote = true };
            var hub = Base().Consent(() => false).Add(remote, TelemetryTier.Analytics).Add(local, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            Assert.That(remote.Written.Count, Is.EqualTo(0));
            Assert.That(local.Written.Count, Is.EqualTo(1));
        }

        [Test]
        public void NoConsentCallback_MeansConsent()
        {
            var remote = new ListSink { Remote = true };
            var hub = Base().Add(remote, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            Assert.That(remote.Written.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConsentThrows_TreatedAsNo()
        {
            var remote = new ListSink { Remote = true };
            var hub = Base().Consent(() => throw new InvalidOperationException()).Add(remote, TelemetryTier.Analytics).Build();
            Assert.DoesNotThrow(() => hub.Track(TelemetryCategory.Analytics, "a"));
            Assert.That(remote.Written, Is.Empty);
        }

        [Test]
        public void SinkThrows_OthersStillWrite_WarnedOnce()
        {
            var good = new ListSink();
            var hub = Base().Add(new ThrowingSink(), TelemetryTier.Analytics).Add(good, TelemetryTier.Analytics).Build();
            LogAssert.Expect(LogType.Warning, Prefix);
            Assert.DoesNotThrow(() => hub.Track(TelemetryCategory.Analytics, "a"));
            Assert.DoesNotThrow(() => hub.Track(TelemetryCategory.Analytics, "b"));
            LogAssert.NoUnexpectedReceived();
            Assert.That(good.Written.Count, Is.EqualTo(2));
        }

        [Test]
        public void SinkOfType_ReturnsFirstOrNull()
        {
            var first = new ListSink();
            var hub = Base().Add(first, TelemetryTier.Analytics).Add(new ListSink(), TelemetryTier.Analytics).Build();
            Assert.That(hub.Sink<ListSink>(), Is.SameAs(first));
            Assert.That(hub.Sink<ConsoleSink>(), Is.Null);
        }

        [Test]
        public void TrackWithoutHub_IsNoop()
        {
            Telemetry.Hub = null;
            Assert.DoesNotThrow(() => Telemetry.Track(TelemetryCategory.Analytics, "a", ("k", 1)));
            Assert.DoesNotThrow(() => Telemetry.FlushAsync(TimeSpan.FromSeconds(1)).Forget());
        }
    }
}
