using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Non-generic core of the item definition catalog — an immutable interned table built
    /// once at startup. Knows only the string id ↔ <see cref="ItemId"/> mapping and framework
    /// metadata (maxCount, external numeric id).
    /// The canonical key is the string id — persisted data always stores string ids;
    /// <see cref="ItemId"/> (a registration-order-dependent index) must never be persisted.
    /// Integration with numeric key systems (DB, Steam itemdefid) uses the optional
    /// external-id reverse index (<see cref="TryGetByExternalId"/>).
    /// Game definition schemas live in <see cref="ItemCatalog{TDefinition}"/>.
    /// Containers reference only this core (amount logic is definition-agnostic).
    /// </summary>
    public class ItemCatalog
    {
        internal const long NoExternalId = long.MinValue;

        private readonly string[] _ids;
        private readonly long[] _maxCounts;
        private readonly long[] _externalIds;
        private readonly bool[] _unstackables;
        private readonly long[] _regenPeriods;
        private readonly long[] _maxRegens;
        private readonly ItemId[] _regenItems;
        private readonly Dictionary<string, int> _lookup;
        private readonly Dictionary<long, int> _externalLookup;

        internal ItemCatalog(
            string[] ids,
            long[] maxCounts,
            long[] externalIds,
            bool[] unstackables,
            long[] regenPeriods,
            long[] maxRegens,
            ItemId[] regenItems,
            Dictionary<string, int> lookup,
            Dictionary<long, int> externalLookup)
        {
            _ids = ids;
            _maxCounts = maxCounts;
            _externalIds = externalIds;
            _unstackables = unstackables;
            _regenPeriods = regenPeriods;
            _maxRegens = maxRegens;
            _regenItems = regenItems;
            _lookup = lookup;
            _externalLookup = externalLookup;
        }

        /// <summary>Number of registered definitions.</summary>
        public int Count => _ids.Length;

        /// <summary>Looks up by string id. Returns false if absent; <paramref name="item"/> is None.</summary>
        public bool TryGet(string id, out ItemId item)
        {
            if (id != null && _lookup.TryGetValue(id, out var index))
            {
                item = new ItemId(index);
                return true;
            }

            item = ItemId.None;
            return false;
        }

        /// <summary>Looks up by string id. Throws <see cref="ItemCatalogException"/> if absent.</summary>
        public ItemId GetRequired(string id)
        {
            if (!TryGet(id, out var item))
            {
                throw new ItemCatalogException($"Item id not in catalog: '{id}'");
            }

            return item;
        }

        /// <summary>Whether the identifier is valid for this catalog (None or out of range is false).</summary>
        public bool Contains(ItemId item) => (uint)item.Index < (uint)_ids.Length;

        /// <summary>Returns the interned string id — allocation-free. Throws on an invalid identifier.</summary>
        public string GetIdString(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "Not an identifier of this catalog.");
            }

            return _ids[item.Index];
        }

        /// <summary>Hard cap on holdings per definition. <see cref="long.MaxValue"/> means unlimited.
        /// The regen target is <see cref="GetMaxRegen"/>. Throws on an invalid identifier.</summary>
        public long GetMaxCount(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "Not an identifier of this catalog.");
            }

            return _maxCounts[item.Index];
        }

        /// <summary>Whether the definition is unstackable (instance-based) — single source of truth
        /// for the stack/instance distinction. Throws on an invalid identifier.</summary>
        public bool IsUnstackable(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "Not an identifier of this catalog.");
            }

            return _unstackables[item.Index];
        }

        /// <summary>Regen period (ticks). 0 = no regen. Throws on an invalid identifier.</summary>
        public long GetRegenPeriodTicks(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "Not an identifier of this catalog.");
            }

            return _regenPeriods[item.Index];
        }

        /// <summary>Regen target — regen only fills while the total is below this value. 0 = no regen.
        /// Always at most <see cref="GetMaxCount"/>. Throws on an invalid identifier.</summary>
        public long GetMaxRegen(ItemId item)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "Not an identifier of this catalog.");
            }

            return _maxRegens[item.Index];
        }

        /// <summary>Definitions with regen metadata — iterated by
        /// <see cref="ItemInventory{TState}.SettleRegen"/> and the set whose regen anchor
        /// timestamps the game persists.</summary>
        public ReadOnlySpan<ItemId> RegenItems => _regenItems;

        /// <summary>Returns the external numeric id (DB column, Steam itemdefid, etc.).
        /// False if not registered. Throws on an invalid identifier.</summary>
        public bool TryGetExternalId(ItemId item, out long externalId)
        {
            if (!Contains(item))
            {
                throw new ArgumentOutOfRangeException(nameof(item), "Not an identifier of this catalog.");
            }

            externalId = _externalIds[item.Index];
            return externalId != NoExternalId;
        }

        /// <summary>Reverse lookup by external numeric id. Unregistered ids return false; <paramref name="item"/> is None.</summary>
        public bool TryGetByExternalId(long externalId, out ItemId item)
        {
            if (_externalLookup.TryGetValue(externalId, out var index))
            {
                item = new ItemId(index);
                return true;
            }

            item = ItemId.None;
            return false;
        }
    }
}
