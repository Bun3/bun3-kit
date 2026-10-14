using System;
using System.Collections.Generic;

namespace Bun3.BadWords
{
    [Flags]
    internal enum PatternFlags : byte
    {
        None = 0,
        Allow = 1,
        BoundaryStart = 2,
        BoundaryEnd = 4,
    }

    internal readonly struct Match
    {
        public readonly int Start;
        public readonly int End;
        public readonly int Pattern;

        public Match(int start, int end, int pattern)
        {
            Start = start;
            End = end;
            Pattern = pattern;
        }
    }

    /// <summary>
    /// Immutable Aho-Corasick automaton over distinct, non-empty patterns.
    /// Edges are sorted per state in flattened arrays; lookups use binary search. Safe to share across threads.
    /// </summary>
    internal sealed class AhoCorasick
    {
        internal const int SpanCapacity = 32;

        readonly int[] firstEdge;
        readonly char[] edgeChar;
        readonly int[] edgeTarget;
        readonly int[] fail;
        readonly int[] statePattern;
        readonly int[] firstOutput;
        readonly int[] nextOutput;
        readonly int[] patternLength;
        readonly PatternFlags[] patternFlags;

        internal AhoCorasick(IReadOnlyList<string> patterns, IReadOnlyList<PatternFlags> flags)
        {
            var children = new List<Dictionary<char, int>> { new Dictionary<char, int>() };
            var terminal = new List<int> { -1 };
            patternLength = new int[patterns.Count];
            patternFlags = new PatternFlags[patterns.Count];
            for (int p = 0; p < patterns.Count; p++)
            {
                string pattern = patterns[p];
                if (string.IsNullOrEmpty(pattern)) throw new ArgumentException("Patterns must be non-empty.", nameof(patterns));
                int state = 0;
                foreach (char c in pattern)
                {
                    if (!children[state].TryGetValue(c, out int next))
                    {
                        next = children.Count;
                        children.Add(new Dictionary<char, int>());
                        terminal.Add(-1);
                        children[state].Add(c, next);
                    }
                    state = next;
                }
                if (terminal[state] >= 0) throw new ArgumentException("Patterns must be distinct.", nameof(patterns));
                terminal[state] = p;
                patternLength[p] = pattern.Length;
                patternFlags[p] = flags[p];
            }

            int count = children.Count;
            statePattern = terminal.ToArray();
            firstEdge = new int[count + 1];
            for (int s = 0; s < count; s++) firstEdge[s + 1] = firstEdge[s] + children[s].Count;
            edgeChar = new char[firstEdge[count]];
            edgeTarget = new int[firstEdge[count]];
            for (int s = 0; s < count; s++)
            {
                int e = firstEdge[s];
                foreach (var pair in children[s])
                {
                    edgeChar[e] = pair.Key;
                    edgeTarget[e] = pair.Value;
                    e++;
                }
                Array.Sort(edgeChar, edgeTarget, firstEdge[s], children[s].Count);
            }

            fail = new int[count];
            firstOutput = new int[count];
            nextOutput = new int[count];
            firstOutput[0] = -1;
            nextOutput[0] = -1;
            var queue = new Queue<int>();
            for (int e = firstEdge[0]; e < firstEdge[1]; e++) queue.Enqueue(edgeTarget[e]);
            while (queue.Count > 0)
            {
                int s = queue.Dequeue();
                nextOutput[s] = firstOutput[fail[s]];
                firstOutput[s] = statePattern[s] >= 0 ? s : nextOutput[s];
                for (int e = firstEdge[s]; e < firstEdge[s + 1]; e++)
                {
                    int target = edgeTarget[e];
                    fail[target] = Next(fail[s], edgeChar[e]);
                    queue.Enqueue(target);
                }
            }
        }

        internal MatchEnumerator Matches(ReadOnlySpan<char> text) => new MatchEnumerator(this, text);

        internal bool ContainsBlocked(ReadOnlySpan<char> text) => TryFindBlocked(text, out _);

        // A block span is suppressed when an allow span of the same scan covers it; more than SpanCapacity blocks is a hit.
        internal bool TryFindBlocked(ReadOnlySpan<char> text, out Match blocked)
        {
            Span<Match> blocks = stackalloc Match[SpanCapacity];
            Span<Match> allows = stackalloc Match[SpanCapacity];
            int blockCount = 0, allowCount = 0;
            foreach (Match match in Matches(text))
            {
                PatternFlags flags = patternFlags[match.Pattern];
                if (!IsValid(flags, text, match)) continue;
                if ((flags & PatternFlags.Allow) != 0)
                {
                    if (allowCount < SpanCapacity) allows[allowCount++] = match;
                }
                else if (blockCount == SpanCapacity)
                {
                    blocked = match;
                    return true;
                }
                else
                {
                    blocks[blockCount++] = match;
                }
            }
            for (int b = 0; b < blockCount; b++)
            {
                bool covered = false;
                for (int a = 0; a < allowCount && !covered; a++)
                    covered = allows[a].Start <= blocks[b].Start && blocks[b].End <= allows[a].End;
                if (!covered)
                {
                    blocked = blocks[b];
                    return true;
                }
            }
            blocked = default;
            return false;
        }

        static bool IsValid(PatternFlags flags, ReadOnlySpan<char> text, Match match)
        {
            if ((flags & PatternFlags.BoundaryStart) != 0 && match.Start > 0 && text[match.Start - 1] != ' ') return false;
            if ((flags & PatternFlags.BoundaryEnd) != 0 && match.End < text.Length && text[match.End] != ' ') return false;
            return true;
        }

        int Next(int state, char c)
        {
            while (true)
            {
                int target = Edge(state, c);
                if (target >= 0) return target;
                if (state == 0) return 0;
                state = fail[state];
            }
        }

        int Edge(int state, char c)
        {
            int low = firstEdge[state], high = firstEdge[state + 1] - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                char key = edgeChar[middle];
                if (key == c) return edgeTarget[middle];
                if (key < c) low = middle + 1;
                else high = middle - 1;
            }
            return -1;
        }

        internal ref struct MatchEnumerator
        {
            readonly AhoCorasick automaton;
            readonly ReadOnlySpan<char> text;
            int position;
            int state;
            int output;

            internal MatchEnumerator(AhoCorasick automaton, ReadOnlySpan<char> text)
            {
                this.automaton = automaton;
                this.text = text;
                position = 0;
                state = 0;
                output = -1;
                Current = default;
            }

            public Match Current { get; private set; }

            public MatchEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                while (output < 0)
                {
                    if (position == text.Length) return false;
                    state = automaton.Next(state, text[position++]);
                    output = automaton.firstOutput[state];
                }
                int pattern = automaton.statePattern[output];
                Current = new Match(position - automaton.patternLength[pattern], position, pattern);
                output = automaton.nextOutput[output];
                return true;
            }
        }
    }
}
