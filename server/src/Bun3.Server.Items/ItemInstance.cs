using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// An item instance held by an inventory. Unstackable definitions hold amount 1 per
    /// instance; stackable definitions (including currencies) merge amounts into a singleton
    /// instance per definition. Amounts are <see cref="BigNum"/> — long converts implicitly,
    /// and BigNum addition is lossy, absorbing terms outside the significant digits
    /// (idle-game amount semantics; full consumption is exactly Zero).
    /// <see cref="State"/> is game-owned opaque state (level, exp, options, etc.) — assign it as a
    /// value, or use <see cref="BeginStateChange"/> for a mutable state type; <see cref="MarkChanged"/>
    /// covers manual in-place mutation outside a transaction.
    /// </summary>
    /// <typeparam name="TState">Game-defined instance state type.</typeparam>
    public sealed class ItemInstance<TState>
    {
        private uint _flags;
        private long _expiresAtTicksUtc;
        private TState _state;

        internal ItemInstance(
            ItemInventory<TState> owner,
            long instanceId,
            ItemId item,
            BigNum quantity,
            uint flags,
            long expiresAtTicksUtc,
            TState state)
        {
            _owner = owner;
            InstanceId = instanceId;
            Item = item;
            Quantity = quantity;
            _flags = flags;
            _expiresAtTicksUtc = expiresAtTicksUtc;
            _state = state;
        }

        internal ItemInventory<TState>? _owner;
        internal bool IsNew;
        internal bool Changed;
        internal int TxToken;
        internal int TxSnapshotIndex = -1;

        /// <summary>Unique instance id — from the issuer seam or a loaded external authoritative id (DB, Steam).</summary>
        public long InstanceId { get; }

        /// <summary>Definition identifier (immutable).</summary>
        public ItemId Item { get; }

        /// <summary>Held amount. Always 1 for unstackables. Mutated only by the framework.</summary>
        public BigNum Quantity { get; internal set; }

        /// <summary>State bit flags — semantics belong to the game/platform (e.g. in use, locked,
        /// untradable). Instances matching the inventory's removeBlockingFlags mask cannot be
        /// consumed. The setter feeds change tracking automatically.</summary>
        public uint Flags
        {
            get => _flags;
            set
            {
                if (_flags != value)
                {
                    _owner?.CaptureForRollback(this);
                    _flags = value;
                    MarkChangedInternal();
                }
            }
        }

        /// <summary>Expiry time (UTC ticks). 0 = no expiry. Extension rules (accumulate/renew/cap)
        /// are defined by how the game updates this value. The framework has no clock, so expiry
        /// detection and handling happen via <see cref="ItemInventory{TState}.CollectExpired"/> with
        /// game-injected current time (expiry does not auto-delete). The setter feeds change
        /// tracking automatically.</summary>
        public long ExpiresAtTicksUtc
        {
            get => _expiresAtTicksUtc;
            set
            {
                if (_expiresAtTicksUtc != value)
                {
                    _owner?.CaptureForRollback(this);
                    _expiresAtTicksUtc = value;
                    MarkChangedInternal();
                }
            }
        }

        /// <summary>Game-owned state — the framework does not interpret it. Assign a replacement
        /// rather than mutating in place: the previous value is what a transaction restores on
        /// rollback. For mutable state types use <see cref="BeginStateChange"/> instead.
        /// The setter feeds change tracking automatically.</summary>
        public TState State
        {
            get => _state;
            set
            {
                _owner?.CaptureForRollback(this);
                _state = value;
                MarkChangedInternal();
            }
        }

        /// <summary>Opens a mutation scope for a mutable state type. Inside a transaction the
        /// framework deep-copies the state through the inventory's snapshotState delegate here, so
        /// the change is reversible.</summary>
        /// <exception cref="InvalidOperationException">Inside a transaction when the inventory was
        /// created without a snapshotState delegate.</exception>
        public ItemStateScope<TState> BeginStateChange()
        {
            _owner?.CaptureStateForRollback(this);
            return new ItemStateScope<TState>(this);
        }

        /// <summary>Call after mutating <see cref="State"/> in place outside a transaction — feeds
        /// change tracking (Updated) and the inventory's onDirty. Ignored after removal from the
        /// inventory.</summary>
        /// <exception cref="InvalidOperationException">Inside an active transaction — in-place
        /// mutation is not rollback-safe.</exception>
        public void MarkChanged()
        {
            _owner?.ThrowIfTransactionActive();
            MarkChangedInternal();
        }

        /// <summary>Marks the instance changed without the transaction guard — internal callers
        /// (property setters, <see cref="ItemStateScope{TState}"/>) already know the mutation is
        /// rollback-safe.</summary>
        internal void MarkChangedInternal()
        {
            Changed = true;
            _owner?.OnInstanceChanged();
        }

        /// <summary>Restores framework-tracked fields from a transaction snapshot on rollback.</summary>
        internal void RestoreForRollback(BigNum quantity, uint flags, long expiresAt, bool isNew, bool changed, TState state)
        {
            Quantity = quantity;
            _flags = flags;
            _expiresAtTicksUtc = expiresAt;
            IsNew = isNew;
            Changed = changed;
            _state = state;
        }
    }
}
