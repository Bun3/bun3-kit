// ItemInventory partial — transaction participation (snapshot, rollback) and notification buffering.
// Containers (_instances, _regenBasis) are snapshotted wholesale at first touch; instance fields,
// shared mutable objects present in both copies, keep a per-instance undo log.
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

        private readonly List<InstanceSnapshot> _txSnapshots = new List<InstanceSnapshot>();
        private readonly Dictionary<long, ItemInstance<TState>> _txInstances =
            new Dictionary<long, ItemInstance<TState>>();
        private readonly Dictionary<ItemId, long> _txRegenBasis = new Dictionary<ItemId, long>();

        private int _txSnapshotToken;
        private int _txRemovedMark;
        private bool _txHasChangesMark;
        private bool _txEnlisted;
        private bool _txDirtyPending;

        /// <summary>Whether this inventory is currently taking part in a transaction.</summary>
        private bool InTransaction => _transaction != null && _transaction.IsActive;

        /// <summary>Enlists on the first change of a transaction and takes the container snapshot.
        /// Every path must call it before its first mutation of the instance or regen-basis map.</summary>
        private void EnsureEnlisted()
        {
            if (_txEnlisted || !InTransaction)
            {
                return;
            }

            _transaction!.Enlist(this);
            _txEnlisted = true;
            _txSnapshotToken++;
            _txSnapshots.Clear();
            _txRemovedMark = _removed.Count;
            _txHasChangesMark = _hasChanges;
            _txDirtyPending = false;

            _txInstances.Clear();
            foreach (var entry in _instances)
            {
                _txInstances.Add(entry.Key, entry.Value);
            }

            _txRegenBasis.Clear();
            foreach (var entry in _regenBasis)
            {
                _txRegenBasis.Add(entry.Key, entry.Value);
            }
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

            // Promote only when the instance still holds the exact object the snapshot captured —
            // value-equality (an overridden Equals) must not be mistaken for identity here.
            var snapshot = _txSnapshots[instance.TxSnapshotIndex];
            if (!snapshot.StateSnapshotted
                && (typeof(TState).IsValueType || ReferenceEquals(snapshot.State, instance.State)))
            {
                _txSnapshots[instance.TxSnapshotIndex] = snapshot.WithState(_snapshotState(instance.State));
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

            _instances.Clear();
            foreach (var entry in _txInstances)
            {
                _instances.Add(entry.Key, entry.Value);
            }

            // Rebuilt from the restored membership. The lists are reused rather than dropped —
            // an empty list means "not held" — and order within a definition is unspecified.
            foreach (var entry in _instancesByItem)
            {
                entry.Value.Clear();
            }

            foreach (var entry in _instances)
            {
                Index(entry.Value);
            }

            _regenBasis.Clear();
            foreach (var entry in _txRegenBasis)
            {
                _regenBasis.Add(entry.Key, entry.Value);
            }

            for (var i = _txSnapshots.Count - 1; i >= 0; i--)
            {
                var snapshot = _txSnapshots[i];
                var instance = snapshot.Instance;
                instance.RestoreForRollback(
                    snapshot.Quantity, snapshot.Flags, snapshot.ExpiresAt,
                    snapshot.IsNew, snapshot.Changed, snapshot.State);
                instance.TxSnapshotIndex = -1;

                // Absent from the restored membership means the transaction created it — the
                // phantom is detached, so a stray MarkChanged on it is ignored.
                instance._owner = _instances.ContainsKey(instance.InstanceId) ? this : null;
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
            _txSnapshots.Clear();
            _txInstances.Clear();
            _txRegenBasis.Clear();
        }
    }
}
