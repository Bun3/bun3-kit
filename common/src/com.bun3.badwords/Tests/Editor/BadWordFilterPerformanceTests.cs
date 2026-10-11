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
            filter.ContainsBadWord(shortText);
            filter.ContainsBadWord(longText);
#if UNITY_5_3_OR_NEWER
            Assert.That(() =>
            {
                filter.ContainsBadWord(shortText);
                filter.ContainsBadWord(longText);
            }, Is.Not.AllocatingGCMemory());
#else
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                filter.ContainsBadWord(shortText);
                filter.ContainsBadWord(longText);
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
        }
    }
}
