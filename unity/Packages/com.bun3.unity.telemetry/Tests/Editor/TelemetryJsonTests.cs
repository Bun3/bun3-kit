using System;
using System.Globalization;
using Cysharp.Text;
using NUnit.Framework;

namespace Bun3.Unity.Telemetry.Tests
{
    public class TelemetryJsonTests
    {
        CultureInfo previous;
        [SetUp] public void SetUp() => previous = CultureInfo.CurrentCulture;
        [TearDown] public void TearDown() => CultureInfo.CurrentCulture = previous;

        static string Str(string v)
        {
            var sb = ZString.CreateStringBuilder();
            try { TelemetryJson.WriteString(ref sb, v); return sb.ToString(); } finally { sb.Dispose(); }
        }

        static string Val(object v)
        {
            var sb = ZString.CreateStringBuilder();
            try { TelemetryJson.WriteValue(ref sb, v); return sb.ToString(); } finally { sb.Dispose(); }
        }

        [Test] public void EscapesQuotesNewlinesAndControlChars() =>
            Assert.AreEqual("\"a\\\"b\\\\c\\nd\\te\\u0001한\"", Str("a\"b\\c\nd\te\u0001한"));

        [Test] public void WritesNumbersWithInvariantCulture()
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.AreEqual("1.5", Val(1.5f));
            Assert.AreEqual("1.5", Val(1.5d));
            Assert.AreEqual("null", Val(float.NaN));
            Assert.AreEqual("3", Val(3));
        }

        [Test] public void ActivityRecordWritesActivityTier()
        {
            var line = TelemetryJson.RecordLine(new TelemetryRecord { Name = "a", Category = TelemetryCategory.Activity, Utc = DateTime.UtcNow });
            StringAssert.Contains("\"tier\":\"activity\"", line);
            StringAssert.Contains("\"category\":\"activity\"", line);
        }

        [Test] public void RecordLineHasFixedKeysAndOmitsNullExtra()
        {
            var r = new TelemetryRecord
            {
                Name = "lobby_join_finished", Category = TelemetryCategory.Analytics,
                Utc = new DateTime(2026, 10, 29, 12, 3, 43, 120, DateTimeKind.Utc), Seq = 14,
                Context = new TelemetryContext
                {
                    DistinctId = "d", RunId = "r", Build = "b", Environment = "editor", Scene = "Menu", Language = "kr",
                    Extra = new (string, object)[] { ("is_host", false) },
                },
                Props = new (string, object)[] { ("path", "list"), ("count", 3), ("ok", true) },
            };
            Assert.AreEqual(
                "{\"ts\":\"2026-10-29T12:03:43.120Z\",\"seq\":14,\"tier\":\"analytics\",\"category\":\"analytics\"," +
                "\"event\":\"lobby_join_finished\",\"distinct_id\":\"d\",\"run_id\":\"r\",\"build\":\"b\"," +
                "\"environment\":\"editor\",\"scene\":\"Menu\",\"language\":\"kr\",\"is_host\":false," +
                "\"props\":{\"path\":\"list\",\"count\":3,\"ok\":true}}",
                TelemetryJson.RecordLine(r));
        }

        [Test] public void ExtraWrittenInOrder_NullValuesSkipped()
        {
            var line = TelemetryJson.RecordLine(new TelemetryRecord
            {
                Name = "a", Category = TelemetryCategory.Activity, Utc = DateTime.UtcNow,
                Context = new TelemetryContext
                {
                    Extra = new (string, object)[] { ("lobby_id", "L"), ("match_id", null), ("is_host", true) },
                },
            });
            StringAssert.Contains(",\"lobby_id\":\"L\",\"is_host\":true,", line);
            StringAssert.DoesNotContain("match_id", line);
        }

        [Test] public void NoExtra_WritesNoExtraKeys()
        {
            var line = TelemetryJson.RecordLine(new TelemetryRecord { Name = "a", Category = TelemetryCategory.Activity, Utc = DateTime.UtcNow });
            StringAssert.Contains("\"language\":null,\"props\":{}}", line);
        }
    }
}
