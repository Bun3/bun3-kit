using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class AhoCorasickTests
    {
        static readonly string[] alphabets = { "abc", "가나다" };

        [Test]
        public void MatchesEqualNaiveSearch()
        {
            var random = new Random(20261012);
            for (int round = 0; round < 500; round++)
            {
                string alphabet = alphabets[round % alphabets.Length];
                var patterns = new List<string>();
                var seen = new HashSet<string>();
                int patternCount = random.Next(1, 12);
                while (patterns.Count < patternCount)
                {
                    string pattern = RandomText(random, alphabet, random.Next(1, 5));
                    if (seen.Add(pattern)) patterns.Add(pattern);
                }
                var automaton = new AhoCorasick(patterns, new PatternFlags[patterns.Count]);
                string text = RandomText(random, alphabet, random.Next(0, 40));

                var actual = new List<(int, int, int)>();
                foreach (Match match in automaton.Matches(text)) actual.Add((match.Start, match.End, match.Pattern));
                var expected = new List<(int, int, int)>();
                for (int p = 0; p < patterns.Count; p++)
                    for (int i = 0; i + patterns[p].Length <= text.Length; i++)
                        if (string.CompareOrdinal(text, i, patterns[p], 0, patterns[p].Length) == 0)
                            expected.Add((i, i + patterns[p].Length, p));

                Assert.That(actual, Is.EquivalentTo(expected), "round " + round);
            }
        }

        [Test]
        public void AllowSpanSuppressesOnlyFullyCoveredBlocks()
        {
            var automaton = new AhoCorasick(new[] { "ab", "abc" }, new[] { PatternFlags.None, PatternFlags.Allow });
            Assert.That(automaton.ContainsBlocked("abc", false), Is.False);
            Assert.That(automaton.ContainsBlocked("ababc", false), Is.True);
        }

        [Test]
        public void BoundaryAndShortFlagsRestrictValidMatches()
        {
            var automaton = new AhoCorasick(new[] { "ass", "tq" }, new[] { PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd, PatternFlags.Short });
            Assert.That(automaton.ContainsBlocked("my ass", false), Is.True);
            Assert.That(automaton.ContainsBlocked("class", false), Is.False);
            Assert.That(automaton.ContainsBlocked("tq", true), Is.True);
            Assert.That(automaton.ContainsBlocked("tqx", true), Is.False);
            Assert.That(automaton.ContainsBlocked("tqx", false), Is.True);
        }

        [Test]
        public void MergedDuplicatesKeepTheLeastRestrictiveBoundary()
        {
            var set = new PatternSet();
            set.Add("ab", PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd);
            set.Add("ab", PatternFlags.BoundaryStart);
            var automaton = set.Build();
            Assert.That(automaton.ContainsBlocked("ab x", false), Is.True);
            Assert.That(automaton.ContainsBlocked("abx", false), Is.True);
            Assert.That(automaton.ContainsBlocked("xab", false), Is.False);
        }

        [Test]
        public void RejectsEmptyAndDuplicatePatterns()
        {
            Assert.Throws<ArgumentException>(() => new AhoCorasick(new[] { "" }, new PatternFlags[1]));
            Assert.Throws<ArgumentException>(() => new AhoCorasick(new[] { "a", "a" }, new PatternFlags[2]));
        }

        static string RandomText(Random random, string alphabet, int length)
        {
            var chars = new char[length];
            for (int i = 0; i < length; i++) chars[i] = alphabet[random.Next(alphabet.Length)];
            return new string(chars);
        }
    }
}
