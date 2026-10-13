using NUnit.Framework;

namespace Bun3.BadWords.Editor.Tests
{
    public class BadWordTesterWindowTests
    {
        internal static class Valid
        {
            [BadWordTesterSource] internal static readonly BadWordFilter Filter = new BadWordFilter(new[] { "badword" });
        }

        internal static class Malformed
        {
            [BadWordTesterSource] internal static readonly BadWordFilter Filter = new BadWordFilter(new[] { "123" });
        }

        [Test]
        public void ListsDefaultFirstThenMarkedFieldsWithLoadErrors()
        {
            var sources = BadWordTesterWindow.FindSources();
            Assert.That(sources[0].Name, Is.EqualTo(BadWordTesterWindow.DefaultSourceName));
            Assert.That(sources[0].Filter, Is.SameAs(BadWordFilter.Default));

            var valid = sources.Find(s => s.Name == typeof(Valid).FullName + ".Filter");
            Assert.That(valid.Filter, Is.SameAs(Valid.Filter));
            Assert.That(valid.Error, Is.Null);

            var malformed = sources.Find(s => s.Name == typeof(Malformed).FullName + ".Filter");
            Assert.That(malformed.Filter, Is.Null);
            Assert.That(malformed.Error, Does.Contain("'123'"));
        }
    }
}
