using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class DefaultDictionaryTests
    {
        [TestCase("시발")]
        [TestCase("ㅅㅂ")]
        [TestCase("tlqkf")]
        [TestCase("병신")]
        [TestCase("fuck")]
        [TestCase("b1tch")]
        [TestCase("f u c k")]
        [TestCase("fuuuck")]
        public void BlocksStrongWords(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.True, text);

        [TestCase("시발점")]
        [TestCase("다시 발견")]
        [TestCase("수박")]
        [TestCase("class")]
        [TestCase("assassin")]
        [TestCase("Scunthorpe")]
        [TestCase("but")]
        [TestCase("document")]
        [TestCase("accumulate")]
        [TestCase("탈출친구")]
        [TestCase("방장")]
        [TestCase("먼저 온 친구")]
        [TestCase("공룡조련사")]
        [TestCase("Player")]
        [TestCase("dinosaur")]
        [TestCase("Jurassic")]
        [TestCase("hello world")]
        [TestCase("가")]
        public void PassesOrdinaryText(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.False, BadWordFilter.Default.Explain(text));

        [Test]
        public void ReportsDefaultBuildCost()
        {
            long before = GC.GetTotalMemory(true);
            var watch = Stopwatch.StartNew();
            var filter = new BadWordFilter(BadWordFilter.DefaultEntries);
            watch.Stop();
            long retained = GC.GetTotalMemory(true) - before;
            GC.KeepAlive(filter);
            Assert.Pass("entries=" + BadWordFilter.DefaultEntries.Count + " buildMs=" + watch.Elapsed.TotalMilliseconds.ToString("F1") + " retainedBytes=" + retained);
        }
    }
}
