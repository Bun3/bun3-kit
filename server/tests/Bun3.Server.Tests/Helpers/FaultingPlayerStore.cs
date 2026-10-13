using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Bun3.Server.Players;

namespace Bun3.Server.Tests.Helpers;

internal sealed class FaultingPlayerStore : PlayerStore<ExecutionPlayer>
{
    internal enum CommitFault { None, BeforeApply, AfterApply }
    internal CommitFault CommitFailure;
    internal bool RollbackFailure;
    internal bool DisposeConnectionFailure;
    internal bool DisposeTransactionFailure;
    internal bool WriteFailure;
    internal bool RejectWrite;
    internal bool AuditFailure;
    internal bool MissingAnchor;
    internal Exception? FactoryError;
    internal Exception? OpenError;
    internal Exception? BeginError;
    internal Exception? LoadError;
    internal long Gold = 100;
    internal long Version = 3;
    internal int Writes;
    internal int Audits;
    internal int Loads;
    internal int Commits;
    internal int Rollbacks;
    internal int ConnectionsDisposed;
    internal int TransactionsDisposed;
    internal readonly List<string> LockedKeys = new();
    internal Action? OnWrite;
    internal Func<ValueTask>? BeforeLock;
    protected override DbConnection CreateConnection() => FactoryError == null ? new Connection(this) : throw FactoryError;
    protected override async ValueTask<long?> LockVersionAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    {
        Verify(db, tx); LockedKeys.Add(key);
        if (BeforeLock != null) await BeforeLock();
        return MissingAnchor ? null : Version;
    }
    protected override ValueTask<ExecutionPlayer> LoadPlayerAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    { Verify(db, tx); Loads++; if (LoadError != null) throw LoadError; return new(new ExecutionPlayer { Gold = Gold }); }
    protected override object CaptureChanges(ExecutionPlayer player) => player.Gold;
    protected override ValueTask<bool> WritePlayerAsync(DbConnection db, DbTransaction tx, string key, long expectedVersion, object changes, CancellationToken ct)
    {
        Verify(db, tx); Writes++;
        var transaction = (Transaction)tx;
        transaction.HasWrites = true;
        transaction.Gold = (long)changes; transaction.Version = expectedVersion + 1;
        OnWrite?.Invoke();
        if (WriteFailure) throw new InvalidOperationException("write");
        return new(!RejectWrite);
    }
    protected override ValueTask<PlayerOperationRecord?> ReadOperationAsync(DbConnection db, DbTransaction tx, Guid id, CancellationToken ct)
    { Verify(db, tx); return new((PlayerOperationRecord?)null); }
    protected override ValueTask WriteOperationAsync(DbConnection db, DbTransaction tx, PlayerOperationRecord record, CancellationToken ct)
    { Verify(db, tx); return default; }
    protected override ValueTask WriteAuditAsync(DbConnection db, DbTransaction tx, string key, IReadOnlyList<PlayerAuditRecord> records, CancellationToken ct)
    { Verify(db, tx); Audits += records.Count; if (AuditFailure) throw new InvalidOperationException("audit"); return default; }
    private static void Verify(DbConnection db, DbTransaction tx)
    { if (db.State != ConnectionState.Open || !ReferenceEquals(tx.Connection, db)) throw new InvalidOperationException("Wrong database transaction."); }

    private sealed class Connection(FaultingPlayerStore store) : DbConnection
    {
        private ConnectionState _state;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "test";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() { if (store.OpenError != null) throw store.OpenError; _state = ConnectionState.Open; }
        public override void Close() => _state = ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => store.BeginError == null ? new Transaction(store, this, isolationLevel) : throw store.BeginError;
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        public override ValueTask DisposeAsync()
        { store.ConnectionsDisposed++; Close(); if (store.DisposeConnectionFailure) throw new InvalidOperationException("connection dispose"); return default; }
    }
    private sealed class Transaction(FaultingPlayerStore store, Connection db, IsolationLevel isolation) : DbTransaction
    {
        internal long Gold = store.Gold;
        internal long Version = store.Version;
        internal bool HasWrites;
        public override IsolationLevel IsolationLevel => isolation;
        protected override DbConnection DbConnection => db;
        public override void Commit()
        {
            store.Commits++;
            if (store.CommitFailure == CommitFault.BeforeApply) throw new InvalidOperationException("commit before apply");
            if (HasWrites) { store.Gold = Gold; store.Version = Version; }
            if (store.CommitFailure == CommitFault.AfterApply) throw new InvalidOperationException("commit acknowledgement lost");
        }
        public override void Rollback()
        { store.Rollbacks++; if (store.RollbackFailure) throw new InvalidOperationException("rollback acknowledgement lost"); }
        public override ValueTask DisposeAsync()
        { store.TransactionsDisposed++; if (store.DisposeTransactionFailure) throw new InvalidOperationException("transaction dispose"); return default; }
    }
}
