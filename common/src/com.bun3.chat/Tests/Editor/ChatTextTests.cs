#nullable enable
using System;
using NUnit.Framework;

namespace Bun3.Chat.Tests
{
    public sealed class ChatTextTests
    {
        [TestCase("a\nb", "a b")]
        [TestCase("a\tb\r\n", "a b")]
        [TestCase("  hi  ", "hi")]
        [TestCase("a\u2028b", "a b")]
        [TestCase("a\u2029b", "a b")]
        public void ControlCharactersBecomeSpacesAndEdgesAreTrimmed(string input, string expected)
        {
            Assert.That(ChatText.TryNormalize(input, 100, out var text), Is.True);
            Assert.That(text, Is.EqualTo(expected));
        }

        [Test]
        public void FormatCharactersAreRemoved()
        {
            Assert.That(ChatText.TryNormalize("a\u200Bb\u202Ec", 100, out var text), Is.True);
            Assert.That(text, Is.EqualTo("abc"));
        }

        [Test]
        public void NonBmpFormatCharactersAreRemoved()
        {
            Assert.That(ChatText.TryNormalize("a\U000E0041b\U0001D173c", 100, out var text), Is.True);
            Assert.That(text, Is.EqualTo("abc"));
        }

        [Test]
        public void TextIsComposedToNfc()
        {
            Assert.That(ChatText.TryNormalize("\u1100\u1161", 100, out var hangul), Is.True);
            Assert.That(hangul, Is.EqualTo("\uAC00"));
            Assert.That(ChatText.TryNormalize("e\u0301", 100, out var accented), Is.True);
            Assert.That(accented, Is.EqualTo("\u00E9"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\n\t")]
        [TestCase("\u200B")]
        [TestCase("\u0301\u0301")]
        [TestCase("\uFE0F")]
        [TestCase("\u3164")]
        [TestCase("\u115F\u1160")]
        [TestCase("\uFFA0")]
        public void InvisibleTextIsRejected(string? input)
        {
            Assert.That(ChatText.TryNormalize(input, 100, out var text), Is.False);
            Assert.That(text, Is.Empty);
        }

        [Test]
        public void NonBmpInvisibleOnlyTextIsRejected()
        {
            var inputs = new[]
            {
                string.Concat(System.Linq.Enumerable.Repeat("\U000E0041", 50)),
                string.Concat(System.Linq.Enumerable.Repeat("\U000E0100", 50)),
            };
            foreach (var input in inputs)
                Assert.That(ChatText.TryNormalize(input, 100, out _), Is.False);
        }

        [Test]
        public void LengthCountsTextElements()
        {
            Assert.That(ChatText.TryNormalize("\uAC00\uB098\uB2E4", 3, out _), Is.True);
            Assert.That(ChatText.TryNormalize("\uAC00\uB098\uB2E4\uB77C", 3, out _), Is.False);
            Assert.That(ChatText.TryNormalize("\U0001F600\U0001F600", 2, out _), Is.True);
            Assert.That(ChatText.TryNormalize("\U0001F600\U0001F600\U0001F600", 2, out _), Is.False);
        }

        [Test]
        public void LoneSurrogatesAreRejected()
        {
            // Attribute arguments are stored as UTF-8 metadata, which replaces lone surrogates, so the inputs live here.
            var inputs = new[] { "a\uD800b", "\uDC00", "a\uD800" };
            foreach (var input in inputs)
                Assert.That(ChatText.TryNormalize(input, 100, out _), Is.False);
        }

        [Test]
        public void CombiningMarkFloodIsRejectedByUtf8Budget()
        {
            // One text element each: 2 + 299 * 2 = 600 bytes > 400, and 2 + 149 * 2 = 300 bytes <= 400.
            Assert.That(ChatText.TryNormalize("a" + new string('\u0301', 300), 100, out _), Is.False);
            Assert.That(ChatText.TryNormalize("a" + new string('\u0301', 150), 100, out _), Is.True);
        }

        [Test]
        public void NonPositiveMaxLengthThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ChatText.TryNormalize("a", 0, out _));
        }
    }
}
