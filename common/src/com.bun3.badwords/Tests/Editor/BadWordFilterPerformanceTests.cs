using System;
using System.Collections.Generic;
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
        static readonly BadWordFilter filter = new BadWordFilter(new[] { "시발", "!시발점", "ㅅㅂ", "fuck", "|ass|", "хуй", "ちんこ" });

        [Test]
        public void ContainsBadWordDoesNotAllocate()
        {
            string[] blocked = WithLongForms("시1발 f.u.c.k 시발점 tlqkf class", "x\u0443\u0439 привет", "\uFF81\uFF9D\uFF7A 你好");
            // Clean texts must not match so every scan, the gated ones included, runs; the asserts also warm up the code paths.
            string[] clean = WithLongForms("hello world class Player One 안녕하세요", "привет как дела хлеб", "こんにちは今日はいい天気 奶茶");
            foreach (string text in blocked) Assert.That(filter.ContainsBadWord(text), Is.True, text);
            foreach (string text in clean) Assert.That(filter.ContainsBadWord(text), Is.False, text);
            // Unity's Mono ArrayPool drops pooled arrays on every GC; long inputs must not depend on a pool surviving one.
            GC.Collect();
#if UNITY_5_3_OR_NEWER
            Assert.That(() =>
            {
                foreach (string text in blocked) filter.ContainsBadWord(text);
                foreach (string text in clean) filter.ContainsBadWord(text);
            }, Is.Not.AllocatingGCMemory());
#else
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                foreach (string text in blocked) filter.ContainsBadWord(text);
                foreach (string text in clean) filter.ContainsBadWord(text);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
#endif
        }

        [Test]
        public void ConcurrentCallsMatchSequentialResults()
        {
            string[] inputs = { "시발", "시발점", "fuck you", "class", "tq", "ㅅㅂ", "hello", new string('x', 400) + "시발", "x\u0443\u0439", "привет", "\uFF81\uFF9D\uFF7A", "こんにちは" };
            var expected = new bool[inputs.Length];
            for (int i = 0; i < inputs.Length; i++) expected[i] = filter.ContainsBadWord(inputs[i]);
            var actual = new bool[4000];
            Parallel.For(0, actual.Length, i => actual[i] = filter.ContainsBadWord(inputs[i % inputs.Length]));
            for (int i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(expected[i % inputs.Length]));

            string[] defaultInputs = { "시발점", "Player", "fuck", "tlqkf", "hello", "хуй", "ちんこ" };
            var defaultExpected = new bool[defaultInputs.Length];
            for (int i = 0; i < defaultInputs.Length; i++) defaultExpected[i] = BadWordFilter.Default.ContainsBadWord(defaultInputs[i]);
            Parallel.For(0, actual.Length, i => actual[i] = BadWordFilter.Default.ContainsBadWord(defaultInputs[i % defaultInputs.Length]));
            for (int i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(defaultExpected[i % defaultInputs.Length]));
        }

        // Each text and a repetition over 256 characters, so both the stack and the per-thread buffer paths run.
        static string[] WithLongForms(params string[] texts)
        {
            var result = new List<string>();
            foreach (string text in texts)
            {
                string longText = text;
                while (longText.Length <= 300) longText += " " + text;
                result.Add(text);
                result.Add(longText);
            }
            return result.ToArray();
        }
    }
}
