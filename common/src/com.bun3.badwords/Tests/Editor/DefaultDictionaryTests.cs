using System;
using System.Diagnostics;
using System.Text;
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
        [TestCase("개새끼")]
        [TestCase("개 새끼")]
        [TestCase("미친새끼")]
        [TestCase("장애새끼")]
        [TestCase("개세끼")]
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
        [TestCase("공룡 다리 발자국")]
        [TestCase("게놈")]
        [TestCase("새까만")]
        [TestCase("10광년")]
        [TestCase("TV")]
        [TestCase("새끼공룡")]
        [TestCase("Siva")]
        [TestCase("H")]
        [TestCase("Cocoon")]
        [TestCase("세끼")]
        public void PassesOrdinaryText(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.False, BadWordFilter.Default.Explain(text));

        // Owner decision (2026-10-13): arguable words pass; compounds such as 개새끼 stay blocked.
        [TestCase("개드립")]
        [TestCase("개소리")]
        [TestCase("운지")]
        [TestCase("새끼")]
        [TestCase("빨갱이")]
        [TestCase("급식충")]
        [TestCase("김치녀")]
        [TestCase("꺼져")]
        [TestCase("또라이")]
        [TestCase("듣보잡")]
        [TestCase("domination")]
        [TestCase("collared")]
        [TestCase("collaring")]
        [TestCase("fingering")]
        [TestCase("fingered")]
        [TestCase("intercourse")]
        [TestCase("nude")]
        [TestCase("topless")]
        [TestCase("bareback")]
        [TestCase("skeet")]
        [TestCase("playboy")]
        [TestCase("bastard")]
        [TestCase("panties")]
        [TestCase("grope")]
        [TestCase("enby")]
        [TestCase("cishet")]
        public void PassesOwnerAllowedWords(string text) =>
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

        [Test]
        public void ReportsScanCost()
        {
            const int iterations = 100000;
            string[] inputs = { "Player One", "탈출친구", "f.u.c.k you", "привет как дела", "こんにちは今日はいい天気", "你好世界朋友们" };
            var report = new StringBuilder();
            foreach (string input in inputs)
            {
                for (int i = 0; i < 1000; i++) BadWordFilter.Default.ContainsBadWord(input);
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++) BadWordFilter.Default.ContainsBadWord(input);
                watch.Stop();
                report.Append(input).Append('=').Append((watch.Elapsed.TotalMilliseconds * 1000000 / iterations).ToString("F0")).Append("ns ");
            }
            Assert.Pass(report.ToString());
        }
    }
}
