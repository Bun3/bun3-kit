using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Telemetry.GameAnalytics.Tests
{
    public class GameAnalyticsSinkTests
    {
        static readonly Regex Warning = new(@"^\[Bun3\.Telemetry\] GameAnalytics");

        sealed class FakeApi : IGameAnalyticsApi
        {
            public readonly List<string> Calls = new();
            public string ThrowOn;

            void Log(string name, params object[] args)
            {
                var parts = new List<string> { name };
                foreach (var a in args) parts.Add(a == null ? "-" : Convert.ToString(a, CultureInfo.InvariantCulture));
                Calls.Add(string.Join(" ", parts));
                if (name == ThrowOn) throw new InvalidOperationException(name + " failed");
            }

            public void SetCustomId(string id) => Log("SetCustomId", id);
            public void SetBuild(string build) => Log("SetBuild", build);
            public void SetEnvironment(string environment) => Log("SetEnvironment", environment);
            public void SetKeys(string gameKey, string secretKey) => Log("SetKeys", gameKey, secretKey);
            public void DisableErrorReporting() => Log("DisableErrorReporting");
            public void Initialize() => Log("Initialize");
            public void SetEnabled(bool enabled) => Log("SetEnabled", enabled);
            public void Design(string id, float? value) => Log("Design", id, value);
            public void Progression(GameAnalyticsEventKind status, string p1, string p2, string p3) => Log("Progression", status, p1, p2, p3);
        }

        static TelemetryRecord Record(string name, string distinctId = "7656") => new()
        {
            Name = name,
            Category = TelemetryCategory.Analytics,
            Context = new TelemetryContext { DistinctId = distinctId, Build = "0.50.1", Environment = "playtest" },
        };

        static GameAnalyticsSink Sink(FakeApi api, GameAnalyticsMap map = null, Func<bool> consent = null) =>
            new("g", "s", map, api, consent ?? (() => true));

        [Test]
        public void NothingHappensBeforeTheFirstRecord()
        {
            var api = new FakeApi();
            var sink = Sink(api);
            Telemetry.NotifyConsentChanged();
            Assert.That(sink.Remote, Is.True);
            Assert.That(api.Calls, Is.Empty);
        }

        [Test]
        public void FirstRecordInitializesInOrder()
        {
            var api = new FakeApi();
            var sink = Sink(api);
            sink.Write(Record("app_started"));
            Assert.That(api.Calls, Is.EqualTo(new[]
            {
                "SetCustomId 7656", "SetBuild 0.50.1", "SetEnvironment playtest", "SetKeys g s",
                "DisableErrorReporting", "Initialize", "Design app_started -",
            }));
            sink.Write(Record("lobby_left"));
            Assert.That(api.Calls[^1], Is.EqualTo("Design lobby_left -"));
            Assert.That(api.Calls.Count, Is.EqualTo(8), "Initializes once");
        }

        [Test]
        public void EmptyDistinctIdKeepsSdkId()
        {
            var api = new FakeApi();
            Sink(api).Write(Record("app_started", distinctId: ""));
            Assert.That(api.Calls, Has.None.StartsWith("SetCustomId"));
            Assert.That(api.Calls, Has.Member("Initialize"));
        }

        [Test]
        public void MapSkipAndManyEvents()
        {
            var api = new FakeApi();
            var sink = Sink(api, (r, output) =>
            {
                if (r.Name == "b")
                {
                    output.Add(GameAnalyticsEvent.Progression(GameAnalyticsEventKind.ProgressionFail, "match"));
                    output.Add(GameAnalyticsEvent.Design("match_end:expert:left", 3.5));
                }
                return true;
            });
            sink.Write(Record("a"));
            sink.Write(Record("b"));
            Assert.That(api.Calls[^2], Is.EqualTo("Progression ProgressionFail match - -"));
            Assert.That(api.Calls[^1], Is.EqualTo("Design match_end:expert:left 3.5"));
            Assert.That(api.Calls, Has.None.Contains(" a "), "true with no events skips the record");
        }

        [Test]
        public void SanitizedIdsReachTheSdk()
        {
            var api = new FakeApi();
            var sink = Sink(api, (r, output) =>
            {
                output.Add(GameAnalyticsEvent.Design("bad#id", double.NaN));
                return true;
            });
            LogAssert.Expect(LogType.Warning, Warning);
            sink.Write(Record("x"));
            sink.Write(Record("y"));
            Assert.That(api.Calls[^1], Is.EqualTo("Design bad_id -"), "Cleaned id, NaN value dropped");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ConsentChangeTogglesSubmissionAfterInit()
        {
            var api = new FakeApi();
            var allowed = true;
            var sink = Sink(api, consent: () => allowed);
            sink.Write(Record("app_started"));
            allowed = false;
            Telemetry.NotifyConsentChanged();
            Assert.That(api.Calls[^1], Is.EqualTo("SetEnabled False"));
            allowed = true;
            Telemetry.NotifyConsentChanged();
            Assert.That(api.Calls[^1], Is.EqualTo("SetEnabled True"));
        }

        [Test]
        public void SdkFailureStopsTheSinkOnce()
        {
            var api = new FakeApi { ThrowOn = "Initialize" };
            var sink = Sink(api);
            LogAssert.Expect(LogType.Warning, Warning);
            Assert.DoesNotThrow(() => sink.Write(Record("app_started")));
            var count = api.Calls.Count;
            sink.Write(Record("lobby_left"));
            Telemetry.NotifyConsentChanged();
            Assert.That(api.Calls.Count, Is.EqualTo(count), "Stopped for the run");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AddGameAnalyticsNeedsBothKeys()
        {
            var api = new FakeApi();
            TelemetryHub Hub(GameAnalyticsSinkOptions options)
            {
                var environment = new TelemetryEnvironmentOptions { Name = "playtest" };
                if (options != null) environment.Sinks.Add(options);
                return new TelemetryBuilder(environment).AddGameAnalytics(api: api).Build();
            }

            Assert.That(Hub(null).Sink<GameAnalyticsSink>(), Is.Null);
            Assert.That(Hub(new GameAnalyticsSinkOptions { GameKey = "g" }).Sink<GameAnalyticsSink>(), Is.Null);
            Assert.That(Hub(new GameAnalyticsSinkOptions { SecretKey = "s" }).Sink<GameAnalyticsSink>(), Is.Null);
            Assert.That(Hub(new GameAnalyticsSinkOptions { GameKey = "g", SecretKey = "s", Tiers = TelemetryTier.None }).Sink<GameAnalyticsSink>(), Is.Null);

            var hub = Hub(new GameAnalyticsSinkOptions { GameKey = "g", SecretKey = "s" });
            Assert.That(hub.Sink<GameAnalyticsSink>(), Is.Not.Null);
            hub.Track(TelemetryCategory.Ui, "ui_click");
            Assert.That(api.Calls, Is.Empty, "Analytics tier only by default");
            hub.Track(TelemetryCategory.Analytics, "app_started");
            Assert.That(api.Calls, Has.Member("Initialize"));
        }

        [Test]
        public void MapFailureDropsOnlyThatRecord()
        {
            var api = new FakeApi();
            var sink = Sink(api, (r, output) => r.Name == "x" ? throw new InvalidOperationException("map") : false);
            LogAssert.Expect(LogType.Warning, Warning);
            sink.Write(Record("x"));
            sink.Write(Record("y"));
            Assert.That(api.Calls, Has.None.EqualTo("Design x -"));
            Assert.That(api.Calls[^1], Is.EqualTo("Design y -"));
        }
    }
}
