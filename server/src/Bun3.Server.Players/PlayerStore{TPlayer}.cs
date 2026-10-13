using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    /// <summary>Maps player persistence using an anchor version checked on every write.</summary>
    public abstract class PlayerStore<TPlayer> : PlayerPersistence<TPlayer> where TPlayer : Player
    {
        /// <summary>Specifies the isolation used for all store transactions.</summary>
        protected PlayerStore(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) : base(isolationLevel) { }
        /// <summary>Locks an account anchor until transaction completion and returns its version, or null if absent.</summary>
        protected abstract ValueTask<long?> LockVersionAsync(DbConnection db, DbTransaction tx, string accountKey, CancellationToken ct);
        /// <summary>Writes changes and atomically advances exactly one anchor from expectedVersion to expectedVersion + 1.</summary>
        protected abstract ValueTask<bool> WritePlayerAsync(DbConnection db, DbTransaction tx, string accountKey, long expectedVersion, object changes, CancellationToken ct);
        internal override async ValueTask<PlayerStoreAnchor> LockAnchorAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
        {
            var version = await LockVersionAsync(db, tx, key, ct).ConfigureAwait(false);
            return version.HasValue ? PlayerStoreAnchor.Versioned(version.Value) : PlayerStoreAnchor.Missing;
        }
        internal override async ValueTask WriteChangesAsync(DbConnection db, DbTransaction tx, PlayerSaveData data, CancellationToken ct)
        {
            var version = data.ExpectedVersion ?? throw new InvalidOperationException("A persistent version is required.");
            _ = checked(version + 1);
            if (!await WritePlayerAsync(db, tx, data.Key, version, data.Changes, ct).ConfigureAwait(false))
                throw new PlayerVersionConflictException(data.Key);
        }
        internal override ValueTask ProtectAsync(DbConnection db, DbTransaction tx, PlayerStoreAccess access, CancellationToken ct) => default;
    }
}
