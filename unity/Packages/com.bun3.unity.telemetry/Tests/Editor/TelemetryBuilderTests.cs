using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Telemetry.Tests
{
    public class TelemetryBuilderTests
    {
        static TelemetryEnvironmentOptions Env(params TelemetrySinkOptions[] sinks)
        {
            var env = new TelemetryEnvironmentOptions { Name = "dev" };
            env.Sinks.AddRange(sinks);
            return env;
        }

        [Test]
        public void AddConsole_MissingOptions_NotAttached()
        {
            var hub = new TelemetryBuilder(Env()).AddConsole().Build();
            Assert.That(hub.Sink<ConsoleSink>(), Is.Null);
        }

        [Test]
        public void AddConsole_NoneTiers_NotAttached()
        {
            var hub = new TelemetryBuilder(Env(new ConsoleSinkOptions { Tiers = TelemetryTier.None })).AddConsole().Build();
            Assert.That(hub.Sink<ConsoleSink>(), Is.Null);
        }

        [Test]
        public void AddConsole_WithTiers_AttachedWithThoseTiers()
        {
            var hub = new TelemetryBuilder(Env(new ConsoleSinkOptions { Tiers = TelemetryTier.Debug })).AddConsole().Build();
            Assert.That(hub.Sink<ConsoleSink>(), Is.Not.Null);
            hub.Track(TelemetryCategory.Analytics, "not_printed");
            LogAssert.NoUnexpectedReceived();
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Bun3\.Telemetry\] .*""event"":""printed"""));
            hub.Track(TelemetryCategory.Ui, "printed");
        }

        [Test]
        public void NullEnvironment_BuildsWithExplicitSinksOnly()
        {
            var sink = new ListSink();
            var builder = new TelemetryBuilder(null);
            var hub = builder.AddConsole().Add(sink, TelemetryTier.Analytics).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            Assert.That(builder.Environment, Is.Null);
            Assert.That(hub.Sink<ConsoleSink>(), Is.Null);
            Assert.That(sink.Written.Count, Is.EqualTo(1));
            Assert.That(sink.Written[0].Context.Environment, Is.Null);
        }

        [Test]
        public void Add_NoneTiers_NotAttached()
        {
            var sink = new ListSink();
            var hub = new TelemetryBuilder(null).Add(sink, TelemetryTier.None).Build();
            hub.Track(TelemetryCategory.Analytics, "a");
            Assert.That(hub.Sink<ListSink>(), Is.Null);
            Assert.That(sink.Written, Is.Empty);
        }
    }
}
