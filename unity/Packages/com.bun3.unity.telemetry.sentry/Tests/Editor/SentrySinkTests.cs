using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace Bun3.Unity.Telemetry.Sentry.Tests
{
    public class SentrySinkTests
    {
        sealed class FakeSentryApi : ISentryApi
        {
            public readonly List<(string Message, string Category, IDictionary<string, string> Data, bool Info)> Crumbs = new();
            public readonly List<(string Key, string Value)> Tags = new();
            public readonly List<string> Users = new();

            public void AddBreadcrumb(string message, string category, IDictionary<string, string> data, bool info) =>
                Crumbs.Add((message, category, data, info));
            public void SetTag(string key, string value) => Tags.Add((key, value));
            public void SetUser(string id) => Users.Add(id);
            public void SetContext(string key, IDictionary<string, object> values) { }
        }

        static TelemetryContext Ctx(string distinct = "dev-a", string match = null) => new()
        {
            DistinctId = distinct, RunId = "run1", Build = "0.1.0", Environment = "dev", Scene = "Menu", Language = "kr",
            Extra = new (string, object)[] { ("lobby_id", "L1"), ("match_id", match) },
        };

        static TelemetryRecord Rec(TelemetryCategory category, string name, TelemetryContext context,
            params (string, object)[] props) => new()
        {
            Name = name, Category = category, Context = context, Props = props,
        };

        [Test] public void AnalyticsRecordBecomesInfoBreadcrumb()
        {
            var api = new FakeSentryApi();
            var sink = new SentrySink(api, null, null);
            Assert.IsTrue(sink.Remote);

            var culture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                sink.Write(Rec(TelemetryCategory.Analytics, "lobby_join_finished", Ctx(),
                    ("path", "list"), ("public", true), ("duration_ms", 1250), ("ratio", 1.5)));
            }
            finally { Thread.CurrentThread.CurrentCulture = culture; }

            Assert.AreEqual(1, api.Crumbs.Count);
            var c = api.Crumbs[0];
            Assert.AreEqual("lobby_join_finished", c.Message);
            Assert.AreEqual("analytics", c.Category);
            Assert.IsTrue(c.Info);
            CollectionAssert.AreEquivalent(new Dictionary<string, string>
            {
                { "path", "list" }, { "public", "true" }, { "duration_ms", "1250" }, { "ratio", "1.5" },
            }, c.Data);
        }

        [Test] public void DebugRecordUsesCategoryName()
        {
            var api = new FakeSentryApi();
            new SentrySink(api, null, null).Write(Rec(TelemetryCategory.Ui, "button_clicked", Ctx(), ("path", "Menu/Play")));

            Assert.AreEqual(1, api.Crumbs.Count);
            Assert.AreEqual("ui", api.Crumbs[0].Category);
            Assert.IsFalse(api.Crumbs[0].Info);
        }

        [Test] public void StaticTagsSetOnce()
        {
            var api = new FakeSentryApi();
            var sink = new SentrySink(api, null, null);
            sink.Write(Rec(TelemetryCategory.Ui, "a", Ctx()));
            sink.Write(Rec(TelemetryCategory.Ui, "b", Ctx()));

            CollectionAssert.AreEqual(new (string, string)[] { ("run_id", "run1"), ("build", "0.1.0"), ("environment", "dev") }, api.Tags);
        }

        [Test] public void TagKeys_SetFromExtra_UpdatedOnChange()
        {
            var api = new FakeSentryApi();
            var sink = new SentrySink(api, new[] { "lobby_id", "match_id" }, null);
            sink.Write(Rec(TelemetryCategory.Ui, "a", Ctx(match: "M1")));
            sink.Write(Rec(TelemetryCategory.Ui, "b", Ctx(match: "M1")));
            sink.Write(Rec(TelemetryCategory.Ui, "c", Ctx(match: "M2")));
            sink.Write(Rec(TelemetryCategory.Ui, "d", Ctx(match: null)));

            CollectionAssert.AreEqual(new (string, string)[]
            {
                ("run_id", "run1"), ("build", "0.1.0"), ("environment", "dev"),
                ("lobby_id", "L1"), ("match_id", "M1"), ("match_id", "M2"), ("match_id", null),
            }, api.Tags);
        }

        [Test] public void UserSetWhenDistinctIdChanges()
        {
            var api = new FakeSentryApi();
            var sink = new SentrySink(api, null, null);
            sink.Write(Rec(TelemetryCategory.Ui, "a", Ctx("dev-a")));
            sink.Write(Rec(TelemetryCategory.Ui, "b", Ctx("dev-a")));
            sink.Write(Rec(TelemetryCategory.Ui, "c", Ctx("76561198000000000")));

            CollectionAssert.AreEqual(new[] { "dev-a", "76561198000000000" }, api.Users);
        }

        [Test] public void Hook_HandledRecord_SkipsBreadcrumb()
        {
            var api = new FakeSentryApi();
            var seen = new List<string>();
            var sink = new SentrySink(api, null, (r, a) =>
            {
                seen.Add(r.Name);
                return r.Name == "handled";
            });
            sink.Write(Rec(TelemetryCategory.Ui, "handled", Ctx()));
            sink.Write(Rec(TelemetryCategory.Ui, "plain", Ctx()));

            CollectionAssert.AreEqual(new[] { "handled", "plain" }, seen);
            Assert.AreEqual(1, api.Crumbs.Count);
            Assert.AreEqual("plain", api.Crumbs[0].Message);
        }

        [Test] public void EngineLog_AddedAsBreadcrumb()
        {
            var api = new FakeSentryApi();
            new SentrySink(api, null, null).Write(Rec(TelemetryCategory.EngineLog, "engine_log", Ctx(), ("message", "x")));

            Assert.AreEqual(1, api.Crumbs.Count);
            Assert.AreEqual("engine_log", api.Crumbs[0].Category);
            Assert.IsFalse(api.Crumbs[0].Info);
        }

        static TelemetryEnvironmentOptions Env(SentrySinkOptions options) =>
            new() { Name = "dev", Sinks = options == null ? new List<TelemetrySinkOptions>() : new List<TelemetrySinkOptions> { options } };

        static SentrySink Attached(TelemetryEnvironmentOptions env) =>
            new TelemetryBuilder(env).AddSentry(api: new FakeSentryApi()).Build().Sink<SentrySink>();

        [Test] public void AddSentry_Configured_Attached() =>
            Assert.IsNotNull(Attached(Env(new SentrySinkOptions { Dsn = "dsn", Tiers = TelemetryTier.All })));

        [Test] public void AddSentry_MissingOptions_NotAttached() => Assert.IsNull(Attached(Env(null)));

        [Test] public void AddSentry_NullEnvironment_NotAttached() => Assert.IsNull(Attached(null));

        [Test] public void AddSentry_EmptyDsn_NotAttached() =>
            Assert.IsNull(Attached(Env(new SentrySinkOptions { Dsn = "", Tiers = TelemetryTier.All })));

        [Test] public void AddSentry_NoneTiers_NotAttached() =>
            Assert.IsNull(Attached(Env(new SentrySinkOptions { Dsn = "dsn", Tiers = TelemetryTier.None })));
    }
}
