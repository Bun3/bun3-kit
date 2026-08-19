using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Catalog secondary index — allocation-free lookup of <see cref="ItemId"/> lists by a
    /// definition key (type, tag, category, etc.). Declared via
    /// <see cref="ItemCatalogBuilder{TDefinition}.CreateIndex{TKey}"/> and built once during
    /// catalog <c>Build()</c>. Prefer this index and build-time resolved references over
    /// hardcoded string lookups.
    /// </summary>
    /// <typeparam name="TKey">Index key type.</typeparam>
    public sealed class ItemCatalogIndex<TKey> where TKey : notnull
    {
        private Dictionary<TKey, ItemId[]>? _map;

        internal ItemCatalogIndex()
        {
        }

        /// <summary>Returns the items for the key. Unregistered keys yield an empty span.
        /// Calling before catalog Build throws (startup ordering error).</summary>
        public ReadOnlySpan<ItemId> Get(TKey key)
        {
            if (_map == null)
            {
                throw new InvalidOperationException("Index cannot be queried before catalog Build.");
            }

            return _map.TryGetValue(key, out var items) ? items : ReadOnlySpan<ItemId>.Empty;
        }

        /// <summary>Whether the key exists in the index. Calling before Build throws.</summary>
        public bool Contains(TKey key)
        {
            if (_map == null)
            {
                throw new InvalidOperationException("Index cannot be queried before catalog Build.");
            }

            return _map.ContainsKey(key);
        }

        internal void Build(Dictionary<TKey, List<ItemId>> entries)
        {
            var map = new Dictionary<TKey, ItemId[]>(entries.Count);
            foreach (var entry in entries)
            {
                map.Add(entry.Key, entry.Value.ToArray());
            }

            _map = map;
        }
    }
}
