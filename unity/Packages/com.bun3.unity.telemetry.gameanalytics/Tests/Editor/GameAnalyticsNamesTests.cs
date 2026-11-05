using NUnit.Framework;

namespace Bun3.Unity.Telemetry.GameAnalytics.Tests
{
    public class GameAnalyticsNamesTests
    {
        static string Clean(string id, int max, out bool changed) => GameAnalyticsNames.Clean(id, max, out changed);

        [Test]
        public void ValidIdIsUnchanged()
        {
            Assert.That(Clean("mission:reports:success", 5, out var changed), Is.EqualTo("mission:reports:success"));
            Assert.That(changed, Is.False);
        }

        [Test]
        public void RejectedCharactersBecomeUnderscore()
        {
            Assert.That(Clean("a:b c#1", 5, out var changed), Is.EqualTo("a:b c_1"));
            Assert.That(changed, Is.True);
        }

        [Test]
        public void LongSegmentIsCutTo64()
        {
            var id = new string('x', 70);
            Assert.That(Clean(id, 5, out var changed), Is.EqualTo(new string('x', 64)));
            Assert.That(changed, Is.True);
        }

        [Test]
        public void ExtraSegmentsAreDropped()
        {
            Assert.That(Clean("a:b:c:d:e:f", 5, out var changed), Is.EqualTo("a:b:c:d:e"));
            Assert.That(changed, Is.True);
            Assert.That(Clean("a:b:c:d", 3, out _), Is.EqualTo("a:b:c"));
        }

        [Test]
        public void EmptySegmentsBecomeNone()
        {
            Assert.That(Clean("a::b", 5, out var changed), Is.EqualTo("a:none:b"));
            Assert.That(changed, Is.True);
            Assert.That(Clean("", 5, out _), Is.EqualTo("none"));
            Assert.That(Clean(null, 5, out _), Is.EqualTo("none"));
        }
    }
}
