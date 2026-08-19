#nullable enable

namespace Bun3.Gameplay.Tags
{
    internal static class TagName
    {
        internal const int MaximumLength = 255;
        internal const int MaximumDepth = 16;

        internal static string ValidateAndFold(
            string value,
            string jsonPath,
            int lineNumber,
            int linePosition)
        {
            if (!TryFold(value, out var folded))
            {
                throw new TagCatalogException(
                    "Tag name must be a dot-joined path of 1..255 ASCII alphanumeric segments, at most 16 levels deep.",
                    jsonPath,
                    lineNumber,
                    linePosition);
            }

            return folded;
        }

        internal static bool TryFold(string? value, out string folded)
        {
            folded = string.Empty;
            if (value is null || value.Length == 0 || value.Length > MaximumLength)
            {
                return false;
            }

            var chars = value.ToCharArray();
            var depth = 1;
            var segmentLength = 0;
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (c == '.')
                {
                    if (segmentLength == 0 || ++depth > MaximumDepth)
                    {
                        return false;
                    }

                    segmentLength = 0;
                    continue;
                }

                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                {
                    return false;
                }

                if (c >= 'A' && c <= 'Z')
                {
                    chars[i] = (char)(c + ('a' - 'A'));
                }

                segmentLength++;
            }

            if (segmentLength == 0)
            {
                return false;
            }

            folded = new string(chars);
            return true;
        }
    }
}
