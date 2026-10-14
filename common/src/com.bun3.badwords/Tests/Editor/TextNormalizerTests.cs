using System;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class TextNormalizerTests
    {
        delegate int Builder(ReadOnlySpan<char> text, Span<char> work, Span<char> destination);

        [TestCase('\u200B', '\0')]
        [TestCase('\u3164', '\0')]
        [TestCase('\u0301', '\0')]
        [TestCase('\uFF21', 'A')]
        [TestCase('\u00E9', 'e')]
        [TestCase('\u00C6', '\uFFFF')]
        [TestCase('\u0430', 'a')]
        [TestCase('\uD83D', '\uFFFF')]
        [TestCase('\uAC00', '\uAC00')]
        [TestCase('\u00DF', 's')]
        [TestCase('\u1E9E', 's')]
        public void CleansCharacters(char input, char expected) =>
            Assert.That(TextNormalizer.Clean(input), Is.EqualTo(expected));

        [TestCase("시1발", "시발")]
        [TestCase("시 발", "시발")]
        [TestCase("시.발", "시발")]
        [TestCase("ㅅㅣㅂㅏㄹ", "시발")]
        [TestCase("시\u200B발", "시발")]
        [TestCase("시\u3164발", "시발")]
        [TestCase("ㅅ ㅂ", "ㅅㅂ")]
        [TestCase("ㅅ1ㅂ", "ㅅㅂ")]
        [TestCase("abc시발", "시발")]
        [TestCase("abc", "")]
        public void HangulStreamKeepsComposedHangulOnly(string text, string expected) =>
            Assert.That(Run(TextNormalizer.Hangul, text), Is.EqualTo(expected));

        [TestCase("tlqkf", "시발")]
        [TestCase("t l q k f", "시발")]
        [TestCase("qudtls", "병신")]
        [TestCase("TLQKF", "씨빨")]
        [TestCase("tq", "ㅅㅂ")]
        [TestCase("123 시발", "")]
        public void KeyboardStreamFollowsTwoBeolsikLayout(string text, string expected) =>
            Assert.That(Run(TextNormalizer.Keyboard, text), Is.EqualTo(expected));

        [TestCase("Sh1t", "shit")]
        [TestCase("f.u.c.k", "fuck")]
        [TestCase("f u c k", "fuck")]
        [TestCase("a b xyz", "a b xyz")]
        [TestCase("i am a b c", "i am abc")]
        [TestCase("  hello   world  ", "hello world")]
        [TestCase("fu\u200Bck", "fuck")]
        [TestCase("\uFF46\uFF55\uFF43\uFF4B", "fuck")]
        [TestCase("f\u00FCck", "fuck")]
        [TestCase("fu\u0441k", "fuck")]
        [TestCase("시발", "")]
        [TestCase("Straße", "strase")]
        [TestCase("STRAẞE", "strase")]
        public void LatinStreamNormalizesLettersAndBoundaries(string text, string expected) =>
            Assert.That(Run(TextNormalizer.Latin, text), Is.EqualTo(expected));

        [TestCase("fuuuck", "fuck")]
        [TestCase("aa bb", "a b")]
        public void CollapseKeepsOneLetterPerRun(string text, string expected)
        {
            var destination = new char[text.Length];
            int length = TextNormalizer.Collapse(text, destination);
            Assert.That(new string(destination, 0, length), Is.EqualTo(expected));
        }

        static string Run(Builder builder, string text)
        {
            var work = new char[text.Length];
            var destination = new char[text.Length];
            int length = builder(text, work, destination);
            return new string(destination, 0, length);
        }
    }
}
