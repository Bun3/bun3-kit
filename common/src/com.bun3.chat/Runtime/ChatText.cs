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
        /// Replaces control characters with spaces, removes format characters, trims, and composes to NFC.
        /// Fails on lone surrogates, on text with nothing visible, on more than <paramref name="maxLength"/>
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
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= input.Length || !char.IsLowSurrogate(input[i + 1])) return false;
                    builder.Append(c).Append(input[++i]);
                    continue;
                }
                if (char.IsLowSurrogate(c)) return false;
                var category = char.GetUnicodeCategory(c);
                if (category == UnicodeCategory.Control) builder.Append(' ');
                else if (category != UnicodeCategory.Format) builder.Append(c);
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
            foreach (char c in value)
                if (!char.IsWhiteSpace(c) && char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) return true;
            return false;
        }
    }
}
