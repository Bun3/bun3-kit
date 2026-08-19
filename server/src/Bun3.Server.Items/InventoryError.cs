namespace Bun3.Server.Items
{
    /// <summary>Failure reason for an inventory operation. <see cref="None"/> means success.</summary>
    public enum InventoryError
    {
        /// <summary>Success.</summary>
        None = 0,

        /// <summary>Item not in the catalog (including <see cref="ItemId.None"/>).</summary>
        UnknownItem,

        /// <summary>Disallowed amount — non-positive (single op), zero (delta), non-integer for
        /// non-stackables, or exceeding the per-operation cap
        /// (<see cref="ItemInventory{TState}.MaxInstancesPerOperation"/>).</summary>
        InvalidAmount,

        /// <summary>Insufficient available (unlocked) amount. Consuming the same instance twice
        /// in one batch also resolves here.</summary>
        Insufficient,

        /// <summary>Per-definition holding cap (maxCount) exceeded, or amount arithmetic overflow.</summary>
        ExceedsMaxCount,

        /// <summary>Instance id not present in the inventory.</summary>
        UnknownInstance,

        /// <summary>Instance id already exists, or duplicate instance for a stackable definition (load).</summary>
        DuplicateInstance,

        /// <summary>Direct consume attempt on an instance blocked by lock flags (removeBlockingFlags).</summary>
        Locked,
    }
}
