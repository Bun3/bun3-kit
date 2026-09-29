#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// Entire document read from or written to one tag source.
    /// </summary>
    public sealed class TagSourceDocument
    {
        /// <summary>Descriptor of the source that owns the document.</summary>
        public TagSourceDescriptor Descriptor { get; }

        /// <summary>Origin path or declaration label used in error diagnostics.</summary>
        public string Origin { get; }

        /// <summary>Tags the source declared explicitly.</summary>
        public IReadOnlyList<TagSourceTag> Tags { get; }

        /// <summary>Tag redirects the source declared.</summary>
        public IReadOnlyList<TagSourceRedirect> Redirects { get; }

        /// <summary>Creates a defensively copied tag source document.</summary>
        public TagSourceDocument(
            TagSourceDescriptor descriptor,
            string origin,
            IReadOnlyList<TagSourceTag> tags,
            IReadOnlyList<TagSourceRedirect> redirects)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            Tags = Copy(tags, nameof(tags));
            Redirects = Copy(redirects, nameof(redirects));
        }

        private static T[] Copy<T>(IReadOnlyList<T> values, string parameterName)
        {
            if (values is null) throw new ArgumentNullException(parameterName);
            var copy = new T[values.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }
}
