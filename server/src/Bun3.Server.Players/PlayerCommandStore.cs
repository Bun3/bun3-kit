using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Players
{
    /// <summary>Maps durable command acceptance and terminal results into the same database as player persistence.</summary>
    public abstract class PlayerCommandStore
    {
        /// <summary>Creates a closed connection for acceptance and observation.</summary>
        protected abstract DbConnection CreateConnection();
        /// <summary>Atomically inserts Pending or returns the existing row for a unique command ID.</summary>
        protected abstract ValueTask<PlayerCommandRecord> InsertOrReadAsync(DbConnection db, DbTransaction tx, PlayerCommandEnvelope command, CancellationToken ct);
        /// <summary>Reads a command, optionally locking its row until the supplied transaction ends.</summary>
        protected abstract ValueTask<PlayerCommandRecord?> ReadAsync(DbConnection db, DbTransaction tx, Guid id, bool forUpdate, CancellationToken ct);
        /// <summary>Finds pending IDs, applying supportedInputs before the limit. Null includes all input types.</summary>
        protected abstract ValueTask<IReadOnlyList<Guid>> FindPendingAsync(DbConnection db, DbTransaction tx, string scopeKey, int limit,
            IReadOnlyList<(string Kind, int InputSchemaVersion)>? supportedInputs, CancellationToken ct);
        /// <summary>Writes a terminal result to the already locked command row in the supplied player transaction.</summary>
        protected abstract ValueTask CompleteAsync(DbConnection db, DbTransaction tx, PlayerCommandRecord record, CancellationToken ct);
        internal DbConnection Create() => CreateConnection();
        internal ValueTask<PlayerCommandRecord> Insert(DbConnection db, DbTransaction tx, PlayerCommandEnvelope command, CancellationToken ct) => InsertOrReadAsync(db, tx, command, ct);
        internal ValueTask<PlayerCommandRecord?> Read(DbConnection db, DbTransaction tx, Guid id, bool forUpdate, CancellationToken ct) => ReadAsync(db, tx, id, forUpdate, ct);
        internal ValueTask<IReadOnlyList<Guid>> Find(DbConnection db, DbTransaction tx, string scope, int limit,
            IReadOnlyList<(string Kind, int InputSchemaVersion)>? supportedInputs, CancellationToken ct) => FindPendingAsync(db, tx, scope, limit, supportedInputs, ct);
        internal ValueTask Complete(DbConnection db, DbTransaction tx, PlayerCommandRecord record, CancellationToken ct)
        {
            if (!record.IsTerminal) throw new InvalidOperationException("Only terminal command results can be persisted.");
            return CompleteAsync(db, tx, record, ct);
        }
    }
}
