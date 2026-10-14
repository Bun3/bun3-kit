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
            "хуй", "|бля", "пизд", "ебат", "!застрахуй",
            "ちんこ", "フェラ", "!フェラーリ", "傻逼", "鸡巴",
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
        [TestCase("хуй")]
        [TestCase("ХУЙ")]
        [TestCase("x\u0443\u0439")]
        [TestCase("X\u0423\u0419")]
        [TestCase("х у й")]
        [TestCase("хууууй")]
        [TestCase("\u0445\u0443\u0438\u0306")]
        [TestCase("иди нахуй")]
        [TestCase("бля")]
        [TestCase("ну бля")]
        [TestCase("пиздец")]
        [TestCase("ちんこ")]
        [TestCase("チンコ")]
        [TestCase("\uFF81\uFF9D\uFF7A")]
        [TestCase("ち ん こ")]
        [TestCase("ちーんこ")]
        [TestCase("お前のちんこ")]
        [TestCase("Player ちんこ")]
        [TestCase("フェラ")]
        [TestCase("傻逼")]
        [TestCase("你是傻逼吗")]
        [TestCase("傻\uD840\uDC00逼")]
        [TestCase("雞巴")]
        [TestCase("鸡巴")]
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
        [TestCase("рубля")]
        [TestCase("застрахуйте")]
        [TestCase("где батарея")]
        [TestCase("тебе")]
        [TestCase("хлеб")]
        [TestCase("\u0483")]
        [TestCase("フェラーリ")]
        [TestCase("こんにちは")]
        [TestCase("中文")]
        [TestCase("\u3099\u309A")]
        [TestCase("\u30FC\u30FB")]
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
            Assert.That(filter.Explain("x\u0443\u0439"), Is.EqualTo("Cyrillic:хуи"));
            Assert.That(filter.Explain("хууууй"), Is.EqualTo("CyrillicCollapsed:хуи"));
            Assert.That(filter.Explain("\uFF81\uFF9D\uFF7A"), Is.EqualTo("Cjk:チンコ"));
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
        [TestCase("\u6027")]
        [TestCase("\u3061")]
        [TestCase("\u30FC\u30FB")]
        [TestCase("\u5356B")]
        [TestCase("\u5356\u0445")]
        [TestCase("\u3061\u3093\u3053\uAC00")]
        [TestCase("|\u3061\u3093\u3053")]
        [TestCase("\u3061\u3093\u3053\uD840\uDC00")]
        [TestCase("\uC2DC\u0445")]
        [TestCase("\u0445\u0443\u04391")]
        [TestCase("\u0445-\u0443\u0439")]
        [TestCase("\u0483")]
        [TestCase("fuck\uD83D\uDE00")]
        [TestCase("fu\u0441k")]
        [TestCase("\u03B1\u03B2\u03B3")]
        [TestCase("\u00FEorn")]
        public void MalformedEntryThrows(string entry) =>
            Assert.Throws<ArgumentException>(() => new BadWordFilter(new[] { entry }));

        [Test]
        public void TwoHanEntryMatchesInsideText() =>
            Assert.That(new BadWordFilter(new[] { "\u4E2D\u56FD" }).ContainsBadWord("\u4E2D\u56FD\u4EBA"), Is.True);

        [Test]
        public void CyrillicStreamRunsOnlyOnCyrillicInput()
        {
            // Every letter of this Cyrillic entry has a Latin look-alike; the middle one is written in Latin.
            var lookalikes = new BadWordFilter(new[] { "\u0441o\u0440" });
            Assert.That(lookalikes.ContainsBadWord("\u0441\u043E\u0440"), Is.True);
            Assert.That(lookalikes.ContainsBadWord("co\u0440"), Is.True);
            Assert.That(lookalikes.ContainsBadWord("cop"), Is.False);
            Assert.That(lookalikes.ContainsBadWord("COP"), Is.False);
        }

        [Test]
        public void SharpSMatchesBothSpellings()
        {
            var german = new BadWordFilter(new[] { "stra\u00DFe" });
            Assert.That(german.ContainsBadWord("Stra\u00DFe"), Is.True);
            Assert.That(german.ContainsBadWord("strasse"), Is.True);
            Assert.That(german.ContainsBadWord("STRA\u1E9EE"), Is.True);
        }

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
