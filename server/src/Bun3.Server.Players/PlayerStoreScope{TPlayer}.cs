using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.Players
{
    internal sealed class PlayerStoreScope<TPlayer> : IAsyncDisposable where TPlayer : Player
    {
        private readonly PlayerPersistence<TPlayer> _store;
        private readonly DbConnection _db;
        private readonly DbTransaction _tx;
        private readonly string[] _keys;
        private readonly PlayerStoreAnchor[] _anchors;
        private readonly ILogger _logger;
        private readonly Action<Exception>? _cleanupFailed;
        private bool _commitAttempted;
        private bool _disposed;
        private PlayerStoreResult? _result;
        internal PlayerStoreScope(PlayerPersistence<TPlayer> store, DbConnection db, DbTransaction tx, string[] keys, PlayerStoreAnchor[] anchors, ILogger logger, Action<Exception>? cleanupFailed = null)
        { _store = store; _db = db; _tx = tx; _keys = keys; _anchors = anchors; _logger = logger; _cleanupFailed = cleanupFailed; }
        internal long? GetVersion(string key) => GetAnchor(key).Version;
        internal DbConnection Connection { get { EnsureActive(); return _db; } }
        internal DbTransaction Transaction { get { EnsureActive(); return _tx; } }
        internal PlayerStoreAnchor GetAnchor(string key)
        {
            EnsureActive();
            var index = Array.BinarySearch(_keys, key, StringComparer.Ordinal);
            if (index < 0) throw new InvalidOperationException("Player is outside the store scope.");
            return _anchors[index];
        }
        internal ValueTask<TPlayer> LoadAsync(string key, CancellationToken ct)
        {
            if (!GetAnchor(key).Exists) throw new PlayerAnchorNotFoundException(key);
            return _store.LoadAsync(_db, _tx, key, ct);
        }
        internal ValueTask<PlayerOperationRecord?> ReadOperationAsync(Guid id, CancellationToken ct)
        { EnsureActive(); return _store.ReadReceiptAsync(_db, _tx, id, ct); }
        internal async ValueTask WriteAsync(PlayerSaveData data, CancellationToken ct)
        {
            var anchor = GetAnchor(data.Key);
            if (!anchor.Exists) throw new PlayerAnchorNotFoundException(data.Key);
            if (anchor.Version != data.ExpectedVersion) throw new PlayerVersionConflictException(data.Key);
            await _store.WriteChangesAsync(_db, _tx, data, ct).ConfigureAwait(false);
            if (data.AuditRecords.Count != 0) await _store.AuditAsync(_db, _tx, data, ct).ConfigureAwait(false);
        }
        internal ValueTask WriteOperationAsync(PlayerOperationRecord record, CancellationToken ct)
        { EnsureActive(); return _store.WriteReceiptAsync(_db, _tx, record, ct); }
        internal async ValueTask<PlayerStoreResult> CommitAsync(CancellationToken ct)
        {
            EnsureActive();
            try
            {
                ct.ThrowIfCancellationRequested();
                _commitAttempted = true;
                await _tx.CommitAsync(ct).ConfigureAwait(false);
                return (_result = new PlayerStoreResult(PlayerStoreOutcome.Committed)).Value;
            }
            catch (Exception ex) { return await RollbackAsync(ex).ConfigureAwait(false); }
        }
        internal async ValueTask<PlayerStoreResult> RollbackAsync(Exception cause)
        {
            if (_result.HasValue) return _result.Value;
            if (_disposed) throw new ObjectDisposedException(nameof(PlayerStoreScope<TPlayer>));
            try
            {
                await _tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return (_result = new PlayerStoreResult(_commitAttempted ? PlayerStoreOutcome.Unknown : PlayerStoreOutcome.RolledBack, cause)).Value;
            }
            catch (Exception rollback)
            {
                return (_result = new PlayerStoreResult(PlayerStoreOutcome.Unknown, new AggregateException(cause, rollback))).Value;
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            if (!_result.HasValue) await RollbackAsync(new InvalidOperationException("Store scope ended without commit.")).ConfigureAwait(false);
            _disposed = true;
            try { await _tx.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _cleanupFailed?.Invoke(ex); LogCleanup(_logger, ex); }
            try { await _db.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _cleanupFailed?.Invoke(ex); LogCleanup(_logger, ex); }
        }
        internal static void LogCleanup(ILogger logger, Exception error)
        {
            try { logger.LogError(new EventId(4103, "StoreCleanupFailed"), error, "Player store cleanup failed"); }
            catch { }
        }
        private void EnsureActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PlayerStoreScope<TPlayer>));
            if (_result.HasValue) throw new InvalidOperationException("Store transaction is already complete.");
        }
    }

    internal sealed class PlayerAnchorNotFoundException : InvalidOperationException
    {
        internal PlayerAnchorNotFoundException(string key) : base("Player anchor does not exist.") { AccountKey = key; }
        internal string AccountKey { get; }
    }
    internal sealed class PlayerVersionConflictException : InvalidOperationException
    {
        internal PlayerVersionConflictException(string key) : base("Player version conflict.") { AccountKey = key; }
        internal string AccountKey { get; }
    }
}
