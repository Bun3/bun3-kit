using System;
using System.Globalization;

namespace Bun3.BadWords
{
    /// <summary>
    /// Builds the normalized streams scanned by <see cref="BadWordFilter"/>.
    /// Every output is no longer than its input; work and destination buffers must not overlap.
    /// </summary>
    /// <remarks>Partial: <c>TextNormalizer.cs</c> (streams) and the generated <c>TextNormalizer.Simplified.cs</c> (Traditional to Simplified pairs).</remarks>
    internal static partial class TextNormalizer
    {
        internal const char Removed = '\0';
        internal const char Other = '\uFFFF';

        // U+00C0..U+00FF folded to lowercase ASCII; '?' marks letters without an ASCII base.
        const string latin1 = "aaaaaa?ceeeeiiii?nooooo?ouuuuy?saaaaaa?ceeeeiiii?nooooo?ouuuuy?y";

        // U+FF66..U+FF9D halfwidth katakana to fullwidth, from their Unicode <narrow> decompositions.
        const string halfwidthKatakana =
            "\u30F2\u30A1\u30A3\u30A5\u30A7\u30A9\u30E3\u30E5\u30E7\u30C3\u30FC\u30A2\u30A4\u30A6\u30A8\u30AA" +
            "\u30AB\u30AD\u30AF\u30B1\u30B3\u30B5\u30B7\u30B9\u30BB\u30BD\u30BF\u30C1\u30C4\u30C6\u30C8\u30CA" +
            "\u30CB\u30CC\u30CD\u30CE\u30CF\u30D2\u30D5\u30D8\u30DB\u30DE\u30DF\u30E0\u30E1\u30E2\u30E4\u30E6" +
            "\u30E8\u30E9\u30EA\u30EB\u30EC\u30ED\u30EF\u30F3";

        // Katakana that a voiced mark turns into the next code point, and the subset a semi-voiced mark turns into the one after.
        const string voiceable = "\u30AB\u30AD\u30AF\u30B1\u30B3\u30B5\u30B7\u30B9\u30BB\u30BD\u30BF\u30C1\u30C4\u30C6\u30C8\u30CF\u30D2\u30D5\u30D8\u30DB";
        const string semiVoiceable = "\u30CF\u30D2\u30D5\u30D8\u30DB";

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
            if (c == '\u1E9E') return 's';
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

        internal static int Cyrillic(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char letter = CyrillicLetter(raw);
                if (letter == Removed) continue;
                if (letter != ' ') work[count++] = letter;
                else if (count > 0 && work[count - 1] != ' ') work[count++] = ' ';
            }
            if (count > 0 && work[count - 1] == ' ') count--;
            return JoinSingleLetters(work.Slice(0, count), destination);
        }

        // Neither Japanese nor Chinese separates words with spaces, so separators are dropped rather than kept as boundaries.
        internal static int Cjk(ReadOnlySpan<char> text, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char c = Kana(raw);
                if (c == '\u3099' || c == '\u309B' || c == '\uFF9E')
                {
                    if (count > 0) destination[count - 1] = Voiced(destination[count - 1]);
                }
                else if (c == '\u309A' || c == '\u309C' || c == '\uFF9F')
                {
                    if (count > 0 && semiVoiceable.IndexOf(destination[count - 1]) >= 0) destination[count - 1] = (char)(destination[count - 1] + 2);
                }
                else if ((c >= '\u30A1' && c <= '\u30FA') || c == '\u3005')
                {
                    destination[count++] = c;
                }
                else if (IsHan(c))
                {
                    destination[count++] = Simplified(c);
                }
            }
            return count;
        }

        internal static char Kana(char c)
        {
            if (c >= '\u3041' && c <= '\u3096') return (char)(c + 0x60);
            if (c >= '\uFF66' && c <= '\uFF9D') return halfwidthKatakana[c - '\uFF66'];
            return c;
        }

        internal static bool IsCjk(char c) =>
            (c >= '\u3041' && c <= '\u30FF') || (c >= '\uFF66' && c <= '\uFF9F') || c == '\u3005' || IsHan(c);

        internal static bool HasCjk(ReadOnlySpan<char> text)
        {
            foreach (char c in text)
                if (IsCjk(c)) return true;
            return false;
        }

        internal static char Simplified(char c)
        {
            int low = 0, high = traditional.Length - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                char key = traditional[middle];
                if (key == c) return simplified[middle];
                if (key < c) low = middle + 1;
                else high = middle - 1;
            }
            return c;
        }

        static bool IsHan(char c) =>
            (c >= '\u3400' && c <= '\u4DBF') || (c >= '\u4E00' && c <= '\u9FFF') || (c >= '\uF900' && c <= '\uFAFF');

        static char Voiced(char c)
        {
            if (c == '\u30A6') return '\u30F4';
            return voiceable.IndexOf(c) >= 0 ? (char)(c + 1) : c;
        }

        internal static bool IsCyrillic(char c) => c >= '\u0400' && c <= '\u04FF';

        internal static bool HasCyrillic(ReadOnlySpan<char> text)
        {
            foreach (char c in text)
                if (IsCyrillic(c)) return true;
            return false;
        }

        // Stream D folds the other way from Lookalike: Latin letters drawn like Cyrillic ones become Cyrillic.
        internal static char CyrillicLetter(char c)
        {
            if (c >= '\uFF01' && c <= '\uFF5E') c = (char)(c - 0xFEE0);
            if (IsCyrillic(c) && char.IsLetter(c))
            {
                char lower = char.ToLowerInvariant(c);
                if (lower == '\u0451') return '\u0435';
                return lower == '\u0439' ? '\u0438' : lower;
            }
            char lookalike = c switch
            {
                'a' or 'A' => '\u0430',
                'c' or 'C' => '\u0441',
                'e' or 'E' => '\u0435',
                'o' or 'O' => '\u043E',
                'p' or 'P' => '\u0440',
                'x' or 'X' => '\u0445',
                'y' or 'Y' => '\u0443',
                'B' => '\u0432',
                'H' => '\u043D',
                'K' => '\u043A',
                'M' => '\u043C',
                'T' => '\u0442',
                _ => Removed,
            };
            if (lookalike != Removed) return lookalike;
            return Clean(c) == Removed ? Removed : ' ';
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
        internal static char Lookalike(char c) => c switch
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
