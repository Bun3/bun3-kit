using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Catalog holding game definitions. The definition schema <typeparamref name="TDefinition"/>
    /// belongs to the game; the framework stores it opaquely.
    /// Built once at startup via <see cref="ItemCatalogBuilder{TDefinition}"/>.
    /// </summary>
    /// <typeparam name="TDefinition">Game-defined item definition type.</typeparam>
    public sealed class ItemCatalog<TDefinition> : ItemCatalog
    {
        private readonly TDefinition[] _definitions;

        internal ItemCatalog(
            string[] ids,
            long[] maxCounts,
            long[] externalIds,
            bool[] unstackables,
            long[] regenPeriods,
            long[] maxRegens,
            ItemId[] regenItems,
            Dictionary<string, int> lookup,
            Dictionary<long, int> externalLookup,
            TDefinition[] definitions)
            : base(ids, maxCounts, externalIds, unstackables, regenPeriods, maxRegens, regenItems, lookup, externalLookup)
        {
            _definitions = definitions;
        }

        /// <summary>Returns the definition. Throws on an invalid identifier.</summary>
        public TDefinition GetDefinition(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "Not an identifier of this catalog.");
            }

            return _definitions[item.Index];
        }
    }
}
