using System;
using System.Collections.Generic;

namespace Bun3.Server.Core
{
    /// <summary>
    /// A module that can take part in a transaction. Participants apply their changes immediately
    /// and keep a snapshot taken at first touch, so a rollback is a restore rather than a deferred
    /// apply — later steps of a chain can read the results of earlier ones.
    /// </summary>
    public interface ITransactionParticipant
    {
        /// <summary>Discards the snapshot and flushes any notifications buffered during the transaction.</summary>
        void OnCommit();

        /// <summary>Restores the snapshot, discards it, and drops any buffered notifications.</summary>
        void OnRollback();
    }

    /// <summary>Transaction handle — close with using. Without a <see cref="Commit"/> call the
    /// transaction rolls back on dispose.</summary>
    public readonly struct TransactionScope : IDisposable
    {
        private readonly Transaction? _transaction;
        private readonly int _token;

        internal TransactionScope(Transaction transaction, int token)
        {
            _transaction = transaction;
            _token = token;
        }

        /// <summary>Commits — participants are notified in enlist order.</summary>
        public void Commit() => _transaction?.Commit(_token);

        /// <summary>Rolls back unless <see cref="Commit"/> was called.</summary>
        public void Dispose() => _transaction?.Rollback(_token);
    }

    /// <summary>
    /// Multi-operation transaction, one per session. Modules hold it as a constructor member and
    /// enlist themselves the first time they change while it is active; changes apply immediately
    /// and a rollback restores the snapshot each participant took at that first touch.
    /// Notifications a participant would raise are buffered for the duration and fire on commit,
    /// so a rolled-back change never reaches an audit ledger or a hook.
    /// Single level — a nested <see cref="Begin"/> throws. Use only inside a single session actor
    /// (no locks). When no transaction is active, participants skip snapshotting entirely, so a
    /// game that never rolls back pays nothing.
    /// </summary>
    public sealed class Transaction
    {
        private readonly List<ITransactionParticipant> _participants = new List<ITransactionParticipant>();
        private int _token;
        private bool _active;
        private bool _notifying;

        /// <summary>Whether a transaction is currently open. False while participants are being
        /// notified, so commit hooks may mutate freely.</summary>
        public bool IsActive => _active;

        /// <summary>Opens the transaction. Not available from a commit or rollback hook — the
        /// participant list is being walked at that point.</summary>
        /// <exception cref="InvalidOperationException">When a transaction is already open, or while
        /// participants are being notified.</exception>
        public TransactionScope Begin()
        {
            if (_notifying)
            {
                throw new InvalidOperationException(
                    "Cannot begin a transaction while participants are being notified — commit and rollback hooks must not open a transaction.");
            }

            if (_active)
            {
                throw new InvalidOperationException("Nested transactions are not supported.");
            }

            _active = true;
            _participants.Clear();
            _token++;
            return new TransactionScope(this, _token);
        }

        /// <summary>Registers a participant. Idempotent within one transaction — a module calls
        /// this on its first change.</summary>
        /// <exception cref="InvalidOperationException">When no transaction is open.</exception>
        public void Enlist(ITransactionParticipant participant)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            if (!_active)
            {
                throw new InvalidOperationException("Enlist requires an active transaction.");
            }

            for (var i = 0; i < _participants.Count; i++)
            {
                if (ReferenceEquals(_participants[i], participant))
                {
                    return;
                }
            }

            _participants.Add(participant);
        }

        internal void Commit(int token)
        {
            if (token != _token || !_active)
            {
                return;
            }

            _active = false;
            _notifying = true;
            try
            {
                for (var i = 0; i < _participants.Count; i++)
                {
                    _participants[i].OnCommit();
                }
            }
            finally
            {
                _notifying = false;
                _participants.Clear();
            }
        }

        internal void Rollback(int token)
        {
            if (token != _token || !_active)
            {
                return;
            }

            _active = false;
            _notifying = true;
            try
            {
                // Reverse enlist order.
                for (var i = _participants.Count - 1; i >= 0; i--)
                {
                    _participants[i].OnRollback();
                }
            }
            finally
            {
                _notifying = false;
                _participants.Clear();
            }
        }
    }
}
