#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// Common base for containers bound to one catalog that answer hierarchical tag queries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One catalog per process.</b> A <see cref="GameplayTag"/> is a 2-byte index that is only
    /// meaningful inside its catalog; mixing tags from different catalogs makes hierarchical
    /// queries silently return wrong answers even when indices coincide.
    /// </para>
    /// <para>
    /// Mutation paths reject out-of-range indices with an exception. Same-sized foreign catalogs
    /// cannot be detected by index alone, so honoring the contract is the responsibility of the
    /// caller. Query paths are tick hot paths, so out-of-range tags are treated as non-matches
    /// instead of throwing.
    /// </para>
    /// </remarks>
    public abstract class TagQueryContainer
    {
        private readonly TagCatalog _catalog;

        // internal ctor - blocks external derivation so derived types stay effectively sealed.
        internal TagQueryContainer(TagCatalog catalog) => _catalog = catalog;

        internal TagCatalog Catalog => _catalog;

        /// <summary>Checks whether the query tag itself or one of its descendants is held in this container.</summary>
        /// <param name="tag">Tag to query.</param>
        /// <returns><see langword="true"/> if a matching tag is present.</returns>
        public abstract bool Has(GameplayTag tag);

        /// <summary>Checks whether the query tag is held exactly.</summary>
        /// <param name="tag">Tag to query.</param>
        /// <returns><see langword="true"/> if an exactly matching tag is present.</returns>
        public abstract bool HasExact(GameplayTag tag);

        /// <summary>Checks whether any hierarchical query tag matches this container.</summary>
        /// <param name="query">Query tags built from the same catalog instance.</param>
        /// <returns><see langword="true"/> if any query tag matches.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/> belongs to a different catalog instance.</exception>
        public bool HasAny(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query.ExactKindCount; i++)
            {
                if (Has(new GameplayTag(query.GetExactIndexAt(i))))
                    return true;
            }

            return false;
        }

        /// <summary>Checks whether all hierarchical query tags match this container.</summary>
        /// <param name="query">Query tags built from the same catalog instance.</param>
        /// <returns><see langword="true"/> if all query tags match, including an empty query.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/> belongs to a different catalog instance.</exception>
        public bool HasAll(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query.ExactKindCount; i++)
            {
                if (!Has(new GameplayTag(query.GetExactIndexAt(i))))
                    return false;
            }

            return true;
        }

        /// <summary>Checks whether any exact query tag is present in this container.</summary>
        /// <param name="query">Query tags built from the same catalog instance.</param>
        /// <returns><see langword="true"/> if any exact query tag matches.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/> belongs to a different catalog instance.</exception>
        public bool HasAnyExact(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query.ExactKindCount; i++)
            {
                if (HasExact(new GameplayTag(query.GetExactIndexAt(i))))
                    return true;
            }

            return false;
        }

        /// <summary>Checks whether all exact query tags are present in this container.</summary>
        /// <param name="query">Query tags built from the same catalog instance.</param>
        /// <returns><see langword="true"/> if all exact query tags match, including an empty query.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="query"/> belongs to a different catalog instance.</exception>
        public bool HasAllExact(TagContainer query)
        {
            ValidateQueryCatalog(query);
            for (var i = 0; i < query.ExactKindCount; i++)
            {
                if (!HasExact(new GameplayTag(query.GetExactIndexAt(i))))
                    return false;
            }

            return true;
        }

        // Shared mutation-path validation - rejects None and indices from other catalogs.
        private protected void ValidateMutationTag(GameplayTag tag)
        {
            if (!tag.IsValid)
                throw new ArgumentException("GameplayTag.None cannot be stored in a container.", nameof(tag));
            if (tag.Index > _catalog.Count)
                throw new ArgumentOutOfRangeException(
                    nameof(tag),
                    "Tag is outside the catalog range of this container; there must be one catalog per process.");
        }

        private void ValidateQueryCatalog(TagContainer query)
        {
            if (query is null)
                throw new ArgumentNullException(nameof(query));
            if (!ReferenceEquals(_catalog, query.Catalog))
                throw new ArgumentException("Tag queries require the same TagCatalog instance.", nameof(query));
        }
    }
}
