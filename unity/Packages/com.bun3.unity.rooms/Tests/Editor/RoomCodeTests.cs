using System;
using NUnit.Framework;

namespace Bun3.Unity.Rooms.Tests
{
    public class RoomCodeTests
    {
        [Test]
        public void GeneratedCodesUseTheAlphabetAndRoundTripThroughInput()
        {
            for (int i = 0; i < 1000; i++)
            {
                var code = RoomCode.Generate();
                string text = code.ToString();
                Assert.That(text, Has.Length.EqualTo(RoomCode.Length));
                foreach (char c in text)
                    Assert.That(RoomCode.Alphabet.IndexOf(c), Is.GreaterThanOrEqualTo(0), text);
                Assert.That(RoomCode.TryNormalize(text, out var parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(code));
            }
        }

        [TestCase(" abc234 ", "ABC234")]
        [TestCase("zzzz99", "ZZZZ99")]
        public void InputIsTrimmedAndUpperCased(string input, string expected)
        {
            Assert.That(RoomCode.TryNormalize(input, out var code), Is.True);
            Assert.That(code.ToString(), Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("ABC23")]
        [TestCase("ABC2345")]
        [TestCase("ABC20O")]
        [TestCase("ABC21I")]
        [TestCase("AB C23")]
        public void MalformedInputIsRejected(string input) =>
            Assert.That(RoomCode.TryNormalize(input, out _), Is.False);

        [Test]
        public void NullInputIsRejected() => Assert.That(RoomCode.TryNormalize(null, out _), Is.False);

        [TestCase(0u, "AAAAAA")]
        [TestCase(RoomCode.MaxValue, "999999")]
        [TestCase(17973u, "AAATTX")]
        public void ValuesRoundTripThroughText(uint value, string expected)
        {
            var code = RoomCode.FromValue(value);
            Assert.That(code.ToString(), Is.EqualTo(expected));
            Assert.That(RoomCode.TryNormalize(expected, out var parsed), Is.True);
            Assert.That(parsed.Value, Is.EqualTo(value));
        }

        [Test]
        public void ValuesAboveThirtyBitsAreRejected() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => RoomCode.FromValue(RoomCode.MaxValue + 1));

        [Test]
        public void TryFormatNeedsRoomForSixCharacters()
        {
            var code = RoomCode.FromValue(1);
            var small = new char[5];
            Assert.That(code.TryFormat(small, out int written), Is.False);
            Assert.That(written, Is.EqualTo(0));
            var exact = new char[6];
            Assert.That(code.TryFormat(exact, out written), Is.True);
            Assert.That(new string(exact), Is.EqualTo("AAAAAB"));
            Assert.That(written, Is.EqualTo(6));
        }
    }
}
