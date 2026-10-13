using System;
using System.Threading.Tasks;
using NUnit.Framework;
#if UNITY_5_3_OR_NEWER
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
#endif

namespace Bun3.BadWords.Tests
{
    public class BadWordFilterPerformanceTests
    {
        static readonly BadWordFilter filter = new BadWordFilter(new[] { "시발", "!시발점", "ㅅㅂ", "fuck", "|ass|" });

        [Test]
        public void ContainsBadWordDoesNotAllocate()
        {
            string shortText = "시1발 f.u.c.k 시발점 tlqkf class";
            string longText = new string('a', 300) + " fuck";
            // Clean texts must not match so every scan runs; the asserts also warm up the code paths.
            string cleanText = "hello world class Player One 안녕하세요";
            string cleanLong = cleanText;
            while (cleanLong.Length <= 300) cleanLong += " " + cleanText;
            Assert.That(filter.ContainsBadWord(shortText), Is.True);
            Assert.That(filter.ContainsBadWord(longText), Is.True);
            Assert.That(filter.ContainsBadWord(cleanText), Is.False);
            Assert.That(filter.ContainsBadWord(cleanLong), Is.False);
#if UNITY_5_3_OR_NEWER
            Assert.That(() =>
            {
                filter.ContainsBadWord(shortText);
                filter.ContainsBadWord(longText);
                filter.ContainsBadWord(cleanText);
                filter.ContainsBadWord(cleanLong);
            }, Is.Not.AllocatingGCMemory());
#else
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                filter.ContainsBadWord(shortText);
                filter.ContainsBadWord(longText);
                filter.ContainsBadWord(cleanText);
                filter.ContainsBadWord(cleanLong);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
#endif
        }

        [Test]
        public void ConcurrentCallsMatchSequentialResults()
        {
            string[] inputs = { "시발", "시발점", "fuck you", "class", "tq", "ㅅㅂ", "hello", new string('x', 400) + "시발" };
            var expected = new bool[inputs.Length];
            for (int i = 0; i < inputs.Length; i++) expected[i] = filter.ContainsBadWord(inputs[i]);
            var actual = new bool[4000];
            Parallel.For(0, actual.Length, i => actual[i] = filter.ContainsBadWord(inputs[i % inputs.Length]));
            for (int i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(expected[i % inputs.Length]));

            string[] defaultInputs = { "시발점", "Player", "fuck", "tlqkf", "hello" };
            var defaultExpected = new bool[defaultInputs.Length];
            for (int i = 0; i < defaultInputs.Length; i++) defaultExpected[i] = BadWordFilter.Default.ContainsBadWord(defaultInputs[i]);
            Parallel.For(0, actual.Length, i => actual[i] = BadWordFilter.Default.ContainsBadWord(defaultInputs[i % defaultInputs.Length]));
            for (int i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(defaultExpected[i % defaultInputs.Length]));
        }
    }
}
