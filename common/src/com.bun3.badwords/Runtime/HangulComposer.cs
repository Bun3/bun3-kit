using System;

namespace Bun3.BadWords
{
    /// <summary>
    /// Composes Hangul compatibility jamo into syllables with 2-beolsik IME rules.
    /// Precomposed syllables and other characters pass through unchanged and flush pending jamo.
    /// </summary>
    internal static class HangulComposer
    {
        internal const char FirstSyllable = '\uAC00';
        internal const char LastSyllable = '\uD7A3';
        internal const char FirstConsonant = '\u3131';
        internal const char FirstVowel = '\u314F';
        internal const char LastVowel = '\u3163';

        // Indexed by (consonant - U+3131): initial index, or -1 when the consonant cannot start a syllable.
        static readonly sbyte[] initialOf = { 0, 1, -1, 2, -1, -1, 3, 4, 5, -1, -1, -1, -1, -1, -1, -1, 6, 7, 8, -1, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };

        // Indexed by (consonant - U+3131): final index, or 0 when the consonant cannot end a syllable.
        static readonly byte[] finalOf = { 1, 2, 3, 4, 5, 6, 7, 0, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 0, 18, 19, 20, 21, 22, 0, 23, 24, 25, 26, 27 };

        // Indexed by final index: the initial index a simple final becomes before a vowel.
        static readonly sbyte[] finalToInitial = { -1, 0, 1, -1, 2, -1, -1, 3, 5, -1, -1, -1, -1, -1, -1, -1, 6, 7, -1, 9, 10, 11, 12, 14, 15, 16, 17, 18 };

        // Indexed by initial index.
        const string initialJamo = "\u3131\u3132\u3134\u3137\u3138\u3139\u3141\u3142\u3143\u3145\u3146\u3147\u3148\u3149\u314A\u314B\u314C\u314D\u314E";

        /// <summary>Writes the composed text and returns its length (never more than <paramref name="source"/>.Length).</summary>
        internal static int Compose(ReadOnlySpan<char> source, Span<char> destination)
        {
            int written = 0, initial = -1, vowel = -1, final = 0;
            foreach (char c in source)
            {
                if (c >= FirstSyllable && c <= LastSyllable)
                {
                    Flush(destination, ref written, ref initial, ref vowel, ref final);
                    destination[written++] = c;
                }
                else if (c >= FirstConsonant && c < FirstVowel)
                {
                    int consonant = c - FirstConsonant;
                    if (initial >= 0 && vowel >= 0)
                    {
                        int next = final == 0 ? finalOf[consonant] : CombineFinal(final, finalOf[consonant]);
                        if (next > 0)
                        {
                            final = next;
                            continue;
                        }
                    }
                    Flush(destination, ref written, ref initial, ref vowel, ref final);
                    if (initialOf[consonant] >= 0) initial = initialOf[consonant];
                    else destination[written++] = c;
                }
                else if (c >= FirstVowel && c <= LastVowel)
                {
                    int v = c - FirstVowel;
                    if (initial >= 0 && vowel < 0)
                    {
                        vowel = v;
                        continue;
                    }
                    if (vowel >= 0 && final == 0)
                    {
                        int combined = CombineVowel(vowel, v);
                        if (combined >= 0)
                        {
                            vowel = combined;
                            continue;
                        }
                    }
                    if (initial >= 0 && vowel >= 0 && final > 0)
                    {
                        int nextInitial = SplitFinal(final, out final);
                        Flush(destination, ref written, ref initial, ref vowel, ref final);
                        initial = nextInitial;
                        vowel = v;
                        continue;
                    }
                    Flush(destination, ref written, ref initial, ref vowel, ref final);
                    vowel = v;
                }
                else
                {
                    Flush(destination, ref written, ref initial, ref vowel, ref final);
                    destination[written++] = c;
                }
            }
            Flush(destination, ref written, ref initial, ref vowel, ref final);
            return written;
        }

        static void Flush(Span<char> destination, ref int written, ref int initial, ref int vowel, ref int final)
        {
            if (initial >= 0 && vowel >= 0) destination[written++] = (char)(FirstSyllable + (initial * 21 + vowel) * 28 + final);
            else if (initial >= 0) destination[written++] = initialJamo[initial];
            else if (vowel >= 0) destination[written++] = (char)(FirstVowel + vowel);
            initial = -1;
            vowel = -1;
            final = 0;
        }

        static int CombineVowel(int first, int second)
        {
            switch (first)
            {
                case 8: return second == 0 ? 9 : second == 1 ? 10 : second == 20 ? 11 : -1;
                case 13: return second == 4 ? 14 : second == 5 ? 15 : second == 20 ? 16 : -1;
                case 18: return second == 20 ? 19 : -1;
                default: return -1;
            }
        }

        static int CombineFinal(int first, int second)
        {
            switch (first)
            {
                case 1: return second == 19 ? 3 : 0;
                case 4: return second == 22 ? 5 : second == 27 ? 6 : 0;
                case 8:
                    switch (second)
                    {
                        case 1: return 9;
                        case 16: return 10;
                        case 17: return 11;
                        case 19: return 12;
                        case 25: return 13;
                        case 26: return 14;
                        case 27: return 15;
                        default: return 0;
                    }
                case 17: return second == 19 ? 18 : 0;
                default: return 0;
            }
        }

        // Returns the initial that moves to the next syllable; remaining is the final that stays.
        static int SplitFinal(int final, out int remaining)
        {
            switch (final)
            {
                case 3: remaining = 1; return 9;
                case 5: remaining = 4; return 12;
                case 6: remaining = 4; return 18;
                case 9: remaining = 8; return 0;
                case 10: remaining = 8; return 6;
                case 11: remaining = 8; return 7;
                case 12: remaining = 8; return 9;
                case 13: remaining = 8; return 16;
                case 14: remaining = 8; return 17;
                case 15: remaining = 8; return 18;
                case 18: remaining = 17; return 9;
                default: remaining = 0; return finalToInitial[final];
            }
        }
    }
}
