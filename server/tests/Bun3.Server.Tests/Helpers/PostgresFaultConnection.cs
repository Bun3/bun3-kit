using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace Bun3.Server.Tests.Helpers;

internal sealed class PostgresFaultConnection(NpgsqlConnection inner, bool loseAcknowledgement, Task? delay) : DbConnection
{
    internal NpgsqlConnection Inner => inner;
    internal bool Detached;
    internal Task PendingCommit { get; private set; } = Task.CompletedTask;
    [AllowNull] public override string ConnectionString { get => inner.ConnectionString; set => inner.ConnectionString = value!; }
    public override string Database => inner.Database;
    public override string DataSource => inner.DataSource;
    public override string ServerVersion => inner.ServerVersion;
    public override ConnectionState State => inner.State;
    public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);
    public override void Open() => inner.Open();
    public override Task OpenAsync(CancellationToken ct) => inner.OpenAsync(ct);
    public override void Close() { if (!Detached) inner.Close(); }
    protected override DbCommand CreateDbCommand() => inner.CreateCommand();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new FaultTransaction(this, inner.BeginTransaction(isolationLevel), loseAcknowledgement, delay);
    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken ct)
        => new FaultTransaction(this, await inner.BeginTransactionAsync(isolationLevel, ct), loseAcknowledgement, delay);
    public override ValueTask DisposeAsync() => Detached ? default : inner.DisposeAsync();

    internal sealed class FaultTransaction(PostgresFaultConnection owner, NpgsqlTransaction inner, bool loseAcknowledgement, Task? delay) : DbTransaction
    {
        internal NpgsqlTransaction Inner => inner;
        public override IsolationLevel IsolationLevel => inner.IsolationLevel;
        protected override DbConnection DbConnection => owner;
        public override void Commit() => CommitAsync(default).GetAwaiter().GetResult();
        public override async Task CommitAsync(CancellationToken ct = default)
        {
            if (delay != null)
            {
                owner.Detached = true; owner.PendingCommit = FinishServerCommitAsync(delay);
                throw new IOException("Injected client timeout while the server transaction is still executing.");
            }
            await inner.CommitAsync(ct);
            if (loseAcknowledgement) throw new IOException("Injected lost commit acknowledgement.");
        }
        private async Task FinishServerCommitAsync(Task release)
        {
            try { await release; await inner.CommitAsync(); }
            finally { await inner.DisposeAsync(); await owner.Inner.DisposeAsync(); }
        }
        public override void Rollback()
        { if (owner.Detached) throw new IOException("The detached server transaction is still running."); inner.Rollback(); }
        public override Task RollbackAsync(CancellationToken ct = default)
        { if (owner.Detached) throw new IOException("The detached server transaction is still running."); return inner.RollbackAsync(ct); }
        public override ValueTask DisposeAsync() => owner.Detached ? default : inner.DisposeAsync();
    }
}
