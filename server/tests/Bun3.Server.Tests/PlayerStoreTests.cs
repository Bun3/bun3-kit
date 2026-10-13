using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Bun3.Server.Tests;

public class PlayerStoreTests
{
    private static readonly string[] Keys = ["guest:a"];
    private static PlayerSaveData Save(long version = 3, IReadOnlyList<PlayerAuditRecord>? audit = null) => new("guest:a", version, 1, 90L, audit ?? Array.Empty<PlayerAuditRecord>());
    private static ExecutionSession Session(PlayerRegistry<ExecutionPlayer> registry) => registry.Wrap(new PlayersConfig<ExecutionSession>(), c => new ExecutionSession(c))(new FakeConnection(1, new FakeTransport()));

    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Dispose_error_preserves_committed_outcome(bool transaction, bool connection)
    {
        var store = new FaultingPlayerStore();
        using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var player = (await Session(registry).SignInAsync("guest:a")).Player;
        store.DisposeTransactionFailure = transaction; store.DisposeConnectionFailure = connection;
        player.Gold = 90; player.MarkDirty(); await registry.SaveAsync(player);
        Assert.That((store.Gold, store.Version), Is.EqualTo((90L, 4L)));
        Assert.That(registry.TryGet("guest:a"), Is.SameAs(player));
        Assert.That(player.IsDirty, Is.False); Assert.That(store.Rollbacks, Is.Zero);
        Assert.That(store.ConnectionsDisposed, Is.EqualTo(2));
        Assert.That(store.TransactionsDisposed, Is.EqualTo(2));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task Commit_attempt_exception_is_unknown_even_after_rollback_ack(bool applied)
    {
        var store = new FaultingPlayerStore { CommitFailure = applied ? FaultingPlayerStore.CommitFault.AfterApply : FaultingPlayerStore.CommitFault.BeforeApply };
        await using var scope = await store.OpenScopeAsync(Keys, NullLogger.Instance, default);
        await scope.WriteAsync(Save(), default);
        var result = await scope.CommitAsync(default);
        Assert.That(result.Outcome, Is.EqualTo(PlayerStoreOutcome.Unknown));
        Assert.That((store.Gold,store.Version), Is.EqualTo(applied ? (90L,4L) : (100L,3L)));
        Assert.That(store.Rollbacks, Is.EqualTo(1));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task Write_error_classifies_rollback_ack(bool rollbackFails)
    {
        var store = new FaultingPlayerStore { WriteFailure = true, RollbackFailure = rollbackFails };
        await using var scope = await store.OpenScopeAsync(Keys, NullLogger.Instance, default);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.WriteAsync(Save(), default));
        var result = await scope.RollbackAsync(error!);
        Assert.That(result.Outcome, Is.EqualTo(rollbackFails ? PlayerStoreOutcome.Unknown : PlayerStoreOutcome.RolledBack));
        Assert.That(store.Commits, Is.Zero); Assert.That(store.Gold, Is.EqualTo(100));
    }
    [Test]
    public async Task Canceled_before_commit_is_confirmed_rollback()
    {
        var store = new FaultingPlayerStore();
        await using var scope = await store.OpenScopeAsync(Keys, NullLogger.Instance, default);
        await scope.WriteAsync(Save(), default);
        var result = await scope.CommitAsync(new CancellationToken(true));
        Assert.That(result.Outcome, Is.EqualTo(PlayerStoreOutcome.RolledBack));
        Assert.That(store.Commits, Is.Zero);
    }
    [Test]
    public async Task False_write_and_overflow_fail_without_committing()
    {
        var store = new FaultingPlayerStore { RejectWrite = true };
        await using (var scope = await store.OpenScopeAsync(Keys, NullLogger.Instance, default))
            Assert.ThrowsAsync<PlayerVersionConflictException>(async () => await scope.WriteAsync(Save(), default));
        store.Version = long.MaxValue;
        await using (var scope = await store.OpenScopeAsync(Keys, NullLogger.Instance, default))
            Assert.ThrowsAsync<OverflowException>(async () => await scope.WriteAsync(Save(long.MaxValue), default));
        Assert.That(store.Writes, Is.EqualTo(1)); Assert.That(store.Commits, Is.Zero);
    }
    [Test]
    public async Task Missing_anchor_does_not_load_or_create_a_player()
    {
        var store = new FaultingPlayerStore { MissingAnchor = true };
        using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        Assert.ThrowsAsync<PlayerAnchorNotFoundException>(async () => await Session(registry).SignInAsync("guest:a"));
        Assert.That(store.Loads, Is.Zero); Assert.That(store.Writes, Is.Zero);
        Assert.That(registry.TryGet("guest:a"), Is.Null);
        await registry.RetireAllAsync();
    }
    [Test]
    public async Task Load_waits_for_anchor_barrier_before_reading_state()
    {
        var entered = PlayerExecutionHarness.Barrier(); var release = PlayerExecutionHarness.Barrier();
        var store = new FaultingPlayerStore { BeforeLock = async () => { entered.TrySetResult(true); await release.Task; } };
        using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var loading = Session(registry).SignInAsync("guest:a").AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(loading.IsCompleted, Is.False); Assert.That(store.Loads, Is.Zero);
        store.Gold = 120; store.Version = 4; release.TrySetResult(true);
        var player = (await loading).Player;
        Assert.That(player.Gold, Is.EqualTo(120));
        player.Gold = 110; player.MarkDirty(); await registry.SaveAsync(player);
        Assert.That((store.Gold, store.Version), Is.EqualTo((110L,5L)));
    }
    [Test]
    public async Task Same_scope_owns_sorted_anchors_receipt_and_audit()
    {
        var store = new FaultingPlayerStore();
        await using var scope = await store.OpenScopeAsync(["guest:a", "guest:b"], NullLogger.Instance, default);
        var id = Guid.NewGuid();
        Assert.That(await scope.ReadOperationAsync(id, default), Is.Null);
        await scope.LoadAsync("guest:a", default);
        await scope.WriteAsync(Save(audit: [new PlayerAuditRecord(id, "spent", DateTime.UtcNow, new byte[] { 1 })]), default);
        await scope.WriteOperationAsync(new PlayerOperationRecord(id, "spend", "guest:a", Keys, new byte[32], 1, new byte[] { 2 }), default);
        Assert.That((await scope.CommitAsync(default)).Outcome, Is.EqualTo(PlayerStoreOutcome.Committed));
        Assert.That(store.LockedKeys, Is.EqualTo(new[] { "guest:a", "guest:b" })); Assert.That(store.Audits, Is.EqualTo(1));
    }
    [Test]
    public async Task Audit_write_failure_rolls_back_state()
    {
        var store = new FaultingPlayerStore { AuditFailure = true };
        await using var scope = await store.OpenScopeAsync(Keys, NullLogger.Instance, default);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.WriteAsync(Save(audit: [new PlayerAuditRecord(Guid.NewGuid(), "spent", DateTime.UtcNow, new byte[] { 1 })]), default));
        Assert.That((await scope.RollbackAsync(error!)).Outcome, Is.EqualTo(PlayerStoreOutcome.RolledBack));
        Assert.That(store.Gold, Is.EqualTo(100));
    }
    [Test]
    public async Task Dirty_mark_during_save_survives_capture()
    {
        var store = new FaultingPlayerStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var player = (await Session(registry).SignInAsync("guest:a")).Player;
        player.Gold = 90; player.MarkDirty(); store.OnWrite = () => { player.Gold = 80; player.MarkDirty(); };
        await registry.SaveAsync(player); Assert.That(player.IsDirty, Is.True); Assert.That(store.Gold, Is.EqualTo(90));
        store.OnWrite = null; await registry.SaveAsync(player); Assert.That(player.IsDirty, Is.False); Assert.That((store.Gold,store.Version), Is.EqualTo((80L,5L)));
    }
    [Test]
    public void Records_copy_their_inputs()
    {
        var id = Guid.NewGuid(); var keys = new[] { "guest:a" }; var bytes = new byte[32];
        var receipt = new PlayerOperationRecord(id,"spend","guest:a",keys,bytes,1,bytes);
        var audit = new PlayerAuditRecord(id,"spent",DateTime.UtcNow,bytes);
        keys[0] = "changed"; bytes[0] = 9;
        Assert.That(receipt.ParticipantKeys[0], Is.EqualTo("guest:a"));
        Assert.That(receipt.InputFingerprint.Span[0], Is.Zero); Assert.That(receipt.ResultPayload.Span[0], Is.Zero); Assert.That(audit.Payload.Span[0], Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Unknown_save_discards_memory_without_retirement_retry(bool applied)
    {
        var store = new FaultingPlayerStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var session = Session(registry); var player = (await session.SignInAsync("guest:a")).Player;
        player.Gold = 90; player.MarkDirty();
        store.CommitFailure = applied ? FaultingPlayerStore.CommitFault.AfterApply : FaultingPlayerStore.CommitFault.BeforeApply;
        await registry.SaveAsync(player);
        Assert.That(registry.TryGet("guest:a"), Is.Null); Assert.That(player.IsValid, Is.False);
        await registry.HandleSessionClosedAsync(session); await registry.SaveAsync(player);
        Assert.That(store.Writes, Is.EqualTo(1)); Assert.That(player.RetirementCalls, Is.Zero);
        store.CommitFailure = FaultingPlayerStore.CommitFault.None;
        var reloaded = (await Session(registry).SignInAsync("guest:a")).Player;
        Assert.That(reloaded, Is.Not.SameAs(player)); Assert.That(reloaded.Gold, Is.EqualTo(applied ? 90 : 100));
        await registry.RetireAllAsync(); Assert.That(store.Writes, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Detach_and_retirement_save_through_store_only(bool retire)
    {
        var store = new FaultingPlayerStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var session = Session(registry); var player = (await session.SignInAsync("guest:a")).Player;
        player.Gold = 90; player.MarkDirty();
        if (retire) await registry.RetireAllAsync();
        else await registry.HandleSessionClosedAsync(session);
        Assert.That((store.Gold,store.Version), Is.EqualTo((90L,4L)));
        Assert.That(player.SaveCalls, Is.Zero); Assert.That(player.RetirementCalls, Is.Zero);
    }

    [Test]
    public async Task Periodic_save_uses_store_inside_posted_execution()
    {
        var store = new FaultingPlayerStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var transport = new FakeTransport(); var config = new PlayersConfig<ExecutionSession>();
        config.OnRequestUnauthenticated<LoginRequest,LoginResponse>((_, _) => new ValueTask<Reply<LoginResponse>>(new LoginResponse()));
        config.OnRequest<AddGoldRequest,AddGoldResponse>((_, _) => new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse()));
        config.OnRequest<GetGoldRequest,GetGoldResponse>((_, _) => new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse()));
        ExecutionSession? session = null;
        var server = new RpcServer<ExecutionSession,PlayersRequest,PlayersResponse,PlayersUpdate>(transport,
            registry.Wrap(config, c => session = new ExecutionSession(c)), config.Rpc);
        await server.StartAsync();
        try
        {
            transport.Connect(1);
            var player = (await session!.SignInAsync("guest:a")).Player;
            player.Gold = 90; player.MarkDirty(); player.NextSaveAtTicksUtc = 0;
            var done = PlayerExecutionHarness.Barrier();
            await new PlayerTicker<ExecutionPlayer>(registry).TickAsync(TimeSpan.Zero);
            Assert.That(session.Post(() => { done.TrySetResult(true); return default; }), Is.True);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That((store.Gold,store.Version), Is.EqualTo((90L,4L)));
            Assert.That(player.IsDirty, Is.False); Assert.That(player.SaveCalls, Is.Zero);
        }
        finally { await server.StopAsync(); await registry.RetireAllAsync(); }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void Acquisition_failure_retains_rollback_result_original_cause_and_cleanup(bool rollbackFails)
    {
        var original = new InvalidOperationException("anchor lock");
        var store = new FaultingPlayerStore { BeforeLock = () => throw original, RollbackFailure = rollbackFails };
        var failure = Assert.ThrowsAsync<PlayerStoreAcquisitionException>(async () => await store.OpenScopeAsync(Keys, NullLogger.Instance, default))!;
        Assert.That(failure.OriginalError, Is.SameAs(original));
        Assert.That(failure.Result.Outcome, Is.EqualTo(rollbackFails ? PlayerStoreOutcome.Unknown : PlayerStoreOutcome.RolledBack));
        Assert.That(failure.Phase, Is.EqualTo("AnchorLock"));
        if (rollbackFails)
        {
            var errors = ((AggregateException)failure.Result.Error!).InnerExceptions;
            Assert.That(errors[0], Is.SameAs(original));
            Assert.That(errors[1].Message, Does.Contain("rollback"));
        }
        else Assert.That(failure.Result.Error, Is.SameAs(original));
        Assert.That((store.Rollbacks, store.TransactionsDisposed, store.ConnectionsDisposed), Is.EqualTo((1, 1, 1)));
    }

    [TestCase("factory")]
    [TestCase("open")]
    [TestCase("begin")]
    public void Acquisition_without_transaction_ack_does_not_claim_rollback(string phase)
    {
        var original = new InvalidOperationException(phase);
        var store = new FaultingPlayerStore();
        if (phase == "factory") store.FactoryError = original;
        if (phase == "open") store.OpenError = original;
        if (phase == "begin") store.BeginError = original;
        var failure = Assert.ThrowsAsync<PlayerStoreAcquisitionException>(async () => await store.OpenScopeAsync(Keys, NullLogger.Instance, default))!;
        Assert.That(failure.OriginalError, Is.SameAs(original));
        Assert.That(failure.Result.Error, Is.SameAs(original));
        Assert.That(failure.Result.Outcome, Is.EqualTo(PlayerStoreOutcome.Unknown));
        Assert.That(store.Rollbacks, Is.Zero);
        Assert.That(store.ConnectionsDisposed, Is.EqualTo(phase == "factory" ? 0 : 1));
    }
}
