// AchievementManager partial — transaction participation (snapshot, rollback) and notification buffering.
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Bun3.Server.Core;

namespace Bun3.Server.Achievements
{
    // State changes apply immediately; a per-achievement snapshot taken at first touch makes them
    // reversible. While a transaction is active, dirty and completion hooks are buffered and fire
    // on commit.
    public sealed partial class AchievementManager<TDef> : ITransactionParticipant
    {
        private sealed class RollbackStorage
        {
            internal readonly bool[] Captured;
            internal readonly List<AchievementId> Ids = new List<AchievementId>();
            internal readonly List<AchievementState> States = new List<AchievementState>();
            internal readonly List<AchievementId> CompletedIds = new List<AchievementId>();
            internal readonly List<int> CompletedCounts = new List<int>();
            internal readonly List<AchievementChange> Changes = new List<AchievementChange>();

            internal RollbackStorage(int count) => Captured = new bool[count];

            internal void ClearSnapshots()
            {
                for (var i = 0; i < Ids.Count; i++) Captured[Ids[i].Index] = false;
                Ids.Clear();
                States.Clear();
            }

            internal void ClearNotifications()
            {
                CompletedIds.Clear();
                CompletedCounts.Clear();
                Changes.Clear();
            }
        }

        private RollbackStorage? _txRollback;
        private bool _txEnlisted;
        private bool _txDirtyPending;
        private bool _committedDirty;

        private bool InTransaction => _transaction != null && _transaction.IsActive;

        /// <summary>Snapshots an achievement before its first change in the current transaction.</summary>
        private void Capture(AchievementId id)
        {
            if (!InTransaction)
            {
                return;
            }

            if (!_txEnlisted)
            {
                _txRollback ??= new RollbackStorage(_states.Length);
                _transaction!.Enlist(this);
                _txEnlisted = true;
            }

            var rollback = _txRollback!;
            if (rollback.Captured[id.Index])
            {
                return;
            }

            rollback.Captured[id.Index] = true;
            rollback.Ids.Add(id);
            rollback.States.Add(_states[id.Index]);
        }

        /// <summary>Guard for <see cref="AchievementManager{TDef}.Restore"/> — the load path writes
        /// without snapshotting, so it is rejected while a transaction is active.</summary>
        private void ThrowIfTransactionActive()
        {
            if (InTransaction)
            {
                throw new System.InvalidOperationException(
                    "Restore is not available inside a transaction — load before the transaction begins.");
            }
        }

        private void NotifyMutation(AchievementId id, AchievementChangeKind? kind, bool dirty, int completions = 0)
        {
            var state = _states[id.Index];
            var change = kind.HasValue
                ? new AchievementChange(id, kind.Value, state.CompletedCount, state.ClaimedCount, state.Availability)
                : default;
            if (_txEnlisted)
            {
                var rollback = _txRollback!;
                if (kind.HasValue && _log != null) rollback.Changes.Add(change);
                _txDirtyPending |= dirty;
                if (completions > 0)
                {
                    rollback.CompletedIds.Add(id);
                    rollback.CompletedCounts.Add(completions);
                }
                return;
            }

            List<Exception>? errors = null;
            if (kind.HasValue) DeliverChange(change, ref errors);
            if (dirty) DeliverDirty(ref errors);
            if (completions > 0) DeliverCompleted(id, completions, ref errors);
            ThrowNotificationErrors(errors);
        }

        private void DeliverChange(AchievementChange change, ref List<Exception>? errors)
        {
            try { _log?.Append(change, _logLabel); }
            catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
        }

        private void DeliverDirty(ref List<Exception>? errors)
        {
            try { _onDirty?.Invoke(); }
            catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
        }

        private void DeliverCompleted(AchievementId id, int count, ref List<Exception>? errors)
        {
            try { OnCompleted?.Invoke(id, _catalog.GetDefinition(id), count); }
            catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
        }

        private static void ThrowNotificationErrors(List<Exception>? errors)
        {
            if (errors == null) return;
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException(errors);
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnCommit()
        {
            _txEnlisted = false;
            _committedDirty = _txDirtyPending;
            _txDirtyPending = false;
            _txRollback?.ClearSnapshots();
            // Begin is forbidden during notification; ordinary reactions do not write these lists.
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnCommitted()
        {
            var dirty = _committedDirty;
            _committedDirty = false;
            var rollback = _txRollback!;
            List<Exception>? errors = null;
            try
            {
                for (var i = 0; i < rollback.Changes.Count; i++)
                {
                    DeliverChange(rollback.Changes[i], ref errors);
                }

                if (dirty) DeliverDirty(ref errors);
                for (var i = 0; i < rollback.CompletedIds.Count; i++)
                {
                    DeliverCompleted(rollback.CompletedIds[i], rollback.CompletedCounts[i], ref errors);
                }
            }
            finally
            {
                rollback.ClearNotifications();
            }

            ThrowNotificationErrors(errors);
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnRollback()
        {
            _txEnlisted = false;
            _txDirtyPending = false;
            var rollback = _txRollback!;
            rollback.ClearNotifications();

            for (var i = rollback.Ids.Count - 1; i >= 0; i--)
            {
                _states[rollback.Ids[i].Index] = rollback.States[i];
            }

            rollback.ClearSnapshots();
        }
    }
}
