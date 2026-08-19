namespace Bun3.Server.Items
{
    /// <summary>Kind of change an instance underwent since the last drain.</summary>
    public enum ItemChangeKind
    {
        /// <summary>Newly created — DB INSERT target.</summary>
        Created,

        /// <summary>Quantity, flags, or state changed — DB UPDATE target.</summary>
        Updated,

        /// <summary>Removed — DB DELETE target. <see cref="ItemChange{TState}.Instance"/> is null.</summary>
        Removed,
    }

    /// <summary>One change emitted by <see cref="ItemInventory{TState}.DrainChanges"/>.
    /// Instances created and removed before the drain cancel out and are not emitted
    /// (no DB round trip needed).</summary>
    /// <typeparam name="TState">Game-defined instance state type.</typeparam>
    public readonly struct ItemChange<TState>
    {
        internal ItemChange(ItemChangeKind kind, long instanceId, ItemInstance<TState>? instance)
        {
            Kind = kind;
            InstanceId = instanceId;
            Instance = instance;
        }

        /// <summary>Change kind.</summary>
        public ItemChangeKind Kind { get; }

        /// <summary>Target instance id.</summary>
        public long InstanceId { get; }

        /// <summary>Target instance — null for <see cref="ItemChangeKind.Removed"/>.</summary>
        public ItemInstance<TState>? Instance { get; }
    }
}
