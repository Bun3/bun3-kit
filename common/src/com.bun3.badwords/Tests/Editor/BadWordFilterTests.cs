using System;
using System.Text;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class BadWordFilterTests
    {
        static readonly BadWordFilter filter = new BadWordFilter(new[]
        {
            "시발", "!시발점", "!다시발", "ㅅㅂ", "병신", "좆",
            "fuck", "|ass|", "butt", "cum", "!accumulate", "cunt", "!scunthorpe",
        });

        [TestCase("시발")]
        [TestCase("시1발")]
        [TestCase("시 발")]
        [TestCase("시.발")]
        [TestCase("ㅅㅣㅂㅏㄹ")]
        [TestCase("시ㅂㅏㄹ")]
        [TestCase("시\u200B발")]
        [TestCase("시\u3164발")]
        [TestCase("시\uD83D\uDE00발")]
        [TestCase("ㅅㅂ")]
        [TestCase("ㅅ ㅂ")]
        [TestCase("ㅅ1ㅂ")]
        [TestCase("ㅅㅂ야")]
        [TestCase("닉네임시발")]
        [TestCase("시발시발점")]
        [TestCase("tlqkf")]
        [TestCase("t l q k f")]
        [TestCase("qudtls")]
        [TestCase("tq")]
        [TestCase("fuck")]
        [TestCase("FUCK")]
        [TestCase("f u c k")]
        [TestCase("f.u.c.k")]
        [TestCase("fuuuck")]
        [TestCase("\uFF46\uFF55\uFF43\uFF4B")]
        [TestCase("f\u00FCck")]
        [TestCase("fu\u0441k")]
        [TestCase("my ass")]
        [TestCase("you fuck off")]
        public void BlocksEvasionForms(string text) =>
            Assert.That(filter.ContainsBadWord(text), Is.True, text);

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\u200B\u200B")]
        [TestCase("\u3164")]
        [TestCase("시발점")]
        [TestCase("다시 발견")]
        [TestCase("수박")]
        [TestCase("class")]
        [TestCase("assassin")]
        [TestCase("but")]
        [TestCase("accumulate")]
        [TestCase("Scunthorpe")]
        [TestCase("tqx")]
        [TestCase("whw")]
        [TestCase("Player One")]
        [TestCase("가")]
        public void PassesOrdinaryText(string text) =>
            Assert.That(filter.ContainsBadWord(text), Is.False, filter.Explain(text));

        [Test]
        public void KeyboardStreamIgnoresSingleLettersAndMixedInput()
        {
            var shortFilter = new BadWordFilter(new[] { "ㅗ", "ㅂㅅ", "시발" });
            Assert.That(shortFilter.ContainsBadWord("h"), Is.False);
            Assert.That(shortFilter.ContainsBadWord("H"), Is.False);
            Assert.That(shortFilter.ContainsBadWord("공룡h"), Is.False);
            Assert.That(shortFilter.ContainsBadWord("가qt"), Is.False);
            Assert.That(shortFilter.ContainsBadWord("qt"), Is.True);
            Assert.That(shortFilter.ContainsBadWord("가tlqkf"), Is.True);
        }

        [Test]
        public void LongInputUsesThePooledPath()
        {
            Assert.That(filter.ContainsBadWord(new string('\uAC00', 300) + "시발"), Is.True);
            Assert.That(filter.ContainsBadWord(new string('\uAC00', 300)), Is.False);
        }

        [Test]
        public void MoreBlockSpansThanCapacityCountAsBadWord()
        {
            var text = new StringBuilder();
            for (int i = 0; i < AhoCorasick.SpanCapacity + 8; i++) text.Append("시발점");
            Assert.That(filter.ContainsBadWord(text.ToString()), Is.True);
        }

        [Test]
        public void ExplainNamesTheStreamAndNormalizedSpan()
        {
            Assert.That(filter.Explain("시1발"), Is.EqualTo("Hangul:시발"));
            Assert.That(filter.Explain("tlqkf"), Is.EqualTo("Keyboard:시발"));
            Assert.That(filter.Explain("f.u.c.k"), Is.EqualTo("Latin:fuck"));
            Assert.That(filter.Explain("fuuuck"), Is.EqualTo("Collapsed:fuck"));
            Assert.That(filter.Explain("시발점"), Is.Null);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("!")]
        [TestCase("|")]
        [TestCase("!|")]
        [TestCase("123")]
        [TestCase("시발bal")]
        [TestCase("18놈")]
        [TestCase("|시발")]
        [TestCase("\u4E2D\u56FD")]
        [TestCase("\u0445\u0443\u0439")]
        [TestCase("fu\u0441k")]
        [TestCase("\u03B1\u03B2\u03B3")]
        [TestCase("stra\u00DFe")]
        public void MalformedEntryThrows(string entry) =>
            Assert.Throws<ArgumentException>(() => new BadWordFilter(new[] { entry }));

        [Test]
        public void NullEntryThrowsArgumentException() =>
            Assert.Throws<ArgumentException>(() => new BadWordFilter(new string[] { null }));

        [Test]
        public void NullEntriesThrow() =>
            Assert.Throws<ArgumentNullException>(() => new BadWordFilter(null));

        [Test]
        public void DuplicateEntriesKeepTheLeastRestrictiveBoundary()
        {
            var merged = new BadWordFilter(new[] { "|fuck|", "fuck" });
            Assert.That(merged.ContainsBadWord("motherfucker"), Is.True);
        }
    }
}
