using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    // Checks the hand-written kana tables against .NET's Unicode data.
    public class UnicodeTableTests
    {
        [Test]
        public void HalfwidthKatakanaFoldsLikeCompatibilityDecomposition()
        {
            for (char c = '\uFF66'; c <= '\uFF9D'; c++)
                Assert.That(TextNormalizer.Kana(c), Is.EqualTo(c.ToString().Normalize(NormalizationForm.FormKC)[0]), ((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }

        [Test]
        public void VoicedMarksComposeLikeCanonicalComposition()
        {
            foreach (char c in "カキクケコサシスセソタチツテトハヒフヘホウ")
                Assert.That(Cjk(c + "\u3099"), Is.EqualTo((c + "\u3099").Normalize(NormalizationForm.FormC)), c.ToString());
            foreach (char c in "ハヒフヘホ")
                Assert.That(Cjk(c + "\u309A"), Is.EqualTo((c + "\u309A").Normalize(NormalizationForm.FormC)), c.ToString());
        }

        static string Cjk(string text)
        {
            var destination = new char[text.Length];
            return new string(destination, 0, TextNormalizer.Cjk(text, destination));
        }
    }
}
