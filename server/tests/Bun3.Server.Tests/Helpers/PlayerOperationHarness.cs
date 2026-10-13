using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Bun3.Server.Players;

namespace Bun3.Server.Tests.Helpers;

internal sealed class TransferResult
{
    public long SenderBalance { get; set; }
    public long RecipientBalance { get; set; }
}

internal sealed class TransferCodec : IPlayerResultCodec<TransferResult>
{
    public int SchemaVersion => 1;
    internal bool FailEncode;
    public ReadOnlyMemory<byte> Encode(TransferResult result) => FailEncode
        ? throw new InvalidOperationException("codec") : JsonSerializer.SerializeToUtf8Bytes(result);
    public TransferResult Decode(int version, ReadOnlyMemory<byte> payload) => JsonSerializer.Deserialize<TransferResult>(payload.Span)!;
}

internal sealed class TransferOperation : PlayerOperation<ExecutionPlayer, TransferResult>
{
    internal int ApplyCalls;
    internal long Amount = 10;
    internal Action? Applying;
    internal Action<PlayerOperationContext<ExecutionPlayer>>? Validating;
    internal PlayerOperationContext<ExecutionPlayer>? CapturedContext;
    internal TransferResult? MutableResult;
    internal TransferOperation(Guid? id = null, string actor = "a", string kind = "transfer", string[]? keys = null,
        byte[]? fingerprint = null, TransferCodec? codec = null)
        : base(id ?? Guid.NewGuid(), kind, actor, keys ?? new[] { "a", "b" }, fingerprint ?? new byte[32], codec ?? new TransferCodec()) { }
    public override int Validate(PlayerOperationContext<ExecutionPlayer> context)
    { Validating?.Invoke(context); return context.GetPlayer(ActorKey).Gold < Amount ? -10 : 0; }
    public override TransferResult Apply(PlayerOperationContext<ExecutionPlayer> context)
    {
        ApplyCalls++;
        CapturedContext = context;
        var a = context.GetPlayer(ParticipantKeys[0]);
        var b = context.GetPlayer(ParticipantKeys[1]);
        a.Gold -= Amount; b.Gold += Amount;
        a.MarkDirty(); b.MarkDirty();
        Applying?.Invoke();
        return MutableResult = new TransferResult { SenderBalance = a.Gold, RecipientBalance = b.Gold };
    }
}

internal sealed class OperationStore : PlayerStore<ExecutionPlayer>
{
    internal readonly Dictionary<string, (long Gold, long Version)> Rows = new(StringComparer.Ordinal)
        { ["a"] = (100, 3), ["b"] = (20, 8), ["c"] = (100, 3), ["d"] = (20, 8) };
    internal readonly Dictionary<Guid, PlayerOperationRecord> Receipts = new();
    internal readonly List<ExecutionPlayer> Loaded = new();
    internal Func<string, ValueTask>? BeforeLock;
    internal Action<ExecutionPlayer>? ConfigurePlayer;
    internal Func<string, ValueTask>? BeforeLoad;
    internal Func<ValueTask>? BeforeCommit;
    internal Action? OnWrite;
    internal int FailWriteAt;
    internal bool LostCommitAck;
    internal bool FailCommitBeforeApply;
    internal bool FailAudit;
    internal readonly List<PlayerAuditRecord> Audits = new();
    internal int Writes;
    internal int Commits;
    internal int WriteCommits;
    protected override DbConnection CreateConnection() => new Connection(this);
    protected override async ValueTask<long?> LockVersionAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    {
        if (BeforeLock != null) await BeforeLock(key);
        ct.ThrowIfCancellationRequested();
        lock (Rows) return Rows.TryGetValue(key, out var row) ? row.Version : null;
    }
    protected override async ValueTask<ExecutionPlayer> LoadPlayerAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    {
        if (BeforeLoad != null) await BeforeLoad(key);
        ct.ThrowIfCancellationRequested();
        ExecutionPlayer player;
        lock (Rows) player = new ExecutionPlayer { Gold = Rows[key].Gold };
        ConfigurePlayer?.Invoke(player);
        lock (Loaded) Loaded.Add(player);
        return player;
    }
    protected override object CaptureChanges(ExecutionPlayer player) => player.Gold;
    protected override ValueTask<bool> WritePlayerAsync(DbConnection db, DbTransaction tx, string key, long version, object changes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ((Transaction)tx).Pending[key] = ((long)changes, version + 1);
        var writes = Interlocked.Increment(ref Writes);
        OnWrite?.Invoke();
        if (writes == FailWriteAt) throw new InvalidOperationException("write");
        return new(true);
    }
    protected override ValueTask<PlayerOperationRecord?> ReadOperationAsync(DbConnection db, DbTransaction tx, Guid id, CancellationToken ct)
    { lock (Rows) return new(Receipts.GetValueOrDefault(id)); }
    protected override ValueTask WriteOperationAsync(DbConnection db, DbTransaction tx, PlayerOperationRecord record, CancellationToken ct)
    { ((Transaction)tx).Receipt = record; return default; }
    protected override ValueTask WriteAuditAsync(DbConnection db, DbTransaction tx, string key, IReadOnlyList<PlayerAuditRecord> records, CancellationToken ct)
    {
        if (FailAudit) throw new InvalidOperationException("audit");
        ((Transaction)tx).Audit.AddRange(records);
        return default;
    }
    private sealed class Connection(OperationStore store) : DbConnection
    {
        private ConnectionState _state;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "test";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string name) => throw new NotSupportedException();
        public override void Open() => _state = ConnectionState.Open;
        public override void Close() => _state = ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolation) => new Transaction(store, this, isolation);
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
    private sealed class Transaction(OperationStore store, Connection db, IsolationLevel isolation) : DbTransaction
    {
        internal readonly Dictionary<string, (long Gold, long Version)> Pending = new();
        internal PlayerOperationRecord? Receipt;
        internal readonly List<PlayerAuditRecord> Audit = new();
        public override IsolationLevel IsolationLevel => isolation;
        protected override DbConnection DbConnection => db;
        public override void Commit() => throw new NotSupportedException();
        public override async Task CommitAsync(CancellationToken ct = default)
        {
            if (store.BeforeCommit != null) await store.BeforeCommit();
            ct.ThrowIfCancellationRequested();
            if (store.FailCommitBeforeApply) throw new InvalidOperationException("connection lost before commit applied");
            lock (store.Rows)
            {
                store.Commits++;
                store.Audits.AddRange(Audit);
                if (Pending.Count > 0) store.WriteCommits++;
                foreach (var pair in Pending) store.Rows[pair.Key] = pair.Value;
                if (Receipt != null) store.Receipts.Add(Receipt.Id, Receipt);
            }
            if (store.LostCommitAck) throw new InvalidOperationException("commit acknowledgement lost");
        }
        public override void Rollback() { }
    }
}

internal sealed class PlayerOperationHarness : IDisposable
{
    internal readonly OperationStore Store = new();
    internal readonly PlayerRegistry<ExecutionPlayer> Registry;
    private int _connection;
    internal PlayerOperationHarness() => Registry = new(Store);
    internal ExecutionSession NewSession() => Registry.Wrap(new PlayersConfig<ExecutionSession>(),
        c => new ExecutionSession(c))(new FakeConnection(Interlocked.Increment(ref _connection), new FakeTransport()));
    internal async Task<ExecutionSession> Login(string key)
    { var session = NewSession(); await session.SignInAsync(key); return session; }
    public void Dispose() => Registry.Dispose();
}
