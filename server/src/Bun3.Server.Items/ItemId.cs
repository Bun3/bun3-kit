using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Item definition identifier interned in a catalog. Hot paths use only this value,
    /// never strings. <c>default</c> equals <see cref="None"/> and belongs to no catalog.
    /// With multiple catalogs in one process, cross-catalog use is only caught by index
    /// range, so single-catalog operation is the contract.
    /// </summary>
    public readonly struct ItemId : IEquatable<ItemId>
    {
        // Stores index+1 so that 0 naturally means None.
        private readonly int _indexPlusOne;

        internal ItemId(int index)
        {
            _indexPlusOne = index + 1;
        }

        /// <summary>Invalid identifier — same as <c>default(ItemId)</c>.</summary>
        public static readonly ItemId None = default;

        /// <summary>Whether this is the invalid identifier.</summary>
        public bool IsNone => _indexPlusOne == 0;

        /// <summary>Catalog-internal index. -1 for None.</summary>
        internal int Index => _indexPlusOne - 1;

        /// <summary>Value equality.</summary>
        public bool Equals(ItemId other) => _indexPlusOne == other._indexPlusOne;

        /// <summary>Value equality.</summary>
        public override bool Equals(object? obj) => obj is ItemId other && Equals(other);

        /// <summary>Hash code.</summary>
        public override int GetHashCode() => _indexPlusOne;

        /// <summary>Equality operator.</summary>
        public static bool operator ==(ItemId a, ItemId b) => a.Equals(b);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(ItemId a, ItemId b) => !a.Equals(b);

        /// <summary>Debug representation — low-frequency paths only (allocates a string).</summary>
        public override string ToString() => IsNone ? "ItemId.None" : $"ItemId({Index})";
    }
}
