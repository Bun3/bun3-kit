using System;
using System.Collections.Generic;

namespace Bun3.BadWords
{
    /// <summary>
    /// Detects bad words in short user text such as nicknames and chat lines, in every bundled language at once.
    /// Immutable after construction and safe to share across threads.
    /// </summary>
    /// <remarks>
    /// Partial: <c>BadWordFilter.cs</c> (API, entry parsing, scanning) and the generated <c>BadWordFilter.Default.cs</c> (default entries).
    /// </remarks>
    public sealed partial class BadWordFilter
    {
        const int StackLimit = 256;
        const PatternFlags Boundaries = PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd;

        // Unity's Mono ArrayPool drops pooled arrays on every GC, so long inputs reuse a per-thread buffer instead.
        [ThreadStatic] static char[] scratch;

        readonly AhoCorasick korean;
        readonly AhoCorasick latin;
        readonly AhoCorasick latinCollapsed;
        readonly AhoCorasick cyrillic;
        readonly AhoCorasick cyrillicCollapsed;
        readonly AhoCorasick cjk;

        /// <summary>The filter built from <see cref="DefaultEntries"/> on first access. Touch it during boot to prewarm.</summary>
        public static BadWordFilter Default => DefaultFilter.Instance;

        /// <summary>The bundled entries for every supported language, for composing consumer-specific filters.</summary>
        public static IReadOnlyList<string> DefaultEntries => DefaultEntryList.Instance;

        /// <summary>Builds a filter from entries in the form <c>[!][|]word[|]</c>.</summary>
        /// <param name="entries">
        /// Words to block. A leading <c>!</c> marks an allow entry that suppresses block matches it fully covers.
        /// A leading or trailing <c>|</c> requires a word boundary there (Latin and Cyrillic entries only).
        /// Entries containing Hangul are Korean, entries containing Cyrillic are Cyrillic, entries containing kana or Han are kana/Han, and the rest are Latin.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// An entry is null, has no letters, has letters of an unsupported script or a character outside the Basic Multilingual Plane,
        /// mixes scripts, mixes a script with characters its stream would drop, is a kana or Han entry shorter than two characters,
        /// or puts a boundary on a Korean or kana/Han entry.
        /// </exception>
        public BadWordFilter(IEnumerable<string> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var koreanSet = new PatternSet();
            var latinSet = new PatternSet();
            var latinCollapsedSet = new PatternSet();
            var cyrillicSet = new PatternSet();
            var cyrillicCollapsedSet = new PatternSet();
            var cjkSet = new PatternSet();
            foreach (string entry in entries)
            {
                switch (Parse(entry, out string key, out PatternFlags flags))
                {
                    case Stage.Korean:
                        koreanSet.Add(key, flags);
                        break;
                    case Stage.Cyrillic:
                        AddCollapsible(key, flags, cyrillicSet, cyrillicCollapsedSet);
                        break;
                    case Stage.Cjk:
                        cjkSet.Add(key, flags);
                        break;
                    default:
                        AddCollapsible(key, flags, latinSet, latinCollapsedSet);
                        break;
                }
            }
            korean = koreanSet.Build();
            latin = latinSet.Build();
            latinCollapsed = latinCollapsedSet.Build();
            cyrillic = cyrillicSet.Build();
            cyrillicCollapsed = cyrillicCollapsedSet.Build();
            cjk = cjkSet.Build();
        }

        enum Stage
        {
            Latin,
            Korean,
            Cyrillic,
            Cjk,
        }

        enum StreamKind
        {
            None,
            Hangul,
            Latin,
            Collapsed,
            Cyrillic,
            CyrillicCollapsed,
            Cjk,
        }

        /// <summary>Returns true when <paramref name="text"/> contains a blocked word that no allow entry covers.</summary>
        /// <remarks>
        /// Allocates no managed memory once warmed up. Inputs over 256 characters use a per-thread buffer that grows to twice
        /// the longest input seen on that thread and is kept, so the first longer input on each thread allocates.
        /// </remarks>
        public bool ContainsBadWord(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return false;
            Span<char> buffer = text.Length <= StackLimit ? stackalloc char[StackLimit * 2] : Scratch(text.Length * 2);
            return Find(text, buffer.Slice(0, text.Length), buffer.Slice(text.Length, text.Length), out _) != StreamKind.None;
        }

        static char[] Scratch(int length)
        {
            char[] buffer = scratch;
            if (buffer == null || buffer.Length < length) scratch = buffer = new char[length];
            return buffer;
        }

        // Diagnostics for dictionary curation; allocates.
        internal string Explain(ReadOnlySpan<char> text)
        {
            var work = new char[text.Length];
            var stream = new char[text.Length];
            StreamKind kind = Find(text, work, stream, out Match match);
            if (kind == StreamKind.None) return null;
            char[] source = kind == StreamKind.Collapsed || kind == StreamKind.CyrillicCollapsed ? work : stream;
            return kind + ":" + new string(source, match.Start, match.End - match.Start);
        }

        StreamKind Find(ReadOnlySpan<char> text, Span<char> work, Span<char> stream, out Match match)
        {
            int length = TextNormalizer.Hangul(text, work, stream);
            if (korean.TryFindBlocked(stream.Slice(0, length), out match)) return StreamKind.Hangul;
            length = TextNormalizer.Latin(text, work, stream);
            if (length > 0)
            {
                if (latin.TryFindBlocked(stream.Slice(0, length), out match)) return StreamKind.Latin;
                length = TextNormalizer.Collapse(stream.Slice(0, length), work);
                if (latinCollapsed.TryFindBlocked(work.Slice(0, length), out match)) return StreamKind.Collapsed;
            }
            if (TextNormalizer.HasCyrillic(text))
            {
                length = TextNormalizer.Cyrillic(text, work, stream);
                if (cyrillic.TryFindBlocked(stream.Slice(0, length), out match)) return StreamKind.Cyrillic;
                length = TextNormalizer.Collapse(stream.Slice(0, length), work);
                if (cyrillicCollapsed.TryFindBlocked(work.Slice(0, length), out match)) return StreamKind.CyrillicCollapsed;
            }
            if (TextNormalizer.HasCjk(text))
            {
                length = TextNormalizer.Cjk(text, stream);
                if (cjk.TryFindBlocked(stream.Slice(0, length), out match)) return StreamKind.Cjk;
            }
            match = default;
            return StreamKind.None;
        }

        // Classified by raw characters: look-alike folding would turn a Cyrillic entry into a Latin one.
        static Stage Parse(string entry, out string key, out PatternFlags flags)
        {
            if (entry == null) throw new ArgumentException("Entries must not contain null.", "entries");
            string body = entry.Trim();
            flags = PatternFlags.None;
            if (body.StartsWith("!", StringComparison.Ordinal))
            {
                flags |= PatternFlags.Allow;
                body = body.Substring(1);
            }
            if (body.StartsWith("|", StringComparison.Ordinal))
            {
                flags |= PatternFlags.BoundaryStart;
                body = body.Substring(1);
            }
            if (body.EndsWith("|", StringComparison.Ordinal))
            {
                flags |= PatternFlags.BoundaryEnd;
                body = body.Substring(0, body.Length - 1);
            }

            bool hangul = false, cyrillic = false, cjk = false, latinLetter = false, other = false;
            foreach (char c in body)
            {
                if (char.IsSurrogate(c)) throw Malformed(entry, "has a character outside the Basic Multilingual Plane");
                if (TextNormalizer.IsCyrillic(c))
                {
                    cyrillic = true;
                    continue;
                }
                if (TextNormalizer.IsCjk(c))
                {
                    cjk = true;
                    continue;
                }
                char clean = TextNormalizer.Clean(c);
                if (clean == TextNormalizer.Removed || char.IsWhiteSpace(clean)) continue;
                if (TextNormalizer.IsHangul(clean)) hangul = true;
                // Greek look-alikes would otherwise register as Latin letters.
                else if (TextNormalizer.IsLatinLetter(clean) && TextNormalizer.Lookalike(c) == TextNormalizer.Removed) latinLetter = true;
                else if (char.IsLetter(c)) throw Malformed(entry, "has letters of an unsupported script");
                else other = true;
            }
            if ((hangul ? 1 : 0) + (cyrillic ? 1 : 0) + (cjk ? 1 : 0) > 1) throw Malformed(entry, "mixes scripts");

            var work = new char[body.Length];
            var normalized = new char[body.Length];
            if (hangul)
            {
                // Stream A drops non-Hangul characters, so a Korean entry with digits or symbols would silently shrink.
                if (latinLetter || other) throw Malformed(entry, "mixes Hangul with other characters");
                if ((flags & Boundaries) != 0) throw Malformed(entry, "uses a word boundary on a Korean entry");
                key = new string(normalized, 0, TextNormalizer.Hangul(body, work, normalized));
                return Stage.Korean;
            }
            if (cyrillic)
            {
                // Stream D turns every other character into a boundary, which would silently split the entry.
                foreach (char c in body)
                    if (TextNormalizer.CyrillicLetter(c) == ' ' && !char.IsWhiteSpace(c)) throw Malformed(entry, "mixes Cyrillic with other characters");
                key = new string(normalized, 0, TextNormalizer.Cyrillic(body, work, normalized));
                if (key.Length == 0) throw Malformed(entry, "has no letters");
                return Stage.Cyrillic;
            }
            if (cjk)
            {
                // Stream E drops every other character, which would silently shrink the entry.
                if (latinLetter || other) throw Malformed(entry, "mixes kana or Han with other characters");
                if ((flags & Boundaries) != 0) throw Malformed(entry, "uses a word boundary on a kana or Han entry");
                int length = TextNormalizer.Cjk(body, normalized);
                // A single kana or Han character sits inside too many ordinary words.
                if (length < 2) throw Malformed(entry, "has fewer than two kana or Han characters");
                key = new string(normalized, 0, length);
                return Stage.Cjk;
            }
            if (!latinLetter) throw Malformed(entry, "has no letters");
            key = new string(normalized, 0, TextNormalizer.Latin(body, work, normalized));
            return Stage.Latin;
        }

        static void AddCollapsible(string key, PatternFlags flags, PatternSet plain, PatternSet collapsed)
        {
            plain.Add(key, flags);
            if ((flags & PatternFlags.Allow) != 0)
            {
                var work = new char[key.Length];
                collapsed.Add(new string(work, 0, TextNormalizer.Collapse(key, work)), flags);
            }
            else if (!TextNormalizer.HasDoubledLetter(key))
            {
                collapsed.Add(key, flags);
            }
        }

        static ArgumentException Malformed(string entry, string reason) =>
            new ArgumentException("Entry '" + entry + "' " + reason + ".", "entries");

        static class DefaultEntryList
        {
            internal static readonly IReadOnlyList<string> Instance = Array.AsReadOnly(CreateDefaultEntries());
        }

        static class DefaultFilter
        {
            internal static readonly BadWordFilter Instance = new BadWordFilter(DefaultEntryList.Instance);
        }
    }
}
