using System;
using System.Buffers;
using System.Collections.Generic;

namespace Bun3.BadWords
{
    /// <summary>
    /// Detects Korean and English bad words in short user text such as nicknames and chat lines.
    /// Immutable after construction and safe to share across threads.
    /// </summary>
    /// <remarks>
    /// Partial: <c>BadWordFilter.cs</c> (API, entry parsing, scanning) and the generated <c>BadWordFilter.Default.cs</c> (default entries).
    /// </remarks>
    public sealed partial class BadWordFilter
    {
        const int StackLimit = 256;

        readonly AhoCorasick korean;
        readonly AhoCorasick english;
        readonly AhoCorasick englishCollapsed;

        /// <summary>The filter built from <see cref="DefaultEntries"/> on first access. Touch it during boot to prewarm.</summary>
        public static BadWordFilter Default => DefaultFilter.Instance;

        /// <summary>The bundled Korean and English entries, for composing consumer-specific filters.</summary>
        public static IReadOnlyList<string> DefaultEntries => DefaultEntryList.Instance;

        /// <summary>Builds a filter from entries in the form <c>[!][|]word[|]</c>.</summary>
        /// <param name="entries">
        /// Words to block. A leading <c>!</c> marks an allow entry that suppresses block matches it fully covers.
        /// A leading or trailing <c>|</c> requires a word boundary there (English entries only).
        /// Entries containing Hangul are Korean; others are English.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
        /// <exception cref="ArgumentException">An entry is null, has no letters, mixes Hangul with other characters, or puts a boundary on a Korean word.</exception>
        public BadWordFilter(IEnumerable<string> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var koreanSet = new PatternSet();
            var englishSet = new PatternSet();
            var collapsedSet = new PatternSet();
            foreach (string entry in entries) Add(entry, koreanSet, englishSet, collapsedSet);
            korean = koreanSet.Build();
            english = englishSet.Build();
            englishCollapsed = collapsedSet.Build();
        }

        enum StreamKind
        {
            None,
            Hangul,
            Keyboard,
            Latin,
            Collapsed,
        }

        /// <summary>Returns true when <paramref name="text"/> contains a blocked word that no allow entry covers.</summary>
        /// <remarks>Allocates no managed memory; inputs over 256 characters rent scratch space from <see cref="ArrayPool{T}.Shared"/>.</remarks>
        public bool ContainsBadWord(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return false;
            char[] rented = null;
            Span<char> buffer = text.Length <= StackLimit
                ? stackalloc char[StackLimit * 2]
                : (rented = ArrayPool<char>.Shared.Rent(text.Length * 2));
            try
            {
                return Find(text, buffer.Slice(0, text.Length), buffer.Slice(text.Length, text.Length), out _) != StreamKind.None;
            }
            finally
            {
                if (rented != null) ArrayPool<char>.Shared.Return(rented);
            }
        }

        // Diagnostics for dictionary curation; allocates.
        internal string Explain(ReadOnlySpan<char> text)
        {
            var work = new char[text.Length];
            var stream = new char[text.Length];
            StreamKind kind = Find(text, work, stream, out Match match);
            if (kind == StreamKind.None) return null;
            char[] source = kind == StreamKind.Collapsed ? work : stream;
            return kind + ":" + new string(source, match.Start, match.End - match.Start);
        }

        StreamKind Find(ReadOnlySpan<char> text, Span<char> work, Span<char> stream, out Match match)
        {
            int length = TextNormalizer.Hangul(text, work, stream);
            if (korean.TryFindBlocked(stream.Slice(0, length), false, out match)) return StreamKind.Hangul;
            length = TextNormalizer.Keyboard(text, work, stream);
            if (length > 0 && korean.TryFindBlocked(stream.Slice(0, length), true, out match)) return StreamKind.Keyboard;
            length = TextNormalizer.Latin(text, work, stream);
            if (length == 0) return StreamKind.None;
            if (english.TryFindBlocked(stream.Slice(0, length), false, out match)) return StreamKind.Latin;
            int collapsed = TextNormalizer.Collapse(stream.Slice(0, length), work);
            return englishCollapsed.TryFindBlocked(work.Slice(0, collapsed), false, out match) ? StreamKind.Collapsed : StreamKind.None;
        }

        static void Add(string entry, PatternSet koreanSet, PatternSet englishSet, PatternSet collapsedSet)
        {
            if (entry == null) throw new ArgumentException("Entries must not contain null.", "entries");
            string body = entry.Trim();
            var flags = PatternFlags.None;
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

            bool hangul = false, latin = false, other = false;
            foreach (char c in body)
            {
                char clean = TextNormalizer.Clean(c);
                if (TextNormalizer.IsHangul(clean)) hangul = true;
                else if (TextNormalizer.IsLatinLetter(clean)) latin = true;
                else if (clean != TextNormalizer.Removed && !char.IsWhiteSpace(clean)) other = true;
            }
            if (!hangul && !latin) throw Malformed(entry, "has no Hangul or Latin letters");
            // Stream A drops non-Hangul characters, so a Korean entry with digits or symbols would silently shrink.
            if (hangul && (latin || other)) throw Malformed(entry, "mixes Hangul with other characters");
            if (hangul && (flags & (PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd)) != 0) throw Malformed(entry, "uses a word boundary on a Korean entry");

            var work = new char[body.Length];
            var normalized = new char[body.Length];
            if (hangul)
            {
                int length = TextNormalizer.Hangul(body, work, normalized);
                string key = new string(normalized, 0, length);
                if (length < 2 || TextNormalizer.ContainsJamo(key)) flags |= PatternFlags.Short;
                koreanSet.Add(key, flags);
                return;
            }

            int latinLength = TextNormalizer.Latin(body, work, normalized);
            if (latinLength == 0) throw Malformed(entry, "is empty after normalization");
            string latinKey = new string(normalized, 0, latinLength);
            englishSet.Add(latinKey, flags);
            if ((flags & PatternFlags.Allow) != 0)
            {
                int collapsedLength = TextNormalizer.Collapse(latinKey, work);
                collapsedSet.Add(new string(work, 0, collapsedLength), flags);
            }
            else if (!TextNormalizer.HasDoubledLetter(latinKey))
            {
                collapsedSet.Add(latinKey, flags);
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
