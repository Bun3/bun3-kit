using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Inventory transaction builder — mixes definition-level (<see cref="Add"/>/<see cref="Remove"/>)
    /// and instance-targeted (<see cref="RemoveInstance(long)"/>) operations, applied
    /// all-or-nothing by <see cref="Commit"/>.
    /// Reuses inventory-owned scratch buffers, so it is allocation-free; only one batch may
    /// be open at a time — a new <see cref="ItemInventory{TState}.BeginTransaction"/> discards
    /// the previous uncommitted batch, and using a stale or already-committed builder throws.
    /// </summary>
    /// <typeparam name="TState">Game-defined instance state type.</typeparam>
    public readonly struct InventoryTransaction<TState>
    {
        private readonly ItemInventory<TState> _inventory;
        private readonly int _token;

        internal InventoryTransaction(ItemInventory<TState> inventory, int token)
        {
            _inventory = inventory;
            _token = token;
        }

        /// <summary>Queues a grant. amount must be positive (non-stackables: integer, within the per-operation cap).</summary>
        public void Add(ItemId item, BigNum amount) =>
            _inventory.TxRecord(_token, new ItemInventory<TState>.TxOp(
                ItemInventory<TState>.TxOpKind.Add, item, 0, amount));

        /// <summary>Queues a definition-level consume. Instances targeted elsewhere in the batch are excluded as candidates.</summary>
        public void Remove(ItemId item, BigNum amount) =>
            _inventory.TxRecord(_token, new ItemInventory<TState>.TxOp(
                ItemInventory<TState>.TxOpKind.RemoveByItem, item, 0, amount));

        /// <summary>Queues a full-instance consume — destroys the instance for non-stackables,
        /// drains the full amount for stack singletons.</summary>
        public void RemoveInstance(long instanceId) =>
            _inventory.TxRecord(_token, new ItemInventory<TState>.TxOp(
                ItemInventory<TState>.TxOpKind.RemoveInstanceAll, ItemId.None, instanceId, BigNum.Zero));

        /// <summary>Queues an instance consume with an explicit amount — for partial stack-singleton
        /// consumption; non-stackables only allow 1.</summary>
        public void RemoveInstance(long instanceId, BigNum amount) =>
            _inventory.TxRecord(_token, new ItemInventory<TState>.TxOp(
                ItemInventory<TState>.TxOpKind.RemoveInstanceAmount, ItemId.None, instanceId, amount));

        /// <summary>Applies the batch all-or-nothing. On failure the inventory is fully unchanged
        /// and <paramref name="failedIndex"/> points at the offending operation. Created instances
        /// are collected into <paramref name="created"/>. The builder is invalid after commit.</summary>
        public InventoryError Commit(out int failedIndex, List<ItemInstance<TState>>? created = null) =>
            _inventory.TxCommit(_token, out failedIndex, created);
    }
}
