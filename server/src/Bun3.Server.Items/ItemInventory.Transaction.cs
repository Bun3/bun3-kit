// ItemInventory partial — transaction participation (snapshot, rollback) and notification buffering.
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
                bool isNew, bool changed, TState state)
            {
                Instance = instance;
                Quantity = quantity;
                Flags = flags;
                ExpiresAt = expiresAt;
                IsNew = isNew;
                Changed = changed;
                State = state;
            }

            internal ItemInstance<TState> Instance { get; }
            internal BigNum Quantity { get; }
            internal uint Flags { get; }
            internal long ExpiresAt { get; }
            internal bool IsNew { get; }
            internal bool Changed { get; }
            internal TState State { get; }
        }

        private readonly List<ItemInstance<TState>> _txCreated = new List<ItemInstance<TState>>();
        private readonly List<ItemInstance<TState>> _txRemoved = new List<ItemInstance<TState>>();
        private readonly List<InstanceSnapshot> _txSnapshots = new List<InstanceSnapshot>();
        private readonly List<ItemId> _txRegenItems = new List<ItemId>();
        private readonly List<long> _txRegenBasis = new List<long>();
        private readonly List<bool> _txRegenExisted = new List<bool>();
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
                instance.IsNew, instance.Changed, instance.State));
        }

        /// <summary>Snapshot entry point for <see cref="ItemInstance{TState}"/> property setters.</summary>
        internal void CaptureForRollback(ItemInstance<TState> instance) => Capture(instance);

        /// <summary>Records a newly created instance so a rollback can drop it.</summary>
        private void CaptureCreated(ItemInstance<TState> instance)
        {
            EnsureEnlisted();
            if (!_txEnlisted)
            {
                return;
            }

            instance.TxToken = _txSnapshotToken;   // Created instances are dropped wholesale, never restored.
            _txCreated.Add(instance);
        }

        /// <summary>Records a removed instance so a rollback can re-index it.</summary>
        private void CaptureRemoved(ItemInstance<TState> instance)
        {
            EnsureEnlisted();
            if (_txEnlisted)
            {
                _txRemoved.Add(instance);
            }
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
            _txCreated.Clear();
            _txRemoved.Clear();
            _txSnapshots.Clear();
            _txRegenItems.Clear();
            _txRegenBasis.Clear();
            _txRegenExisted.Clear();
        }
    }
}
