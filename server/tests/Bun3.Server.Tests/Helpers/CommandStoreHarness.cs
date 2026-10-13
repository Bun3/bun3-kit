using System.Data.Common;
using Bun3.Server.Players;

namespace Bun3.Server.Tests.Helpers;

internal sealed class CommandStoreHarness(AuthorityStoreHarness control) : PlayerCommandStore
{
    internal readonly Dictionary<Guid, PlayerCommandRecord> Rows = new();
    internal Func<PlayerCommandRecord, ValueTask>? BeforeComplete { get; set; }
    internal int Completes;
    internal DbTransaction? LastCompleteTransaction;
    protected override DbConnection CreateConnection() => control.NewConnection();
    private async ValueTask Lock(DbTransaction tx, CancellationToken ct)
    {
        var transaction = (AuthorityStoreHarness.Transaction)tx;
        if (!transaction.Locked) { await control.Gate.WaitAsync(ct); transaction.Locked = true; }
    }
    protected override async ValueTask<PlayerCommandRecord> InsertOrReadAsync(DbConnection db, DbTransaction tx, PlayerCommandEnvelope command, CancellationToken ct)
    {
        await Lock(tx, ct);
        if (Rows.TryGetValue(command.Id, out var existing)) return existing;
        var record = new PlayerCommandRecord(command, DateTimeOffset.UtcNow, PlayerCommandStatus.Pending, 0, 0, default);
        ((AuthorityStoreHarness.Transaction)tx).OnCommit += () => Rows.Add(command.Id, record); return record;
    }
    protected override async ValueTask<PlayerCommandRecord?> ReadAsync(DbConnection db, DbTransaction tx, Guid id, bool forUpdate, CancellationToken ct)
    { await Lock(tx, ct); return Rows.GetValueOrDefault(id); }
    protected override async ValueTask<IReadOnlyList<Guid>> FindPendingAsync(DbConnection db, DbTransaction tx, string scopeKey, int limit,
        IReadOnlyList<(string Kind, int InputSchemaVersion)>? supportedInputs, CancellationToken ct)
    { await Lock(tx, ct); return Rows.Values.Where(r => r.Envelope.ScopeKey == scopeKey && r.Status == PlayerCommandStatus.Pending &&
        (supportedInputs == null || supportedInputs.Contains((r.Envelope.Kind, r.Envelope.InputSchemaVersion)))).Take(limit).Select(r => r.Envelope.Id).ToArray(); }
    protected override async ValueTask CompleteAsync(DbConnection db, DbTransaction tx, PlayerCommandRecord record, CancellationToken ct)
    {
        await Lock(tx, ct); Completes++; LastCompleteTransaction = tx;
        if (BeforeComplete != null) await BeforeComplete(record);
        ((AuthorityStoreHarness.Transaction)tx).OnCommit += () => Rows[record.Envelope.Id] = record;
    }
}

internal sealed class CommandHarness : IDisposable
{
    internal readonly OwnedStoreHarness Store;
    internal readonly CommandStoreHarness Commands;
    internal readonly PlayerCommandQueue Queue;
    internal readonly PlayerCommandCatalog<ExecutionPlayer> Catalog = new();
    internal readonly PlayerRegistry<ExecutionPlayer> Registry;
    internal readonly List<CommandGrant> Operations = new();
    internal Func<PlayerCommandEnvelope, CommandGrant>? Factory { get; set; }
    internal CommandHarness(bool offline = true, bool supportsOffline = true)
    {
        Store = offline ? OwnedStoreHarness.Offline() : OwnedStoreHarness.Online();
        Commands = new(Store.Control); Queue = new(Commands); Registry = OwnedStoreHarness.ReadyRegistry(Store);
        Catalog.Register("grant", 1, supportsOffline, envelope =>
        { var op = Factory?.Invoke(envelope) ?? new CommandGrant(envelope); Operations.Add(op); return op; });
    }
    internal PlayerCommandDispatcher<ExecutionPlayer> Dispatcher => new(Queue, Catalog, Registry);
    internal static PlayerCommandEnvelope Envelope(Guid? id = null, string actor = "admin", string scope = "test-scope", byte amount = 7,
        int schema = 1, string kind = "grant") => new(id ?? Guid.NewGuid(), scope, new(PlayerOperationActorKind.Administrator, actor), kind, schema,
            new[] { "a" }, new byte[32], new byte[] { amount });
    public void Dispose() => Registry.Dispose();
}

internal sealed class CommandGrant : PlayerOperation<ExecutionPlayer, TransferResult>
{
    private readonly byte _amount;
    internal int Applies;
    internal int ValidationStatus { get; set; }
    internal bool Audit { get; set; }
    internal Action? Applying { get; set; }
    internal CommandGrant(PlayerCommandEnvelope envelope, Guid? id = null) : base(id ?? envelope.Id, envelope.Kind, envelope.Actor,
        envelope.ParticipantKeys, envelope.InputFingerprint, new TransferCodec()) => _amount = envelope.InputPayload.Span[0];
    public override int Validate(PlayerOperationContext<ExecutionPlayer> context) => ValidationStatus;
    public override TransferResult Apply(PlayerOperationContext<ExecutionPlayer> context)
    {
        Applies++; Applying?.Invoke(); var player = context.GetPlayer("a"); player.Gold += _amount; player.MarkDirty();
        if (Audit) player.RecordAudit("granted", new byte[] { _amount });
        return new TransferResult { RecipientBalance = player.Gold };
    }
}
