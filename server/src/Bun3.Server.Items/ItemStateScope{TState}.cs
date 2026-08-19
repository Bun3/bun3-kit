using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Mutation scope for game-owned instance state. Entering the scope is the moment right before
    /// the change, which is where a transaction takes its rollback snapshot; disposing marks the
    /// instance changed. Use this instead of mutating <see cref="ItemInstance{TState}.State"/> in
    /// place when the state type is mutable — games treating state as a value assign
    /// <see cref="ItemInstance{TState}.State"/> directly instead.
    /// </summary>
    /// <typeparam name="TState">Game-defined instance state type.</typeparam>
    public readonly struct ItemStateScope<TState> : IDisposable
    {
        private readonly ItemInstance<TState>? _instance;

        internal ItemStateScope(ItemInstance<TState> instance)
        {
            _instance = instance;
        }

        /// <summary>The state to mutate.</summary>
        public TState State => _instance!.State;

        /// <summary>Marks the instance changed, feeding change tracking and onDirty.</summary>
        public void Dispose() => _instance?.MarkChangedInternal();
    }
}
