#nullable enable
using System;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// Tag and comment declared by a single source.
    /// </summary>
    public sealed class TagSourceTag
    {
        /// <summary>Lowercase-normalized tag name.</summary>
        public string Name { get; }

        /// <summary>Tag comment the source provided.</summary>
        public string Comment { get; }

        /// <summary>Creates a tag source row.</summary>
        public TagSourceTag(string name, string comment)
        {
            if (!TagName.TryFold(name, out var canonical))
            {
                throw new ArgumentException("Invalid tag name format.", nameof(name));
            }

            Name = canonical;
            Comment = comment ?? throw new ArgumentNullException(nameof(comment));
        }
    }
}
