// ItemInventory partial — transaction participation (snapshot, rollback) and notification buffering.
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;
using Bun3.Server.Core;

namespace Bun3.Server.Items
{
    // Changes apply immediately; a snapshot taken at first touch makes them reversible.
    // While a transaction is active every notification is buffered and flushed on commit.
    public sealed partial class ItemInventory<TState> : ITransactionParticipant
    {
        private readonly struct InstanceSnapshot
        {
            internal InstanceSnapshot(
                ItemInstance<TState> instance, BigNum quantity, uint flags, long expiresAt,
                bool isNew, bool changed, TState state, bool stateSnapshotted)
            {
                Instance = instance;
                Quantity = quantity;
                Flags = flags;
                ExpiresAt = expiresAt;
                IsNew = isNew;
                Changed = changed;
                State = state;
                StateSnapshotted = stateSnapshotted;
            }

            internal ItemInstance<TState> Instance { get; }
            internal BigNum Quantity { get; }
            internal uint Flags { get; }
            internal long ExpiresAt { get; }
            internal bool IsNew { get; }
            internal bool Changed { get; }
            internal TState State { get; }
            /// <summary>Whether <see cref="State"/> already holds a deep copy from the inventory's
            /// snapshotState delegate, as opposed to the shallow reference captured at first touch.</summary>
            internal bool StateSnapshotted { get; }

            /// <summary>Returns a copy with a deep-copied state, marked as snapshotted.</summary>
            internal InstanceSnapshot WithState(TState state) =>
                new InstanceSnapshot(Instance, Quantity, Flags, ExpiresAt, IsNew, Changed, state, true);
        }

        private readonly List<ItemInstance<TState>> _txCreated = new List<ItemInstance<TState>>();
        private readonly List<ItemInstance<TState>> _txRemoved = new List<ItemInstance<TState>>();
        private readonly List<InstanceSnapshot> _txSnapshots = new List<InstanceSnapshot>();
        private readonly List<ItemId> _txRegenItems = new List<ItemId>();
        private readonly List<long> _txRegenBasis = new List<long>();
        private readonly List<bool> _txRegenExisted = new List<bool>();
        /// <summary><see cref="ItemInstance{TState}.TxSnapshotIndex"/> value marking an instance
        /// created within the current transaction — <see cref="CaptureRemoved"/> skips such an
        /// instance, since <see cref="_txCreated"/> already drops it wholesale on rollback and
        /// re-adding it via <see cref="_txRemoved"/> would resurrect it.</summary>
        private const int CreatedThisTransactionMarker = -2;

        private int _txSnapshotToken;
        private int _txRemovedMark;
        private bool _txHasChangesMark;
        private bool _txEnlisted;
        private bool _txDirtyPending;

        /// <summary>Whether this inventory is currently taking part in a transaction.</summary>
        private bool InTransaction => _transaction != null && _transaction.IsActive;

        /// <summary>Enlists on the first change of a transaction and marks the snapshot baseline.</summary>
        private void EnsureEnlisted()
        {
            if (_txEnlisted || !InTransaction)
            {
                return;
            }

            _transaction!.Enlist(this);
            _txEnlisted = true;
            _txSnapshotToken++;
            _txCreated.Clear();
            _txRemoved.Clear();
            _txSnapshots.Clear();
            _txRegenItems.Clear();
            _txRegenBasis.Clear();
            _txRegenExisted.Clear();
            _txRemovedMark = _removed.Count;
            _txHasChangesMark = _hasChanges;
            _txDirtyPending = false;
        }

        /// <summary>Snapshots an instance before its first change in the current transaction.</summary>
        private void Capture(ItemInstance<TState> instance)
        {
            EnsureEnlisted();
            if (!_txEnlisted || instance.TxToken == _txSnapshotToken)
            {
                return;
            }

            instance.TxToken = _txSnapshotToken;
            instance.TxSnapshotIndex = _txSnapshots.Count;
            _txSnapshots.Add(new InstanceSnapshot(
                instance, instance.Quantity, instance.Flags, instance.ExpiresAtTicksUtc,
                instance.IsNew, instance.Changed, instance.State, false));
        }

        /// <summary>Snapshot entry point for <see cref="ItemInstance{TState}"/> property setters.</summary>
        internal void CaptureForRollback(ItemInstance<TState> instance) => Capture(instance);

        /// <summary>Snapshot entry point for <see cref="ItemInstance{TState}.BeginStateChange"/> —
        /// promotes the shallow reference captured at first touch to a deep copy via the
        /// inventory's snapshotState delegate, so a mutable state type is reversible.</summary>
        internal void CaptureStateForRollback(ItemInstance<TState> instance)
        {
            if (!InTransaction)
            {
                return;
            }

            if (_snapshotState == null)
            {
                throw new InvalidOperationException(
                    "BeginStateChange inside a transaction requires a snapshotState delegate — pass one to the inventory, or assign State instead of mutating in place.");
            }

            Capture(instance);
            var index = instance.TxSnapshotIndex;
            if (index < 0)
            {
                return;   // Created within this transaction — rollback drops it wholesale.
            }

            // Promote only when the instance still holds the exact object the snapshot captured —
            // value-equality (an overridden Equals) must not be mistaken for identity here.
            var snapshot = _txSnapshots[index];
            if (!snapshot.StateSnapshotted
                && (typeof(TState).IsValueType || ReferenceEquals(snapshot.State, instance.State)))
            {
                _txSnapshots[index] = snapshot.WithState(_snapshotState(instance.State));
            }
        }

        /// <summary>Guard for <see cref="ItemInstance{TState}.MarkChanged"/> — in-place mutation is
        /// not rollback-safe, so it is rejected while a transaction is active.</summary>
        internal void ThrowIfTransactionActive()
        {
            if (InTransaction)
            {
                throw new InvalidOperationException(
                    "MarkChanged is not available inside a transaction — assign State or use BeginStateChange so the change can be rolled back.");
            }
        }

        /// <summary>Records a newly created instance so a rollback can drop it.</summary>
        private void CaptureCreated(ItemInstance<TState> instance)
        {
            EnsureEnlisted();
            if (!_txEnlisted)
            {
                return;
            }

            instance.TxToken = _txSnapshotToken;   // Created instances are dropped wholesale, never restored.
            instance.TxSnapshotIndex = CreatedThisTransactionMarker;
            _txCreated.Add(instance);
        }

        /// <summary>Records a removed instance so a rollback can re-index it. An instance created
        /// earlier in the same transaction is skipped — <see cref="_txCreated"/> already drops it.</summary>
        private void CaptureRemoved(ItemInstance<TState> instance)
        {
            EnsureEnlisted();
            if (!_txEnlisted)
            {
                return;
            }

            if (instance.TxToken == _txSnapshotToken && instance.TxSnapshotIndex == CreatedThisTransactionMarker)
            {
                return;
            }

            _txRemoved.Add(instance);
        }

        /// <summary>Snapshots a regen basis entry before it changes.</summary>
        private void CaptureRegenBasis(ItemId item)
        {
            EnsureEnlisted();
            if (!_txEnlisted)
            {
                return;
            }

            for (var i = 0; i < _txRegenItems.Count; i++)
            {
                if (_txRegenItems[i] == item)
                {
                    return;
                }
            }

            var existed = _regenBasis.TryGetValue(item, out var basis);
            _txRegenItems.Add(item);
            _txRegenBasis.Add(basis);
            _txRegenExisted.Add(existed);
        }

        /// <summary>Raises onDirty, or buffers it until commit while a transaction is active.</summary>
        private void NotifyDirty()
        {
            if (_txEnlisted)
            {
                _txDirtyPending = true;
                return;
            }

            _onDirty?.Invoke();
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnCommit()
        {
            _txEnlisted = false;
            if (_txDirtyPending)
            {
                _txDirtyPending = false;
                _onDirty?.Invoke();
            }

            FlushApplied();
            ClearSnapshots();
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnRollback()
        {
            _txEnlisted = false;
            _txDirtyPending = false;
            _appliedCount = 0;

            for (var i = _txCreated.Count - 1; i >= 0; i--)
            {
                var created = _txCreated[i];
                _instances.Remove(created.InstanceId);
                Unindex(created);
                created._owner = null;
                created.TxSnapshotIndex = -1;
            }

            for (var i = _txSnapshots.Count - 1; i >= 0; i--)
            {
                var snapshot = _txSnapshots[i];
                var instance = snapshot.Instance;
                instance.RestoreForRollback(
                    snapshot.Quantity, snapshot.Flags, snapshot.ExpiresAt,
                    snapshot.IsNew, snapshot.Changed, snapshot.State);
                instance.TxSnapshotIndex = -1;
            }

            for (var i = _txRemoved.Count - 1; i >= 0; i--)
            {
                var removed = _txRemoved[i];
                removed._owner = this;
                _instances[removed.InstanceId] = removed;
                Index(removed);
            }

            for (var i = 0; i < _txRegenItems.Count; i++)
            {
                if (_txRegenExisted[i])
                {
                    _regenBasis[_txRegenItems[i]] = _txRegenBasis[i];
                }
                else
                {
                    _regenBasis.Remove(_txRegenItems[i]);
                }
            }

            if (_removed.Count > _txRemovedMark)
            {
                _removed.RemoveRange(_txRemovedMark, _removed.Count - _txRemovedMark);
            }

            _hasChanges = _txHasChangesMark;
            ClearSnapshots();
        }

        /// <summary>Releases the snapshot buffers — they hold removed instances and their state,
        /// so both commit and rollback drop them rather than waiting for the next transaction.</summary>
        private void ClearSnapshots()
        {
            _txCreated.Clear();
            _txRemoved.Clear();
            _txSnapshots.Clear();
            _txRegenItems.Clear();
            _txRegenBasis.Clear();
            _txRegenExisted.Clear();
        }
    }
}
