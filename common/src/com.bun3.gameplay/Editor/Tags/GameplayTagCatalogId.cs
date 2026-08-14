#nullable enable
using System;
using System.Text;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagCatalogId
    {
        internal static string Normalize(string value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            var result = new StringBuilder(value.Length);
            var pendingSeparator = false;
            foreach (var input in value)
            {
                var character = char.ToLowerInvariant(input);
                if ((character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9'))
                {
                    if (pendingSeparator && result.Length > 0) result.Append('-');
                    result.Append(character);
                    pendingSeparator = false;
                }
                else
                {
                    pendingSeparator = true;
                }
            }

            return result.ToString();
        }

        internal static string Require(string value, string parameterName)
        {
            var result = Normalize(value);
            if (result.Length == 0)
            {
                throw new ArgumentException(
                    "Catalog ID must contain at least one ASCII letter or digit.", parameterName);
            }

            return result;
        }
    }
}
