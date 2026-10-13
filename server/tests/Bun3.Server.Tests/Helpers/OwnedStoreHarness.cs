using System.Data.Common;
using Bun3.Server.Players;

namespace Bun3.Server.Tests.Helpers;

internal sealed class OwnedStoreHarness : OwnedPlayerStore<ExecutionPlayer>
{
    internal AuthorityStoreHarness Control { get; }
    internal readonly Dictionary<string, long> Rows = new() { ["a"] = 100, ["b"] = 20 };
    internal readonly Dictionary<Guid, PlayerOperationRecord> Receipts = new();
    internal readonly HashSet<string> Foreign = new();
    internal readonly List<string> Locked = new();
    internal readonly List<ExecutionPlayer> Loaded = new();
    internal Func<string, ValueTask>? BeforeAnchor { get; set; }
    internal bool FailWrite { get; set; }
    internal bool FailReceipt { get; set; }
    internal int Writes;
    internal OwnedStoreHarness(AuthorityStoreHarness control, PlayerWriteBinding binding, Dictionary<string, long>? rows = null,
        Dictionary<Guid, PlayerOperationRecord>? receipts = null) : base(control.Authority, binding)
    { Control = control; if (rows != null) Rows = rows; if (receipts != null) Receipts = receipts; }
    internal static OwnedStoreHarness Online()
    {
        var owner = Guid.NewGuid(); var control = new AuthorityStoreHarness { Row = new(PlayerWriteState.Starting, owner, Guid.NewGuid(), new byte[32]) };
        return new(control, new(PlayerWriteRole.Server, owner));
    }
    internal static OwnedStoreHarness Offline() => new(new(), new(PlayerWriteRole.OfflineAdministrator, Guid.Empty));
    internal static PlayerRegistry<ExecutionPlayer> ReadyRegistry(OwnedStoreHarness store)
    {
        var registry = new PlayerRegistry<ExecutionPlayer>(store);
        if (store.Binding.Role == PlayerWriteRole.Server)
            new PlayerServerLifecycle<ExecutionPlayer>(registry).ActivateAsync().AsTask().GetAwaiter().GetResult();
        return registry;
    }
    protected override DbConnection CreateConnection() => Control.NewConnection();
    protected override async ValueTask<bool> LockPlayerAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    {
        if (!ReferenceEquals(Control.ObservedTransaction, tx)) throw new InvalidOperationException("Anchor locked before authority guard.");
        if (BeforeAnchor != null) await BeforeAnchor(key);
        Locked.Add(key); return Rows.ContainsKey(key) && !Foreign.Contains(key);
    }
    protected override ValueTask<ExecutionPlayer> LoadPlayerAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct)
    { var player = new ExecutionPlayer { Gold = Rows[key] }; Loaded.Add(player); return new(player); }
    protected override object CaptureChanges(ExecutionPlayer player) => player.Gold;
    protected override ValueTask WritePlayerAsync(DbConnection db, DbTransaction tx, string key, object changes, CancellationToken ct)
    {
        Writes++; if (FailWrite) throw new InvalidOperationException("write failed");
        ((AuthorityStoreHarness.Transaction)tx).OnCommit += () => Rows[key] = (long)changes; return default;
    }
    protected override ValueTask<PlayerOperationRecord?> ReadOperationAsync(DbConnection db, DbTransaction tx, Guid id, CancellationToken ct)
        => new(Receipts.GetValueOrDefault(id));
    protected override ValueTask WriteOperationAsync(DbConnection db, DbTransaction tx, PlayerOperationRecord record, CancellationToken ct)
    { if (FailReceipt) throw new InvalidOperationException("receipt failed"); ((AuthorityStoreHarness.Transaction)tx).OnCommit += () => Receipts.Add(record.Id, record); return default; }
    protected override ValueTask WriteAuditAsync(DbConnection db, DbTransaction tx, string key, IReadOnlyList<PlayerAuditRecord> records, CancellationToken ct)
        => default;
    internal static ExecutionSession Session(PlayerRegistry<ExecutionPlayer> registry) => registry.Wrap(new PlayersConfig<ExecutionSession>(),
        c => new ExecutionSession(c))(new FakeConnection(Random.Shared.Next(), new FakeTransport()));
}
