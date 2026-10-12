#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace Bun3.Chat
{
    /// <summary>Normalizes user-typed chat text into a single displayable line.</summary>
    public static class ChatText
    {
        /// <summary>
        /// Replaces control and line/paragraph separator characters (including U+2028 and U+2029) with spaces,
        /// removes format characters of any plane, trims, and composes to NFC.
        /// Fails on lone surrogates, on text with nothing visible (only whitespace, combining marks, format
        /// characters, variation selectors or Hangul fillers), on more than <paramref name="maxLength"/>
        /// text elements, and on more than <paramref name="maxLength"/> × 4 UTF-8 bytes.
        /// </summary>
        /// <param name="input">Raw text; null counts as empty.</param>
        /// <param name="maxLength">Maximum number of text elements. Must be positive.</param>
        /// <param name="text">The normalized text, or an empty string on failure.</param>
        /// <returns>True when <paramref name="text"/> is valid.</returns>
        public static bool TryNormalize(string? input, int maxLength, out string text)
        {
            if (maxLength < 1) throw new ArgumentOutOfRangeException(nameof(maxLength));
            text = string.Empty;
            if (string.IsNullOrEmpty(input)) return false;

            var builder = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length;)
            {
                char c = input[i];
                if (char.IsLowSurrogate(c)) return false;
                int length = 1;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= input.Length || !char.IsLowSurrogate(input[i + 1])) return false;
                    length = 2;
                }
                var category = CharUnicodeInfo.GetUnicodeCategory(input, i);
                if (category == UnicodeCategory.Control || category == UnicodeCategory.LineSeparator ||
                    category == UnicodeCategory.ParagraphSeparator) builder.Append(' ');
                else if (category != UnicodeCategory.Format) builder.Append(input, i, length);
                i += length;
            }

            string normalized;
            try { normalized = builder.ToString().Trim().Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return false; }
            if (!HasVisibleCharacter(normalized)) return false;
            if (new StringInfo(normalized).LengthInTextElements > maxLength) return false;
            if (Encoding.UTF8.GetByteCount(normalized) > (long)maxLength * 4) return false;
            text = normalized;
            return true;
        }

        static bool HasVisibleCharacter(string value)
        {
            for (int i = 0; i < value.Length; i += char.IsHighSurrogate(value[i]) ? 2 : 1)
            {
                if (char.IsWhiteSpace(value, i)) continue;
                switch (CharUnicodeInfo.GetUnicodeCategory(value, i))
                {
                    case UnicodeCategory.NonSpacingMark:
                    case UnicodeCategory.EnclosingMark:
                    case UnicodeCategory.Format:
                        continue;
                }
                int code = char.ConvertToUtf32(value, i);
                bool blankFiller = code == 0x115F || code == 0x1160 || code == 0x3164 || code == 0xFFA0;
                bool variationSelector = (code >= 0xFE00 && code <= 0xFE0F) || (code >= 0xE0100 && code <= 0xE01EF);
                if (!blankFiller && !variationSelector) return true;
            }
            return false;
        }
    }
}
