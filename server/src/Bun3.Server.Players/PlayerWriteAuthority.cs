using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Players
{
    /// <summary>Coordinates durable write ownership without a per-player version column.</summary>
    public sealed class PlayerWriteAuthority
    {
        private readonly PlayerWriteAuthorityStore _store;
        private readonly ILogger _logger;
        /// <summary>Creates a coordinator for an already initialized control row.</summary>
        public PlayerWriteAuthority(string scopeKey, PlayerWriteAuthorityStore store, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(scopeKey)) throw new ArgumentException("A scope key is required.", nameof(scopeKey));
            ScopeKey = scopeKey; _store = store ?? throw new ArgumentNullException(nameof(store)); _logger = logger ?? NullLogger.Instance;
        }
        /// <summary>Gets the ownership scope key.</summary>
        public string ScopeKey { get; }
        /// <summary>Reads the durable control row. This observation alone grants no execution rights.</summary>
        public async ValueTask<PlayerWriteAuthorityRecord> ReadAsync(CancellationToken ct = default)
        {
            DbConnection? db = null; DbTransaction? tx = null;
            try
            {
                db = _store.Create(); await db.OpenAsync(ct).ConfigureAwait(false);
                tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
                return await ReadLockedAsync(db, tx, PlayerWriteLock.Shared, ct).ConfigureAwait(false);
            }
            finally { await CleanupAsync(db, tx).ConfigureAwait(false); }
        }
        /// <summary>Recovers only the confirmed old instance under an exclusive database barrier.</summary>
        /// <remarks>The caller must authenticate the operator and independently confirm process termination and restart prevention.</remarks>
        public ValueTask<PlayerWriteTransitionResult> RecoverAsync(PlayerWriteRecoveryConfirmation confirmation, CancellationToken ct = default)
        {
            if (confirmation == null) throw new ArgumentNullException(nameof(confirmation));
            return TransitionCoreAsync(new PlayerWriteTransition(confirmation.Id, PlayerWriteState.RecoveryRequired, confirmation.ExpectedInstanceId,
                PlayerWriteState.Offline, null, confirmation.OperatorKey, confirmation.Reason), true, ct);
        }
        internal ValueTask<PlayerWriteTransitionResult> TransitionAsync(PlayerWriteTransition transition, CancellationToken ct = default)
            => TransitionCoreAsync(transition, false, ct);
        private async ValueTask<PlayerWriteTransitionResult> TransitionCoreAsync(PlayerWriteTransition transition, bool recovery, CancellationToken ct)
        {
            if (transition == null) throw new ArgumentNullException(nameof(transition));
            DbConnection? db = null; DbTransaction? tx = null;
            PlayerWriteAuthorityRecord? record = null; var commitAttempted = false;
            try
            {
                db = _store.Create(); await db.OpenAsync(ct).ConfigureAwait(false);
                tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
                record = await ReadLockedAsync(db, tx, PlayerWriteLock.Exclusive, ct).ConfigureAwait(false);
                var fingerprint = transition.Fingerprint(ScopeKey);
                if (record.TransitionId == transition.Id)
                    return Result(record.TransitionFingerprint.Span.SequenceEqual(fingerprint)
                        ? PlayerWriteTransitionOutcome.Committed : PlayerWriteTransitionOutcome.Conflict, record, transition);
                if (record.InstanceId != transition.ExpectedInstanceId ||
                    (recovery ? record.State == PlayerWriteState.Offline : record.State != transition.ExpectedState || !IsAllowed(transition)))
                    return Result(PlayerWriteTransitionOutcome.Conflict, record, transition);
                if (recovery && record.State != PlayerWriteState.RecoveryRequired)
                {
                    var entering = new PlayerWriteTransition(Guid.NewGuid(), record.State, record.InstanceId, PlayerWriteState.RecoveryRequired,
                        record.InstanceId, transition.OperatorKey, transition.Reason);
                    var recovering = new PlayerWriteAuthorityRecord(PlayerWriteState.RecoveryRequired, record.InstanceId, entering.Id, entering.Fingerprint(ScopeKey));
                    await _store.Write(db, tx, ScopeKey, recovering, entering, ct).ConfigureAwait(false);
                }
                if (transition.ExpectedState == PlayerWriteState.RecoveryRequired && transition.NextState == PlayerWriteState.Offline)
                    await _store.VerifyRecovery(db, tx, ScopeKey, ct).ConfigureAwait(false);
                record = new PlayerWriteAuthorityRecord(transition.NextState, transition.NextInstanceId, transition.Id, fingerprint);
                await _store.Write(db, tx, ScopeKey, record, transition, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested(); commitAttempted = true;
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return Result(PlayerWriteTransitionOutcome.Committed, record, transition);
            }
            catch (Exception ex)
            {
                if (tx != null)
                {
                    try { await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception rollback) { LogCleanup(rollback); }
                }
                if (commitAttempted) return Result(PlayerWriteTransitionOutcome.Unknown, record!, transition, ex);
                throw;
            }
            finally { await CleanupAsync(db, tx).ConfigureAwait(false); }
        }
        internal async ValueTask ProtectAsync(DbConnection db, DbTransaction tx, PlayerWriteBinding binding, PlayerStoreAccess access, CancellationToken ct)
        {
            binding.Validate();
            if (access < PlayerStoreAccess.Load || access > PlayerStoreAccess.Recovery) throw new ArgumentOutOfRangeException(nameof(access));
            var record = await ReadLockedAsync(db, tx, PlayerWriteLock.Shared, ct).ConfigureAwait(false);
            var allowed = binding.Role == PlayerWriteRole.OfflineAdministrator
                ? record.State == PlayerWriteState.Offline && (access == PlayerStoreAccess.Operation || access == PlayerStoreAccess.Recovery)
                : record.InstanceId == binding.InstanceId &&
                  (record.State == PlayerWriteState.Online ||
                   record.State == PlayerWriteState.Starting && access == PlayerStoreAccess.Load ||
                   record.State == PlayerWriteState.Draining && access != PlayerStoreAccess.Load);
            if (!allowed) throw new PlayerWriteAuthorityException(ScopeKey, record);
        }
        private async ValueTask<PlayerWriteAuthorityRecord> ReadLockedAsync(DbConnection db, DbTransaction tx, PlayerWriteLock mode, CancellationToken ct)
        {
            if (db.State != ConnectionState.Open || !ReferenceEquals(tx.Connection, db))
                throw new InvalidOperationException("Authority guard requires the active persistence transaction.");
            return await _store.Lock(db, tx, ScopeKey, mode, ct).ConfigureAwait(false)
                ?? throw new PlayerWriteAuthorityException(ScopeKey, null);
        }
        private static bool IsAllowed(PlayerWriteTransition t)
        {
            if (t.ExpectedState == PlayerWriteState.Offline) return t.NextState == PlayerWriteState.Starting;
            if (t.NextState == PlayerWriteState.Offline)
                return t.ExpectedState == PlayerWriteState.Draining || t.ExpectedState == PlayerWriteState.RecoveryRequired;
            if (t.ExpectedInstanceId != t.NextInstanceId) return false;
            return (t.ExpectedState == PlayerWriteState.Starting && (t.NextState == PlayerWriteState.Online || t.NextState == PlayerWriteState.RecoveryRequired)) ||
                (t.ExpectedState == PlayerWriteState.Online && (t.NextState == PlayerWriteState.Draining || t.NextState == PlayerWriteState.RecoveryRequired)) ||
                (t.ExpectedState == PlayerWriteState.Draining && t.NextState == PlayerWriteState.RecoveryRequired);
        }
        private PlayerWriteTransitionResult Result(PlayerWriteTransitionOutcome outcome, PlayerWriteAuthorityRecord record, PlayerWriteTransition transition, Exception? error = null)
        {
            try
            {
                _logger.LogInformation(new EventId(4110, "PlayerWriteTransition"), error,
                    "Player write transition {ScopeKey} {InstanceId} {TransitionId} {OperatorKey} {ExpectedState} {NextState} {Outcome}",
                    ScopeKey, record.InstanceId, transition.Id, transition.OperatorKey, transition.ExpectedState, transition.NextState, outcome);
            }
            catch { }
            return new PlayerWriteTransitionResult(outcome, record, error);
        }
        private async ValueTask CleanupAsync(DbConnection? db, DbTransaction? tx)
        {
            if (tx != null)
            {
                try { await tx.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { LogCleanup(ex); }
            }
            if (db != null)
            {
                try { await db.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { LogCleanup(ex); }
            }
        }
        private void LogCleanup(Exception error)
        {
            try { _logger.LogError(new EventId(4111, "PlayerWriteAuthorityCleanupFailed"), error, "Authority cleanup failed for {ScopeKey}", ScopeKey); }
            catch { }
        }
    }
}
