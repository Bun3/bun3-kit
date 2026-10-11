using System;
using System.Globalization;

namespace Bun3.BadWords
{
    /// <summary>
    /// Builds the normalized streams scanned by <see cref="BadWordFilter"/>.
    /// Every output is no longer than its input; work and destination buffers must not overlap.
    /// </summary>
    internal static class TextNormalizer
    {
        internal const char Removed = '\0';
        internal const char Other = '\uFFFF';

        // U+00C0..U+00FF folded to lowercase ASCII; '?' marks letters without an ASCII base.
        const string latin1 = "aaaaaa?ceeeeiiii?nooooo?ouuuuy??aaaaaa?ceeeeiiii?nooooo?ouuuuy?y";

        // KS X 5002 2-beolsik layout indexed by (lowercase letter - 'a').
        const string keyboard = "\u3141\u3160\u314A\u3147\u3137\u3139\u314E\u3157\u3151\u3153\u314F\u3163\u3161\u315C\u3150\u3154\u3142\u3131\u3134\u3145\u3155\u314D\u3148\u314C\u315B\u314B";

        internal static char Clean(char c)
        {
            if (c < '\u0080') return c;
            if (c >= '\u00C0' && c <= '\u00FF')
            {
                char folded = latin1[c - '\u00C0'];
                return folded == '?' ? Other : folded;
            }
            if (c >= '\uFF01' && c <= '\uFF5E') return (char)(c - 0xFEE0);
            if (c == '\u115F' || c == '\u1160' || c == '\u3164' || c == '\uFFA0') return Removed;
            char lookalike = Lookalike(c);
            if (lookalike != Removed) return lookalike;
            switch (char.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Format:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.EnclosingMark:
                    return Removed;
                case UnicodeCategory.Surrogate:
                    return Other;
                default:
                    return c;
            }
        }

        internal static int Hangul(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char c = Clean(raw);
                if (IsHangul(c)) work[count++] = c;
            }
            return HangulComposer.Compose(work.Slice(0, count), destination);
        }

        internal static int Keyboard(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char c = Clean(raw);
                if (c >= 'a' && c <= 'z') work[count++] = keyboard[c - 'a'];
                else if (c >= 'A' && c <= 'Z') work[count++] = Shifted(c);
            }
            return count == 0 ? 0 : HangulComposer.Compose(work.Slice(0, count), destination);
        }

        internal static int Latin(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char c = Clean(raw);
                if (c == Removed) continue;
                char letter = LatinLetter(c);
                if (letter != ' ') work[count++] = letter;
                else if (count > 0 && work[count - 1] != ' ') work[count++] = ' ';
            }
            if (count > 0 && work[count - 1] == ' ') count--;
            return JoinSingleLetters(work.Slice(0, count), destination);
        }

        internal static int Collapse(ReadOnlySpan<char> text, Span<char> destination)
        {
            int count = 0;
            foreach (char c in text)
                if (count == 0 || destination[count - 1] != c) destination[count++] = c;
            return count;
        }

        internal static bool IsHangul(char c) =>
            (c >= HangulComposer.FirstSyllable && c <= HangulComposer.LastSyllable) ||
            (c >= HangulComposer.FirstConsonant && c <= HangulComposer.LastVowel);

        internal static bool IsLatinLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        internal static bool ContainsJamo(string text)
        {
            foreach (char c in text)
                if (c >= HangulComposer.FirstConsonant && c <= HangulComposer.LastVowel) return true;
            return false;
        }

        internal static bool HasDoubledLetter(string text)
        {
            for (int i = 1; i < text.Length; i++)
                if (text[i] != ' ' && text[i] == text[i - 1]) return true;
            return false;
        }

        // Input holds tokens separated by single spaces with no leading or trailing space.
        static int JoinSingleLetters(ReadOnlySpan<char> text, Span<char> destination)
        {
            int written = 0, i = 0;
            while (i < text.Length)
            {
                if (written > 0) destination[written++] = ' ';
                int run = 0;
                while (i + 2 * run < text.Length && (i + 2 * run + 1 == text.Length || text[i + 2 * run + 1] == ' ')) run++;
                if (run >= 3)
                {
                    for (int k = 0; k < run; k++) destination[written++] = text[i + 2 * k];
                    i += 2 * run;
                }
                else
                {
                    int end = i;
                    while (end < text.Length && text[end] != ' ') end++;
                    for (int k = i; k < end; k++) destination[written++] = text[k];
                    i = end + 1;
                }
            }
            return written;
        }

        static char LatinLetter(char c)
        {
            if (c >= 'a' && c <= 'z') return c;
            if (c >= 'A' && c <= 'Z') return (char)(c + 32);
            switch (c)
            {
                case '4':
                case '@':
                    return 'a';
                case '3':
                    return 'e';
                case '1':
                    return 'i';
                case '0':
                    return 'o';
                case '5':
                case '$':
                    return 's';
                case '7':
                    return 't';
                default:
                    return ' ';
            }
        }

        static char Shifted(char c) => c switch
        {
            'Q' => '\u3143',
            'W' => '\u3149',
            'E' => '\u3138',
            'R' => '\u3132',
            'T' => '\u3146',
            'O' => '\u3152',
            'P' => '\u3156',
            _ => keyboard[c - 'A'],
        };

        // Subset of Unicode UTS #39 confusables: Cyrillic and Greek letters drawn like Latin letters.
        static char Lookalike(char c) => c switch
        {
            '\u0430' or '\u0410' or '\u03B1' or '\u0391' => 'a',
            '\u0412' or '\u0392' => 'b',
            '\u0441' or '\u0421' => 'c',
            '\u0501' => 'd',
            '\u0435' or '\u0415' or '\u0395' => 'e',
            '\u04BB' or '\u041D' or '\u0397' => 'h',
            '\u0456' or '\u03B9' or '\u0399' => 'i',
            '\u0458' => 'j',
            '\u041A' or '\u03BA' or '\u039A' => 'k',
            '\u04CF' => 'l',
            '\u041C' or '\u039C' => 'm',
            '\u039D' => 'n',
            '\u043E' or '\u041E' or '\u03BF' or '\u039F' => 'o',
            '\u0440' or '\u0420' or '\u03C1' or '\u03A1' => 'p',
            '\u0455' => 's',
            '\u0422' or '\u03C4' or '\u03A4' => 't',
            '\u03BD' => 'v',
            '\u0445' or '\u0425' or '\u03A7' => 'x',
            '\u0443' or '\u0423' or '\u03A5' => 'y',
            '\u0396' => 'z',
            _ => Removed,
        };
    }
}
