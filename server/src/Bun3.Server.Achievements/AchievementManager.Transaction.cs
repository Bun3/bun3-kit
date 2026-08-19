// AchievementManager partial — transaction participation (snapshot, rollback) and notification buffering.
using System.Collections.Generic;
using Bun3.Server.Core;

namespace Bun3.Server.Achievements
{
    // State changes apply immediately; a per-achievement snapshot taken at first touch makes them
    // reversible. While a transaction is active, dirty and completion hooks are buffered and fire
    // on commit.
    public sealed partial class AchievementManager<TDef> : ITransactionParticipant
    {
        private readonly bool[] _txCaptured;
        private readonly List<AchievementId> _txIds = new List<AchievementId>();
        private readonly List<AchievementState> _txStates = new List<AchievementState>();
        private readonly List<AchievementId> _txCompletedIds = new List<AchievementId>();
        private readonly List<int> _txCompletedCounts = new List<int>();
        private readonly List<AchievementChange> _txChanges = new List<AchievementChange>();
        private bool _txEnlisted;
        private bool _txDirtyPending;

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
                _transaction!.Enlist(this);
                _txEnlisted = true;
            }

            if (_txCaptured[id.Index])
            {
                return;
            }

            _txCaptured[id.Index] = true;
            _txIds.Add(id);
            _txStates.Add(_states[id.Index]);
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

        /// <summary>Fires the completion hook, or buffers it until commit.</summary>
        private void NotifyCompleted(AchievementId id, int newCompletions)
        {
            if (_txEnlisted)
            {
                _txCompletedIds.Add(id);
                _txCompletedCounts.Add(newCompletions);
                return;
            }

            OnCompleted?.Invoke(id, _catalog.GetDefinition(id), newCompletions);
        }

        /// <summary>Attaches a change to the action log, or buffers it until commit.
        /// Low-frequency audit path, so boxing is acceptable.</summary>
        private void NotifyChange(AchievementId id, AchievementChangeKind kind)
        {
            if (_log == null)
            {
                return;
            }

            ref readonly var state = ref _states[id.Index];
            var change = new AchievementChange(
                id, kind, state.CompletedCount, state.ClaimedCount, state.Availability);
            if (_txEnlisted)
            {
                _txChanges.Add(change);
                return;
            }

            _log.Append(change, _logLabel);
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnCommit()
        {
            _txEnlisted = false;
            ClearSnapshots();

            for (var i = 0; i < _txChanges.Count; i++)
            {
                _log!.Append(_txChanges[i], _logLabel);
            }

            _txChanges.Clear();

            if (_txDirtyPending)
            {
                _txDirtyPending = false;
                _onDirty?.Invoke();
            }

            for (var i = 0; i < _txCompletedIds.Count; i++)
            {
                var id = _txCompletedIds[i];
                OnCompleted?.Invoke(id, _catalog.GetDefinition(id), _txCompletedCounts[i]);
            }

            _txCompletedIds.Clear();
            _txCompletedCounts.Clear();
        }

        /// <inheritdoc />
        void ITransactionParticipant.OnRollback()
        {
            _txEnlisted = false;
            _txDirtyPending = false;
            _txCompletedIds.Clear();
            _txCompletedCounts.Clear();
            _txChanges.Clear();

            for (var i = _txIds.Count - 1; i >= 0; i--)
            {
                _states[_txIds[i].Index] = _txStates[i];
            }

            ClearSnapshots();
        }

        private void ClearSnapshots()
        {
            for (var i = 0; i < _txIds.Count; i++)
            {
                _txCaptured[_txIds[i].Index] = false;
            }

            _txIds.Clear();
            _txStates.Clear();
        }
    }
}
