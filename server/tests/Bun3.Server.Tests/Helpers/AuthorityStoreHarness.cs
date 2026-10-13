using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Bun3.Server.Players;

namespace Bun3.Server.Tests.Helpers;

internal sealed class AuthorityStoreHarness : PlayerWriteAuthorityStore
{
    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal PlayerWriteAuthorityRecord? Row = new(PlayerWriteState.Offline, null, Guid.NewGuid(), new byte[32]);
    internal bool FailAudit;
    internal bool LoseCommitAck;
    internal bool FailRecovery { get; set; }
    internal int Writes;
    internal int Audits;
    internal int RecoveryChecks;
    internal int Connections;
    internal DbTransaction? ObservedTransaction;
    internal PlayerWriteLock ObservedLock;
    internal Func<ValueTask>? BeforeLock { get; set; }
    internal PlayerWriteAuthority Authority => new("test-scope", this);
    internal DbConnection NewConnection() => new Connection(this);
    protected override DbConnection CreateConnection() { Connections++; return NewConnection(); }
    protected override async ValueTask<PlayerWriteAuthorityRecord?> LockAsync(DbConnection db, DbTransaction tx, string key, PlayerWriteLock mode, CancellationToken ct)
    {
        if (db.State != ConnectionState.Open || tx.Connection != db) throw new InvalidOperationException("Wrong database transaction.");
        if (BeforeLock != null) await BeforeLock();
        var transaction = (Transaction)tx;
        if (!transaction.Locked) { await Gate.WaitAsync(ct); transaction.Locked = true; }
        ObservedTransaction = tx; ObservedLock = mode;
        return Row;
    }
    protected override ValueTask WriteAsync(DbConnection db, DbTransaction tx, string key, PlayerWriteAuthorityRecord row, PlayerWriteTransition transition, CancellationToken ct)
    {
        Writes++; var transaction = (Transaction)tx; transaction.Pending = row;
        if (FailAudit) throw new InvalidOperationException("audit failed");
        transaction.PendingAudits++; return default;
    }
    protected override ValueTask VerifyRecoveryAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    { RecoveryChecks++; if (FailRecovery) throw new InvalidOperationException("recovery unresolved"); return default; }

    internal sealed class Connection(AuthorityStoreHarness store) : DbConnection
    {
        private ConnectionState _state;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "test";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() => _state = ConnectionState.Open;
        public override void Close() => _state = ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new Transaction(store, this, isolationLevel);
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
    internal sealed class Transaction(AuthorityStoreHarness store, Connection db, IsolationLevel isolation) : DbTransaction
    {
        internal bool Locked;
        internal PlayerWriteAuthorityRecord? Pending;
        internal int PendingAudits;
        internal Action? OnCommit { get; set; }
        public override IsolationLevel IsolationLevel => isolation;
        protected override DbConnection DbConnection => db;
        public override void Commit()
        {
            if (Pending != null) store.Row = Pending;
            store.Audits += PendingAudits;
            OnCommit?.Invoke(); Release();
            if (store.LoseCommitAck) throw new IOException("commit acknowledgement lost");
        }
        public override void Rollback() => Release();
        protected override void Dispose(bool disposing) { if (disposing) Release(); base.Dispose(disposing); }
        private void Release() { if (Locked) { Locked = false; store.Gate.Release(); } }
    }
}
