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
    }
}
