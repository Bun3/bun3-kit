using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    /// <summary>Specifies the transaction-lifetime control row lock.</summary>
    public enum PlayerWriteLock
    {
        /// <summary>Allows other guarded writers while preventing ownership changes.</summary>
        Shared,
        /// <summary>Waits for all guarded writers and excludes other transitions.</summary>
        Exclusive
    }

    /// <summary>Maps control row locking and atomic transition auditing to the player's database.</summary>
    public abstract class PlayerWriteAuthorityStore
    {
        /// <summary>Creates a closed connection owned by the coordinator.</summary>
        protected abstract DbConnection CreateConnection();
        /// <summary>Locks the control row in the supplied transaction until completion, returning null if absent.</summary>
        protected abstract ValueTask<PlayerWriteAuthorityRecord?> LockAsync(DbConnection db, DbTransaction tx, string scopeKey, PlayerWriteLock mode, CancellationToken ct);
        /// <summary>Writes the control row and its audit record atomically in the supplied transaction.</summary>
        protected abstract ValueTask WriteAsync(DbConnection db, DbTransaction tx, string scopeKey, PlayerWriteAuthorityRecord record, PlayerWriteTransition transition, CancellationToken ct);
        /// <summary>Checks unresolved durable work under the exclusive recovery barrier; throws if recovery is unsafe.</summary>
        protected abstract ValueTask VerifyRecoveryAsync(DbConnection db, DbTransaction tx, string scopeKey, CancellationToken ct);
        internal DbConnection Create() => CreateConnection();
        internal ValueTask<PlayerWriteAuthorityRecord?> Lock(DbConnection db, DbTransaction tx, string key, PlayerWriteLock mode, CancellationToken ct) => LockAsync(db, tx, key, mode, ct);
        internal ValueTask Write(DbConnection db, DbTransaction tx, string key, PlayerWriteAuthorityRecord record, PlayerWriteTransition transition, CancellationToken ct) => WriteAsync(db, tx, key, record, transition, ct);
        internal ValueTask VerifyRecovery(DbConnection db, DbTransaction tx, string key, CancellationToken ct) => VerifyRecoveryAsync(db, tx, key, ct);
    }
}
