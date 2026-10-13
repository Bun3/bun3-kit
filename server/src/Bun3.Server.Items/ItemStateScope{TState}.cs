using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Mutation scope for game-owned instance state. Entering the scope is the moment right before
    /// the change, which is where a transaction copies the saved pre-transaction state;
    /// disposing marks the instance changed. The inventory's snapshotState delegate must return
    /// an independent copy of all mutable data owned by that state and must not mutate its input
    /// or reenter the inventory. Close this scope before the transaction scope. Rollback restores
    /// state values, so callers must re-read <see cref="ItemInstance{TState}.State"/> afterward.
    /// </summary>
    /// <typeparam name="TState">Game-defined instance state type.</typeparam>
    public readonly struct ItemStateScope<TState> : IDisposable
    {
        private readonly ItemInstance<TState>? _instance;

        internal ItemStateScope(ItemInstance<TState> instance)
        {
            _instance = instance;
        }

        /// <summary>The current state to mutate while the scope is open.</summary>
        public TState State => _instance!.State;

        /// <summary>Marks the instance changed, feeding change tracking and onDirty.</summary>
        public void Dispose() => _instance?.MarkChangedInternal();
    }
}
