// ItemInstance partial — item fields, state access, and change scopes.
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
    /// Generated state tracks nested edits automatically and preserves references on rollback.
    /// Otherwise <see cref="State"/> is game-owned opaque state (level, exp, options, etc.) — assign it as a
    /// value, or use <see cref="BeginStateChange"/> for mutable edits; <see cref="MarkChanged"/>
    /// covers manual in-place mutation outside a transaction. Rollback preserves this instance,
    /// but an opaque mutable state edit restores an independent state value rather than the old object.
    /// File layout: this file (fields and scopes) / StateTracking (generated state ownership).
    /// </summary>
    /// <typeparam name="TState">Game-defined instance state type.</typeparam>
    public sealed partial class ItemInstance<TState>
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

        /// <summary>Game-owned state. Generated state supports automatic nested tracking and
        /// reference-preserving rollback; assigning its current root is a no-op. For opaque state,
        /// assign a replacement
        /// rather than mutating in place: the previous value is what a setter-only transaction
        /// restores on rollback. If a later change scope edits mutable data, rollback restores an
        /// independent copy of the original value. Re-read this property after rollback instead
        /// of retaining a reference to a previous state object. For mutable edits use
        /// <see cref="BeginStateChange"/>.
        /// The setter feeds change tracking automatically. An instance that has used a mutable
        /// change scope cannot subsequently accept generated state; load a new instance instead.</summary>
        public TState State
        {
            get => _state;
            set => SetState(value);
        }

        /// <summary>Opens a mutation scope for a mutable state type. Inside a transaction the
        /// framework copies the saved pre-transaction state through the inventory's snapshotState
        /// delegate on the first scope for this instance. Close this scope before its transaction
        /// scope. Edits through external references and mutable data shared between different item
        /// instances are outside the rollback contract. Generated state and change scopes cannot
        /// be mixed during the lifetime of one instance, including after null or opaque replacement.</summary>
        /// <exception cref="InvalidOperationException">Inside a transaction when the inventory was
        /// created without a snapshotState delegate, or when this instance has used generated state.</exception>
        public ItemStateScope<TState> BeginStateChange()
        {
            ThrowIfGeneratedStateTracking();
            _owner?.CaptureStateForRollback(this);
            _stateScopeUsed = true;
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
