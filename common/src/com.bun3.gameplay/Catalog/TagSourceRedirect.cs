#nullable enable
using System;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// Source redirect replacing one tag name with another.
    /// </summary>
    public sealed class TagSourceRedirect
    {
        /// <summary>Lowercase-normalized old tag name.</summary>
        public string From { get; }

        /// <summary>Lowercase-normalized target tag name.</summary>
        public string To { get; }

        /// <summary>Creates a tag source redirect.</summary>
        public TagSourceRedirect(string from, string to)
        {
            if (!TagName.TryFold(from, out var canonicalFrom))
            {
                throw new ArgumentException("Invalid redirect origin tag name format.", nameof(from));
            }

            if (!TagName.TryFold(to, out var canonicalTo))
            {
                throw new ArgumentException("Invalid redirect target tag name format.", nameof(to));
            }

            From = canonicalFrom;
            To = canonicalTo;
        }
    }
}
