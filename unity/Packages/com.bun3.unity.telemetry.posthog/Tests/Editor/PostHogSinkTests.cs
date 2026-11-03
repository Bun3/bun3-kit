using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Telemetry.PostHog.Tests
{
    public class PostHogSinkTests
    {
        sealed class FakeTransport : IPostHogTransport
        {
            public readonly List<(string Url, string Json)> Calls = new();
            public readonly Queue<(int, string)> Responses = new();
            public bool NeverComplete;

            public UniTask<(int Status, string Body)> PostAsync(string url, string json, CancellationToken ct)
            {
                Calls.Add((url, json));
                if (NeverComplete) return new UniTaskCompletionSource<(int, string)>().Task;
                var r = Responses.Count > 0 ? Responses.Dequeue() : (200, "{\"status\":\"Ok\"}");
                return UniTask.FromResult(r);
            }
        }

        const string Host = "https://eu.i.posthog.com";

        static TelemetryRecord Rec(string name, long seq) => new()
        {
            Name = name, Category = TelemetryCategory.Analytics, Utc = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc), Seq = seq,
            Context = new TelemetryContext { DistinctId = "76561198000000000", RunId = "run1", Build = "0.1.0", Environment = "dev", Scene = "Menu", Language = "kr" },
            Props = new (string, object)[] { ("k", "v") },
        };

        static PostHogSink Make(FakeTransport t, int batch = 2, float flush = 10f, int max = 100) =>
            new(Host, "phc_test", batch, flush, max, t);

        static string[] Uuids(string json) =>
            Regex.Matches(json, "\"uuid\":\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

        [Test] public void EngineLog_NotQueued()
        {
            var t = new FakeTransport();
            var hub = new TelemetryBuilder(null).Add(Make(t, batch: 1), TelemetryTier.All).Build();
            hub.Track(TelemetryCategory.EngineLog, "engine_warn");
            Assert.AreEqual(0, hub.Sink<PostHogSink>().QueuedCount);
        }

        [Test] public void DropsEngineLogRecords()
        {
            var t = new FakeTransport(); var s = Make(t, batch: 1);
            var log = Rec("engine_warn", 1); log.Category = TelemetryCategory.EngineLog;
            s.Write(log); s.Write(Rec("kept", 2)); s.Tick(0);
            Assert.AreEqual(1, t.Calls.Count);
            StringAssert.Contains("\"event\":\"kept\"", t.Calls[0].Json);
            StringAssert.DoesNotContain("engine_warn", t.Calls[0].Json);
        }

        [Test] public void Body_ContainsExtra_SkipsNull()
        {
            var t = new FakeTransport(); var s = Make(t, batch: 1);
            var r = Rec("a", 1);
            r.Context.Extra = new (string, object)[] { ("match_id", "M"), ("room", null) };
            s.Write(r); s.Tick(0);
            StringAssert.Contains("\"match_id\":\"M\"", t.Calls[0].Json);
            StringAssert.DoesNotContain("room", t.Calls[0].Json);
        }

        static TelemetryEnvironmentOptions Env(PostHogSinkOptions o) =>
            new() { Name = "dev", Sinks = o == null ? new() : new() { o } };

        static PostHogSinkOptions Opts(TelemetryTier tiers, string key = "phc_test") => new() { Tiers = tiers, Key = key };

        [Test] public void AddPostHog_MissingOptions_NotAttached() =>
            Assert.IsNull(new TelemetryBuilder(Env(null)).AddPostHog(new FakeTransport()).Build().Sink<PostHogSink>());

        [Test] public void AddPostHog_EmptyKey_NotAttached() =>
            Assert.IsNull(new TelemetryBuilder(Env(Opts(TelemetryTier.All, ""))).AddPostHog(new FakeTransport()).Build().Sink<PostHogSink>());

        [Test] public void AddPostHog_NoneTiers_NotAttached() =>
            Assert.IsNull(new TelemetryBuilder(Env(Opts(TelemetryTier.None))).AddPostHog(new FakeTransport()).Build().Sink<PostHogSink>());

        [Test] public void AddPostHog_Attached_UsesOptionTiers()
        {
            var hub = new TelemetryBuilder(Env(Opts(TelemetryTier.Analytics))).AddPostHog(new FakeTransport()).Build();
            var sink = hub.Sink<PostHogSink>();
            Assert.IsNotNull(sink);
            hub.Track(TelemetryCategory.Ui, "click");
            Assert.AreEqual(0, sink.QueuedCount);
            hub.Track(TelemetryCategory.Analytics, "a");
            Assert.AreEqual(1, sink.QueuedCount);
        }

        [Test] public void BatchBodyHasApiKeyAndAnonymousFlag()
        {
            var t = new FakeTransport(); var s = Make(t);
            s.Write(Rec("a", 1)); s.Write(Rec("b", 2)); s.Tick(0);
            Assert.AreEqual(1, t.Calls.Count);
            Assert.AreEqual("https://eu.i.posthog.com/batch/", t.Calls[0].Url);
            var j = t.Calls[0].Json;
            StringAssert.Contains("\"api_key\":\"phc_test\"", j);
            StringAssert.Contains("\"$process_person_profile\":false", j);
            StringAssert.Contains("\"distinct_id\":\"76561198000000000\"", j);
            StringAssert.Contains("\"event\":\"a\"", j);
            StringAssert.Contains("\"timestamp\":\"2026-10-29T12:00:00.000Z\"", j);
            StringAssert.Contains("\"seq\":1", j);
            StringAssert.Contains("\"k\":\"v\"", j);
            Assert.IsFalse(j.Contains("lobby_id"));
        }

        [Test] public void SendsWhenBatchSizeReached()
        {
            var t = new FakeTransport(); var s = Make(t);
            s.Write(Rec("a", 1)); s.Tick(0);
            Assert.AreEqual(0, t.Calls.Count);
            s.Write(Rec("b", 2)); s.Tick(0);
            Assert.AreEqual(1, t.Calls.Count);
        }

        [Test] public void SendsOnFlushInterval()
        {
            var t = new FakeTransport(); var s = Make(t, 100);
            s.Write(Rec("a", 1)); s.Tick(0);
            Assert.AreEqual(0, t.Calls.Count);
            s.Tick(10.1);
            Assert.AreEqual(1, t.Calls.Count);
        }

        [Test] public void QuotaLimitedStopsForRun()
        {
            var t = new FakeTransport(); var s = Make(t, 1);
            t.Responses.Enqueue((200, "{\"status\":1,\"quota_limited\":[\"events\"]}"));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Bun3\.Telemetry\] PostHog"));
            s.Write(Rec("a", 1)); s.Tick(0);
            s.Write(Rec("b", 2)); s.Tick(100); s.Tick(200);
            Assert.AreEqual(1, t.Calls.Count);
        }

        [Test] public void UnauthorizedStopsForRun()
        {
            var t = new FakeTransport(); var s = Make(t, 1);
            t.Responses.Enqueue((401, "invalid key"));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Bun3\.Telemetry\] PostHog 401"));
            s.Write(Rec("a", 1)); s.Write(Rec("b", 2)); s.Tick(0);
            Assert.AreEqual(0, s.QueuedCount, "the rest of the queue is dropped too");
            s.Write(Rec("c", 3)); s.Tick(100); s.Tick(200);
            Assert.AreEqual(0, s.QueuedCount);
            Assert.AreEqual(1, t.Calls.Count);
            LogAssert.NoUnexpectedReceived();
        }

        [Test] public void BadRequestDropsWithoutRetry()
        {
            var t = new FakeTransport(); var s = Make(t, 1);
            t.Responses.Enqueue((400, "bad"));
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[Bun3\.Telemetry\] PostHog"));
            s.Write(Rec("a", 1)); s.Tick(0);
            Assert.AreEqual(0, s.QueuedCount);
            s.Tick(100);
            Assert.AreEqual(1, t.Calls.Count);
        }

        [Test] public void RetryKeepsSameEventUuids()
        {
            var t = new FakeTransport(); var s = Make(t);
            t.Responses.Enqueue((503, ""));
            s.Write(Rec("a", 1)); s.Write(Rec("b", 2)); s.Tick(0);
            Assert.AreEqual(2, s.QueuedCount);
            s.Tick(1);
            Assert.AreEqual(1, t.Calls.Count, "a retry waits for the next interval");
            s.Tick(11);
            Assert.AreEqual(2, t.Calls.Count);
            Assert.AreEqual(Uuids(t.Calls[0].Json), Uuids(t.Calls[1].Json));
            Assert.AreEqual(0, s.QueuedCount);
        }

        [Test] public void QueueCapDropsOldest()
        {
            var t = new FakeTransport(); var s = Make(t, 100, 10f, 3);
            for (var i = 1; i <= 5; i++) s.Write(Rec("e" + i, i));
            Assert.AreEqual(3, s.QueuedCount);
            s.Tick(0); s.Tick(10.1);
            var j = t.Calls[0].Json;
            Assert.IsFalse(j.Contains("\"event\":\"e2\""));
            StringAssert.Contains("\"event\":\"e3\"", j);
            StringAssert.Contains("\"event\":\"e5\"", j);
        }

        [Test] public void OnlyOneRequestInFlight()
        {
            var t = new FakeTransport { NeverComplete = true }; var s = Make(t, 1);
            s.Write(Rec("a", 1)); s.Tick(0); s.Tick(1); s.Tick(50); s.Tick(100);
            Assert.AreEqual(1, t.Calls.Count);
        }

        [Test] public void FlushAsyncSendsRemainingBatches()
        {
            var t = new FakeTransport(); var s = Make(t, 2);
            for (var i = 1; i <= 5; i++) s.Write(Rec("e" + i, i));
            s.FlushAsync(TimeSpan.FromSeconds(1)).Forget();
            Assert.AreEqual(3, t.Calls.Count);
            Assert.AreEqual(0, s.QueuedCount);
        }

        [TestCase(200, "{\"status\":\"Ok\"}", PostHogOutcome.Sent)]
        [TestCase(200, "{\"quota_limited\":[\"events\"]}", PostHogOutcome.QuotaLimited)]
        [TestCase(413, "", PostHogOutcome.Drop)]
        [TestCase(401, "", PostHogOutcome.Unauthorized)]
        [TestCase(400, "", PostHogOutcome.Drop)]
        [TestCase(404, "", PostHogOutcome.Drop)]
        [TestCase(429, "", PostHogOutcome.Retry)]
        [TestCase(503, "", PostHogOutcome.Retry)]
        [TestCase(0, "", PostHogOutcome.Retry)]
        public void ClassifyStatus(int status, string body, PostHogOutcome expected) =>
            Assert.AreEqual(expected, PostHogSink.Classify(status, body));
    }
}
