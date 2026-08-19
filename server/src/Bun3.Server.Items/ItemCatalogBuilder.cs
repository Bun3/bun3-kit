using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Catalog builder — at startup the game fills it from its definition source (DB/JSON/code)
    /// and calls <see cref="Build"/> once to produce an immutable catalog. Validator delegates
    /// run in bulk at build time; failures throw <see cref="ItemCatalogException"/> and block startup.
    /// </summary>
    /// <typeparam name="TDefinition">Game-defined item definition type.</typeparam>
    public sealed class ItemCatalogBuilder<TDefinition>
    {
        private readonly List<string> _ids = new List<string>();
        private readonly List<TDefinition> _definitions = new List<TDefinition>();
        private readonly List<long> _maxCounts = new List<long>();
        private readonly List<long> _externalIds = new List<long>();
        private readonly List<bool> _unstackables = new List<bool>();
        private readonly List<long> _regenPeriods = new List<long>();
        private readonly List<long> _maxRegens = new List<long>();
        private readonly Dictionary<string, int> _lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<long, int> _externalLookup = new Dictionary<long, int>();
        private readonly List<Action<ItemCatalog<TDefinition>>> _validators = new List<Action<ItemCatalog<TDefinition>>>();
        private readonly List<Action> _indexBuilders = new List<Action>();
        private bool _built;

        /// <summary>
        /// Registers a definition. The id is interned by the catalog and
        /// <see cref="ItemCatalog.GetIdString"/> later returns the same reference.
        /// </summary>
        /// <param name="id">Unique string id (ordinal comparison) — the canonical key. Duplicates throw.</param>
        /// <param name="definition">Game definition (stored opaquely).</param>
        /// <param name="maxCount">Hard cap on holdings per definition (stackable = amount,
        /// unstackable = instance count). Default <see cref="long.MaxValue"/> = unlimited — regen
        /// definitions also allow accruing past the target by default; to forbid strictly, set
        /// maxCount equal to maxRegen. Non-positive values are rejected.</param>
        /// <param name="externalId">Optional external numeric id (DB column, Steam itemdefid, etc.) —
        /// registered in the reverse index. Duplicates throw. <see cref="long.MinValue"/> is reserved
        /// and rejected.</param>
        /// <param name="unstackable">If true, the definition is unstackable (instance-based) — held
        /// as N amount-1 instances instead of a merged amount, with maxCount as the max instance count.</param>
        /// <param name="regenPeriodTicks">Regen period (ticks). 0 = no regen. When set,
        /// <see cref="ItemInventory{TState}.SettleRegen"/> auto-settles this definition — requires
        /// stackable + <paramref name="maxRegen"/>, and only integer amounts are allowed.</param>
        /// <param name="maxRegen">Regen target — regen only fills while the total is below this value.
        /// Must be at most maxCount (a violation is a data error and throws). Explicit grants may
        /// accrue past the target up to maxCount.</param>
        /// <returns>This builder, for chaining.</returns>
        public ItemCatalogBuilder<TDefinition> Register(
            string id,
            TDefinition definition,
            long maxCount = long.MaxValue,
            long? externalId = null,
            bool unstackable = false,
            long regenPeriodTicks = 0,
            long maxRegen = 0)
        {
            ThrowIfBuilt();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Item id must not be empty.", nameof(id));
            }

            if (maxCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "maxCount must be at least 1.");
            }

            if (maxRegen < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRegen), maxRegen, "maxRegen must be 0 (none) or greater.");
            }

            if (externalId == ItemCatalog.NoExternalId)
            {
                throw new ArgumentOutOfRangeException(nameof(externalId), externalId, "long.MinValue is reserved.");
            }

            if (regenPeriodTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(regenPeriodTicks), regenPeriodTicks, "Regen period must be 0 (none) or positive.");
            }

            if (regenPeriodTicks > 0 && unstackable)
            {
                throw new ItemCatalogException($"Unstackable definitions do not support regen: '{id}'");
            }

            if (regenPeriodTicks > 0 && maxRegen <= 0)
            {
                throw new ItemCatalogException($"Regen definitions require maxRegen (target): '{id}'");
            }

            if (maxRegen > 0 && regenPeriodTicks <= 0)
            {
                throw new ItemCatalogException($"maxRegen is meaningless without a regen period: '{id}'");
            }

            if (maxRegen > maxCount)
            {
                throw new ItemCatalogException($"maxRegen ({maxRegen}) must be at most maxCount ({maxCount}): '{id}'");
            }

            if (_lookup.ContainsKey(id))
            {
                throw new ItemCatalogException($"Duplicate item id: '{id}'");
            }

            if (externalId.HasValue && _externalLookup.ContainsKey(externalId.Value))
            {
                throw new ItemCatalogException($"Duplicate external id: {externalId.Value} (item '{id}')");
            }

            if (externalId.HasValue)
            {
                _externalLookup.Add(externalId.Value, _ids.Count);
            }

            _lookup.Add(id, _ids.Count);
            _ids.Add(id);
            _definitions.Add(definition);
            _maxCounts.Add(maxCount);
            _externalIds.Add(externalId ?? ItemCatalog.NoExternalId);
            _unstackables.Add(unstackable);
            _regenPeriods.Add(regenPeriodTicks);
            _maxRegens.Add(maxRegen);
            return this;
        }

        /// <summary>
        /// Adds a validator delegate run at build time. Game-rule violations throw
        /// <see cref="ItemCatalogException"/> and block startup.
        /// </summary>
        /// <param name="validator">Delegate that receives the finished catalog and validates it.</param>
        /// <returns>This builder, for chaining.</returns>
        public ItemCatalogBuilder<TDefinition> AddValidator(Action<ItemCatalog<TDefinition>> validator)
        {
            ThrowIfBuilt();
            _validators.Add(validator ?? throw new ArgumentNullException(nameof(validator)));
            return this;
        }

        /// <summary>
        /// Declares a single-key secondary index (type, category, etc.). The index is built once
        /// during <see cref="Build"/>; querying before that throws. Indexes are built before
        /// validator delegates run, so validators may use them.
        /// </summary>
        /// <typeparam name="TKey">Index key type.</typeparam>
        /// <param name="keySelector">Selector extracting the key from a definition (called only at build time).</param>
        /// <returns>Index handle, queryable after build.</returns>
        public ItemCatalogIndex<TKey> CreateIndex<TKey>(Func<TDefinition, TKey> keySelector)
            where TKey : notnull
        {
            ThrowIfBuilt();
            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }

            var index = new ItemCatalogIndex<TKey>();
            _indexBuilders.Add(() =>
            {
                var entries = new Dictionary<TKey, List<ItemId>>();
                for (var i = 0; i < _definitions.Count; i++)
                {
                    AddIndexEntry(entries, keySelector(_definitions[i]), new ItemId(i));
                }

                index.Build(entries);
            });
            return index;
        }

        /// <summary>Declares a multi-key secondary index (tag lists, etc.). Behaves like
        /// <see cref="CreateIndex{TKey}"/> but allows multiple keys per definition.</summary>
        /// <typeparam name="TKey">Index key type.</typeparam>
        /// <param name="keysSelector">Selector extracting the keys from a definition (called only at build time).</param>
        /// <returns>Index handle, queryable after build.</returns>
        public ItemCatalogIndex<TKey> CreateMultiIndex<TKey>(Func<TDefinition, IEnumerable<TKey>> keysSelector)
            where TKey : notnull
        {
            ThrowIfBuilt();
            if (keysSelector == null)
            {
                throw new ArgumentNullException(nameof(keysSelector));
            }

            var index = new ItemCatalogIndex<TKey>();
            _indexBuilders.Add(() =>
            {
                var entries = new Dictionary<TKey, List<ItemId>>();
                for (var i = 0; i < _definitions.Count; i++)
                {
                    foreach (var key in keysSelector(_definitions[i]))
                    {
                        AddIndexEntry(entries, key, new ItemId(i));
                    }
                }

                index.Build(entries);
            });
            return index;
        }

        private static void AddIndexEntry<TKey>(Dictionary<TKey, List<ItemId>> entries, TKey key, ItemId item)
            where TKey : notnull
        {
            if (!entries.TryGetValue(key, out var list))
            {
                list = new List<ItemId>();
                entries.Add(key, list);
            }

            list.Add(item);
        }

        /// <summary>Builds the catalog and runs validation. May be called only once per builder.</summary>
        public ItemCatalog<TDefinition> Build()
        {
            ThrowIfBuilt();
            _built = true;

            var regenItems = new List<ItemId>();
            for (var i = 0; i < _regenPeriods.Count; i++)
            {
                if (_regenPeriods[i] > 0)
                {
                    regenItems.Add(new ItemId(i));
                }
            }

            var catalog = new ItemCatalog<TDefinition>(
                _ids.ToArray(),
                _maxCounts.ToArray(),
                _externalIds.ToArray(),
                _unstackables.ToArray(),
                _regenPeriods.ToArray(),
                _maxRegens.ToArray(),
                regenItems.ToArray(),
                _lookup,
                _externalLookup,
                _definitions.ToArray());

            foreach (var indexBuilder in _indexBuilders)
            {
                indexBuilder();
            }

            foreach (var validator in _validators)
            {
                validator(catalog);
            }

            return catalog;
        }

        private void ThrowIfBuilt()
        {
            if (_built)
            {
                throw new InvalidOperationException("Builder already built — the catalog is created once at startup.");
            }
        }
    }
}
