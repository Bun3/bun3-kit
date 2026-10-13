// ItemInventory partial — transaction participation (snapshot, rollback) and notification buffering.
// Containers (_instances, _regenBasis) are snapshotted wholesale at first touch; instance fields,
// shared mutable objects present in both copies, keep a per-instance undo log.
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
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

        private sealed class RollbackStorage
        {
            internal readonly List<InstanceSnapshot> Snapshots = new List<InstanceSnapshot>();
            internal readonly Dictionary<long, ItemInstance<TState>> Instances =
                new Dictionary<long, ItemInstance<TState>>();
            internal readonly Dictionary<ItemId, long> RegenBasis = new Dictionary<ItemId, long>();

            internal void Clear()
            {
                Snapshots.Clear();
                Instances.Clear();
                RegenBasis.Clear();
            }
        }

        private RollbackStorage? _txRollback;

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

            var rollback = _txRollback ??= new RollbackStorage();
            _transaction!.Enlist(this);
            _txEnlisted = true;
            _txSnapshotToken++;
            rollback.Clear();
            _txRemovedMark = _removed.Count;
            _txHasChangesMark = _hasChanges;
            _txDirtyPending = false;

            foreach (var entry in _instances)
            {
                rollback.Instances.Add(entry.Key, entry.Value);
            }

            foreach (var entry in _regenBasis)
            {
                rollback.RegenBasis.Add(entry.Key, entry.Value);
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
            instance.StateTransactionOwner = this;
            var rollback = _txRollback!;
            instance.TxSnapshotIndex = rollback.Snapshots.Count;
            rollback.Snapshots.Add(new InstanceSnapshot(
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

            var snapshots = _txRollback!.Snapshots;
            var snapshot = snapshots[instance.TxSnapshotIndex];
            if (!snapshot.StateSnapshotted)
            {
                snapshots[instance.TxSnapshotIndex] = snapshot.WithState(_snapshotState(snapshot.State));
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
            _stateJournal?.Commit();
            foreach (var snapshot in _txRollback!.Snapshots)
            {
                snapshot.Instance.FinalizeStateTracking();
            }
            _txEnlisted = false;
            _committedNotification = FreezeNotification(_txDirtyPending);
            _txDirtyPending = false;
            ClearSnapshots();
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnCommitted()
        {
            var notification = _committedNotification;
            _committedNotification = default;
            DeliverNotification(notification);
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnRollback()
        {
            List<Exception>? errors = null;
            try
            {
                _stateJournal?.Rollback();
            }
            catch (Exception error)
            {
                // A failed journal rejects completion until a new record; framework-only
                // transactions must not inherit that failure from a previous transaction.
                _stateJournal = null;
                (errors ??= new List<Exception>()).Add(error);
            }
            _txEnlisted = false;
            _txDirtyPending = false;
            _appliedCount = 0;

            var rollback = _txRollback!;
            try
            {
                RestoreContainers(rollback);
            }
            catch (Exception error)
            {
                (errors ??= new List<Exception>()).Add(error);
            }

            try
            {
                for (var i = rollback.Snapshots.Count - 1; i >= 0; i--)
                {
                    var snapshot = rollback.Snapshots[i];
                    var instance = snapshot.Instance;
                    instance.RestoreForRollback(
                        snapshot.Quantity, snapshot.Flags, snapshot.ExpiresAt,
                        snapshot.IsNew, snapshot.Changed, snapshot.State);

                    // The baseline determines ownership even if rebuilding a container failed.
                    instance._owner = rollback.Instances.ContainsKey(instance.InstanceId) ? this : null;
                    try
                    {
                        instance.FinalizeStateTracking();
                    }
                    catch (Exception error)
                    {
                        (errors ??= new List<Exception>()).Add(error);
                    }
                }

                if (_removed.Count > _txRemovedMark)
                {
                    _removed.RemoveRange(_txRemovedMark, _removed.Count - _txRemovedMark);
                }
            }
            catch (Exception error)
            {
                (errors ??= new List<Exception>()).Add(error);
            }
            finally
            {
                _hasChanges = _txHasChangesMark;
                ClearSnapshots();
            }

            if (errors == null) return;
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Inventory rollback encountered multiple failures.", errors);
        }

        private void RestoreContainers(RollbackStorage rollback)
        {
            _instances.Clear();
            foreach (var entry in rollback.Instances)
            {
                _instances.Add(entry.Key, entry.Value);
            }

            // Reuse definition lists; their order is unspecified after rollback.
            foreach (var entry in _instancesByItem)
            {
                entry.Value.Clear();
            }

            foreach (var entry in _instances)
            {
                Index(entry.Value);
            }

            _regenBasis.Clear();
            foreach (var entry in rollback.RegenBasis)
            {
                _regenBasis.Add(entry.Key, entry.Value);
            }
        }

        /// <summary>Clears retained instance and state references after commit or rollback,
        /// while keeping the buffer capacity for the next transaction.</summary>
        private void ClearSnapshots()
        {
            _txRollback?.Clear();
        }
    }
}
