using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class HangulComposerTests
    {
        [TestCase("ㅅㅣㅂㅏㄹ", "시발")]
        [TestCase("ㅅㅂ", "ㅅㅂ")]
        [TestCase("ㄱㅗㅏ", "과")]
        [TestCase("ㄷㅏㄹㄱ", "닭")]
        [TestCase("ㄷㅏㄹㄱㅏ", "달가")]
        [TestCase("ㅇㅣㅆㅇㅓ", "있어")]
        [TestCase("ㄸㅏㄸ", "따ㄸ")]
        [TestCase("시ㅂㅏㄹ", "시발")]
        [TestCase("ㅗㅏ", "ㅘ")]
        [TestCase("ㄳ", "ㄳ")]
        [TestCase("ㅂㅕㅇㅅㅣㄴ", "병신")]
        [TestCase("가나", "가나")]
        [TestCase("ㅏㄱ", "ㅏㄱ")]
        [TestCase("", "")]
        public void ComposesLikeTwoBeolsikIme(string jamo, string expected)
        {
            var destination = new char[jamo.Length];
            int length = HangulComposer.Compose(jamo, destination);
            Assert.That(new string(destination, 0, length), Is.EqualTo(expected));
        }

        [TestCase("ㄱㅏㄱㅅㅣㅂㅏㄹ", "각시발", "0-2 3-4 5-7")]
        [TestCase("ㄷㅏㄹㄱㅏ", "달가", "0-2 3-4")]
        [TestCase("ㄱㅗㅏㄴ", "관", "0-3")]
        [TestCase("ㄸㅏㄸ", "따ㄸ", "0-1 2-2")]
        [TestCase("ㅏ가ㄱ", "ㅏ가ㄱ", "0-0 1-1 2-2")]
        public void MapsEachSyllableToItsJamo(string jamo, string expected, string ranges)
        {
            var destination = new char[jamo.Length];
            var index = new int[jamo.Length];
            var first = new int[jamo.Length];
            var last = new int[jamo.Length];
            for (int i = 0; i < index.Length; i++) index[i] = i;
            int length = HangulComposer.Compose(jamo, destination, index, first, last);
            Assert.That(new string(destination, 0, length), Is.EqualTo(expected));
            var actual = new string[length];
            for (int i = 0; i < length; i++) actual[i] = first[i] + "-" + last[i];
            Assert.That(string.Join(" ", actual), Is.EqualTo(ranges));
        }
    }
}
