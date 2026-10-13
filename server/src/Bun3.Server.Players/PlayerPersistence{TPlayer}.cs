using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Players
{
    /// <summary>Maps player persistence to one relational database. The framework owns connection and transaction lifetime.</summary>
    /// <remarks>Mappings must use only the supplied connection and transaction, propagate failures,
    /// and never commit, roll back, or dispose them. Account anchors must already exist.</remarks>
    public abstract class PlayerPersistence<TPlayer> where TPlayer : Player
    {
        private readonly IsolationLevel _isolationLevel;
        /// <summary>Specifies the isolation used for all store transactions.</summary>
        private protected PlayerPersistence(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) => _isolationLevel = isolationLevel;
        /// <summary>Creates a closed connection owned by the framework.</summary>
        protected abstract DbConnection CreateConnection();
        /// <summary>Loads fresh state after its anchor has been locked.</summary>
        protected abstract ValueTask<TPlayer> LoadPlayerAsync(DbConnection db, DbTransaction tx, string accountKey, CancellationToken ct);
        /// <summary>Captures changes whose ownership lasts until the framework processes the database outcome.</summary>
        protected abstract object CaptureChanges(TPlayer player);
        /// <summary>Reads a previously committed operation receipt.</summary>
        protected abstract ValueTask<PlayerOperationRecord?> ReadOperationAsync(DbConnection db, DbTransaction tx, Guid operationId, CancellationToken ct);
        /// <summary>Writes a receipt in the same transaction as player changes.</summary>
        protected abstract ValueTask WriteOperationAsync(DbConnection db, DbTransaction tx, PlayerOperationRecord record, CancellationToken ct);
        /// <summary>Writes audit records in the same transaction as player changes.</summary>
        protected abstract ValueTask WriteAuditAsync(DbConnection db, DbTransaction tx, string accountKey, IReadOnlyList<PlayerAuditRecord> records, CancellationToken ct);

        internal object Capture(TPlayer player) => CaptureChanges(player) ?? throw new InvalidOperationException("Captured changes cannot be null.");
        internal ValueTask<TPlayer> LoadAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct) => LoadPlayerAsync(db, tx, key, ct);
        internal abstract ValueTask<PlayerStoreAnchor> LockAnchorAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct);
        internal abstract ValueTask WriteChangesAsync(DbConnection db, DbTransaction tx, PlayerSaveData data, CancellationToken ct);
        internal abstract ValueTask ProtectAsync(DbConnection db, DbTransaction tx, PlayerStoreAccess access, CancellationToken ct);
        internal ValueTask AuditAsync(DbConnection db, DbTransaction tx, PlayerSaveData data, CancellationToken ct) => WriteAuditAsync(db, tx, data.Key, data.AuditRecords, ct);
        internal ValueTask<PlayerOperationRecord?> ReadReceiptAsync(DbConnection db, DbTransaction tx, Guid id, CancellationToken ct) => ReadOperationAsync(db, tx, id, ct);
        internal ValueTask WriteReceiptAsync(DbConnection db, DbTransaction tx, PlayerOperationRecord record, CancellationToken ct) => WriteOperationAsync(db, tx, record, ct);

        internal async ValueTask<PlayerStoreScope<TPlayer>> OpenScopeAsync(IReadOnlyList<string> keys, ILogger logger, CancellationToken ct,
            PlayerStoreAccess access = PlayerStoreAccess.Operation, Action<Exception>? cleanupFailed = null)
        {
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            var copy = new string[keys.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = keys[i];
                if (string.IsNullOrEmpty(copy[i]) || (i > 0 && StringComparer.Ordinal.Compare(copy[i - 1], copy[i]) >= 0))
                    throw new ArgumentException("Store keys must be nonempty, sorted, and unique.", nameof(keys));
            }
            DbConnection? db = null;
            DbTransaction? tx = null;
            PlayerStoreScope<TPlayer>? scope = null;
            var phase = "ConnectionFactory";
            try
            {
                db = CreateConnection() ?? throw new InvalidOperationException("Store returned no connection.");
                phase = "ConnectionOpen";
                await db.OpenAsync(ct).ConfigureAwait(false);
                phase = "TransactionBegin";
                tx = await db.BeginTransactionAsync(_isolationLevel, ct).ConfigureAwait(false);
                phase = "Authority";
                await ProtectAsync(db, tx, access, ct).ConfigureAwait(false);
                phase = "AnchorLock";
                var anchors = new PlayerStoreAnchor[copy.Length];
                scope = new PlayerStoreScope<TPlayer>(this, db, tx, copy, anchors, logger, cleanupFailed);
                for (var i = 0; i < copy.Length; i++)
                    anchors[i] = await LockAnchorAsync(db, tx, copy[i], ct).ConfigureAwait(false);
                return scope;
            }
            catch (Exception ex)
            {
                var result = new PlayerStoreResult(PlayerStoreOutcome.Unknown, ex);
                if (scope != null)
                {
                    result = await scope.RollbackAsync(ex).ConfigureAwait(false);
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    if (tx != null)
                    {
                        try
                        {
                            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                            result = new PlayerStoreResult(PlayerStoreOutcome.RolledBack, ex);
                        }
                        catch (Exception rollback)
                        { result = new PlayerStoreResult(PlayerStoreOutcome.Unknown, new AggregateException(ex, rollback)); }
                        try { await tx.DisposeAsync().ConfigureAwait(false); }
                        catch (Exception cleanup) { cleanupFailed?.Invoke(cleanup); PlayerStoreScope<TPlayer>.LogCleanup(logger, cleanup); }
                    }
                    if (db != null)
                    {
                        try { await db.DisposeAsync().ConfigureAwait(false); }
                        catch (Exception cleanup) { cleanupFailed?.Invoke(cleanup); PlayerStoreScope<TPlayer>.LogCleanup(logger, cleanup); }
                    }
                }
                throw new PlayerStoreAcquisitionException(ex, result, phase);
            }
        }
    }
}
