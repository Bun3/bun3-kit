using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;
using Bun3.Server.Tests.OperationsProtocol;
using Bun3.Server.Core;
using Bun3.Server.Items;
using Bun3.Server.Achievements;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerOperationAuditTests
{
    [Test]
    public async Task Legacy_mutation_scopes_reject_audit_without_buffering_or_marking_dirty()
    {
        await using var h = new PlayerExecutionHarness();
        var attempts = 0;
        void Record(ExecutionPlayer player)
        {
            attempts++;
            Assert.Throws<InvalidOperationException>(() => player.RecordAudit("unsupported", new byte[] { 1 }));
            Assert.That(player.CaptureAudit(), Is.Empty);
            Assert.That(player.IsDirty, Is.False);
        }
        h.ConfigurePlayer = player => player.Attached = () => { Record(player); return default; };
        await h.StartAsync();
        var connection = h.Transport.Connect(1);
        var login = await PlayerExecutionHarness.LoginAsync(connection);
        Assert.That(login.Status, Is.Zero);
        var player = h.Registry.TryGet("guest:a")!;
        h.Handle = session => { Record(session.Player!); return default; };
        var reply = await PlayerExecutionHarness.SendAsync(connection, new Bun3.Server.Tests.PlayersProtocol.PlayersRequest
        { AddGold = new Bun3.Server.Tests.PlayersProtocol.AddGoldRequest() });
        Assert.That(reply.Status, Is.Zero);
        var posted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.CurrentSession!.Post(() =>
        {
            try { Record(player); posted.TrySetResult(true); }
            catch (Exception ex) { posted.TrySetException(ex); }
            return default;
        });
        await posted.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(attempts, Is.EqualTo(3));
    }

    [Test]
    public async Task Post_apply_rollback_failure_preserves_both_errors_in_unknown_diagnostic()
    {
        var store = new FaultingPlayerStore(); var log = new OperationLogger();
        using var registry = new PlayerRegistry<ExecutionPlayer>(store, logger: log);
        var session = registry.Wrap(new PlayersConfig<ExecutionSession>(), c => new ExecutionSession(c))(new FakeConnection(1, new FakeTransport()));
        var player = (await session.SignInAsync("a")).Player;
        var original = new MappingException("write failure");
        store.OnWrite = () => throw original;
        store.RollbackFailure = true;
        log.Entries.Clear(); log.Errors.Clear();
        var result = await registry.ExecuteAsync(new TransferOperation());
        Assert.That(result.Outcome, Is.EqualTo(PlayerOperationOutcome.Unknown));
        Assert.That(player.IsValid, Is.False);
        var failureIndex = log.Entries.FindIndex(e => Equals(e.GetValueOrDefault("Stage"), "CommitUnknown"));
        Assert.That(failureIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(log.Entries[failureIndex]["DbErrorCode"], Is.EqualTo(917));
        Assert.That(log.Errors[failureIndex], Is.TypeOf<AggregateException>());
        var aggregate = (AggregateException)log.Errors[failureIndex]!;
        Assert.That(aggregate.InnerExceptions[0], Is.SameAs(original));
        Assert.That(aggregate.InnerExceptions[1].Message, Does.Contain("rollback"));
        var invalidationIndex = log.Entries.FindIndex(e => e.GetValueOrDefault("{OriginalFormat}") as string == "Player {AccountKey} invalidated");
        Assert.That(log.Errors[invalidationIndex], Is.SameAs(aggregate));
    }

    [Test]
    public async Task Audit_span_is_owned_and_saved_with_state()
    {
        using var h = new PlayerOperationHarness();
        var a = (await h.Login("a")).Player!;
        var bytes = new byte[] { 1, 2, 3 };
        var operation = new TransferOperation();
        operation.Applying = () => { a.RecordAudit("transfer", bytes); bytes[0] = 9; };
        Assert.That((await h.Registry.ExecuteAsync(operation)).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That(h.Store.Audits.Single().Payload.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(h.Store.Audits.Single().OperationId, Is.EqualTo(operation.Id));
        Assert.That(a.IsDirty, Is.False);
    }

    [Test]
    public async Task Audit_mapping_failure_rolls_back_state_and_receipt()
    {
        using var h = new PlayerOperationHarness();
        var a = (await h.Login("a")).Player!;
        h.Store.FailAudit = true;
        var operation = new TransferOperation { Applying = () => a.RecordAudit("transfer", new byte[] { 1 }) };
        Assert.That((await h.Registry.ExecuteAsync(operation)).Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(h.Store.Rows["a"].Gold, Is.EqualTo(100));
        Assert.That(h.Store.Receipts, Is.Empty);
        Assert.That(h.Registry.TryGet("a"), Is.Null);
    }

    [Test]
    public void Audit_outside_mutation_scope_is_rejected()
    {
        var player = new ExecutionPlayer();
        Assert.Throws<InvalidOperationException>(() => player.RecordAudit("invalid", Array.Empty<byte>()));
    }
    [Test]
    public async Task Throwing_diagnostic_sink_does_not_change_outcome()
    {
        var store = new OperationStore();
        using var registry = new PlayerRegistry<ExecutionPlayer>(store, logger: new OperationLogger { Throws = true });
        Assert.That((await registry.ExecuteAsync(new TransferOperation())).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        store.FailWriteAt = store.Writes + 1;
        Assert.That((await registry.ExecuteAsync(new TransferOperation())).Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(store.Loaded.All(p => p.ReleaseCalls == 1), Is.True);
    }

    [Test]
    public async Task Diagnostic_stages_correlate_participants_and_save_attempt()
    {
        var store = new OperationStore(); var log = new OperationLogger();
        using var registry = new PlayerRegistry<ExecutionPlayer>(store, logger: log);
        var operation = new TransferOperation();
        await registry.ExecuteAsync(operation);
        Assert.That(log.Stages, Does.Contain("Waiting"));
        Assert.That(log.Stages, Does.Contain("Loaded"));
        Assert.That(log.Stages, Does.Contain("MemoryApplied"));
        Assert.That(log.Stages, Does.Contain("Saving"));
        Assert.That(log.Stages, Does.Contain("Committed"));
        var stages = log.Entries.Where(e => e.ContainsKey("Stage")).ToArray();
        Assert.That(stages.All(e => Equals(e["OperationId"], operation.Id)), Is.True);
        Assert.That(stages.Select(e => e["SaveAttemptId"]).Distinct().Count(), Is.EqualTo(1));
        Assert.That(stages.Where(e => Equals(e["Stage"], "Loaded")).Select(e => e["AccountKey"]), Is.EquivalentTo(new[] { "a", "b" }));
    }
    [Test]
    public async Task Ordinary_request_post_tick_and_lifecycle_have_distinct_owned_audit_ids()
    {
        await using var h = new PlayerOperationRpcHarness();
        h.Store.ConfigurePlayer = p => p.Attached = () => { p.RecordAudit("attach", new byte[] { 1 }); return default; };
        await h.Start(); var c = await h.Login(1, "a");
        var player = h.Registry.TryGet("a")!;
        h.Reading = session => { session.Player!.RecordAudit("request", new byte[] { 2 }); return default; };
        await PlayerOperationRpcHarness.Send(c, new OperationRequest { GetBalance = new GetBalanceRequest() });
        var posted = PlayerExecutionHarness.Barrier();
        player.CurrentSession!.Post(() => { player.RecordAudit("post", new byte[] { 3 }); posted.TrySetResult(true); return default; });
        await posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var ticked = PlayerExecutionHarness.Barrier();
        player.Ticking = () => { player.RecordAudit("tick", new byte[] { 4 }); ticked.TrySetResult(true); return default; };
        await new PlayerTicker<ExecutionPlayer>(h.Registry).TickAsync(default);
        await ticked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        player.Detached = () => { player.RecordAudit("detach", new byte[] { 5 }); return default; };
        var session = (ExecutionSession)player.CurrentSession;
        c.Close(); await session.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(h.Store.Audits.Select(a => a.EventName), Is.EqualTo(new[] { "attach", "request", "post", "tick", "detach" }));
        Assert.That(h.Store.Audits.Select(a => a.OperationId).Distinct().Count(), Is.EqualTo(5));
        Assert.That(h.Store.Audits.All(a => a.OperationId != Guid.Empty), Is.True);
    }

    [Test]
    public async Task Domain_reactions_finish_before_capture_and_rollback_produces_no_audit()
    {
        await using var h = new PlayerOperationRpcHarness(); await h.Start(); await h.Login(1, "a");
        var player = h.Registry.TryGet("a")!;
        h.Applying = context =>
        {
            var transaction = new Transaction();
            var log = new ActionLog(batch => player.RecordAudit("domain", new byte[] { (byte)batch.Length }));
            var catalog = new ItemCatalogBuilder<string>().Register("item", "Item").Build();
            var achievements = new AchievementCatalog<AchievementDefinition>(new[] { new AchievementDefinition("acquire", 1) });
            var manager = new AchievementManager<AchievementDefinition>(achievements, transaction: transaction, log: log);
            manager.OnCompleted = (_, _, _) => { player.Gold += 50; player.MarkDirty(); };
            var inventory = new ItemInventory<object>(catalog, () => 1, _ => new object(), transaction: transaction, log: log,
                onApplied: _ => manager.Increase(achievements.GetRequired("acquire"), 1));
            using (transaction.Begin()) { inventory.TryAdd(catalog.GetRequired("item"), 1); }
            Assert.That(player.CaptureAudit(), Is.Empty);
            using (var scope = transaction.Begin()) { inventory.TryAdd(catalog.GetRequired("item"), 1); scope.Commit(); }
        };
        var c = (FakeConnection)player.CurrentSession!.Connection;
        var response = await PlayerOperationRpcHarness.Send(c, new OperationRequest { Transfer = new TransferRequest { Id = Guid.NewGuid().ToString(), Target = "b" } });
        Assert.That(response.Status, Is.Zero);
        Assert.That(h.Store.Rows["a"].Gold, Is.EqualTo(140));
        Assert.That(h.Store.Audits, Is.Not.Empty);
    }
    [Test]
    public async Task Validate_and_temporary_load_cannot_record_audit()
    {
        using var h = new PlayerOperationHarness();
        var operation = new TransferOperation { Validating = context => context.GetPlayer("a").RecordAudit("invalid", new byte[] { 1 }) };
        Assert.That((await h.Registry.ExecuteAsync(operation)).Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(operation.ApplyCalls, Is.Zero);
        Assert.That(h.Store.Audits, Is.Empty);
        h.Store.ConfigurePlayer = p => p.RecordAudit("load", new byte[] { 2 });
        Assert.That((await h.Registry.ExecuteAsync(new TransferOperation())).Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(h.Store.Audits, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Diagnostics_distinguish_rollback_from_unknown(bool unknown)
    {
        var store = new OperationStore { FailWriteAt = unknown ? 0 : 2, LostCommitAck = unknown };
        var log = new OperationLogger();
        using var registry = new PlayerRegistry<ExecutionPlayer>(store, logger: log);
        await registry.ExecuteAsync(new TransferOperation());
        Assert.That(log.Stages, Does.Contain(unknown ? "CommitUnknown" : "RolledBack"));
        Assert.That(log.Stages, Does.Not.Contain(unknown ? "RolledBack" : "CommitUnknown"));
        Assert.That(log.Stages.Count(s => s == "Invalidated"), Is.EqualTo(2));
    }
    private sealed class MappingException(string message) : System.Data.Common.DbException(message, 917) { }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Acquisition_rollback_diagnostics_preserve_actual_ack_and_existing_memory(bool rollbackFails)
    {
        var store = new FaultingPlayerStore(); var log = new OperationLogger();
        using var registry = new PlayerRegistry<ExecutionPlayer>(store, logger: log);
        var session = registry.Wrap(new PlayersConfig<ExecutionSession>(), c => new ExecutionSession(c))(new FakeConnection(1, new FakeTransport()));
        var player = (await session.SignInAsync("a")).Player;
        var original = new MappingException("anchor lock");
        store.BeforeLock = () => throw original;
        store.RollbackFailure = rollbackFails;
        log.Entries.Clear(); log.Errors.Clear();
        var operation = new TransferOperation();
        var result = await registry.ExecuteAsync(operation);
        Assert.That(result.Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(operation.ApplyCalls, Is.Zero);
        Assert.That(registry.TryGet("a"), Is.SameAs(player));
        var entry = log.Entries.Single(e => Equals(e.GetValueOrDefault("Stage"), rollbackFails ? "CommitUnknown" : "RolledBack"));
        Assert.That(entry["OperationId"], Is.EqualTo(operation.Id));
        Assert.That(entry["DbErrorCode"], Is.EqualTo(original.ErrorCode));
        Assert.That(entry["Phase"], Is.EqualTo("AnchorLock"));
        Assert.That(log.Stages, Does.Not.Contain(rollbackFails ? "RolledBack" : "CommitUnknown"));
        if (rollbackFails)
        {
            var aggregate = log.Errors.OfType<AggregateException>().Single();
            Assert.That(aggregate.InnerExceptions[0], Is.SameAs(original));
            Assert.That(aggregate.InnerExceptions[1].Message, Does.Contain("rollback"));
        }
        else Assert.That(log.Errors, Does.Contain(original));
    }

    [TestCase("factory", false)]
    [TestCase("open", false)]
    [TestCase("begin", false)]
    [TestCase("anchor", false)]
    [TestCase("anchor", true)]
    [TestCase("load", false)]
    [TestCase("load", true)]
    public void Login_failure_has_correlated_classified_error_and_preserves_original_exception(string phase, bool rollbackFails)
    {
        var store = new FaultingPlayerStore { RollbackFailure = rollbackFails };
        var original = new MappingException(phase);
        if (phase == "factory") store.FactoryError = original;
        if (phase == "open") store.OpenError = original;
        if (phase == "begin") store.BeginError = original;
        if (phase == "anchor") store.BeforeLock = () => throw original;
        if (phase == "load") store.LoadError = original;
        var log = new OperationLogger();
        using var registry = new PlayerRegistry<ExecutionPlayer>(store, logger: log);
        var session = registry.Wrap(new PlayersConfig<ExecutionSession>(), c => new ExecutionSession(c))(new FakeConnection(1, new FakeTransport()));
        var thrown = Assert.ThrowsAsync<MappingException>(async () => await session.SignInAsync("a"));
        Assert.That(thrown, Is.SameAs(original));
        var uncertain = rollbackFails || phase is "factory" or "open" or "begin";
        var waiting = log.Entries.Single(e => Equals(e.GetValueOrDefault("Stage"), "Waiting"));
        var failure = log.Entries.Single(e => Equals(e.GetValueOrDefault("Stage"), uncertain ? "CommitUnknown" : "RolledBack"));
        Assert.That(failure["OperationKind"], Is.EqualTo("load"));
        Assert.That(failure["OperationId"], Is.EqualTo(waiting["OperationId"]));
        Assert.That(failure["SaveAttemptId"], Is.EqualTo(waiting["SaveAttemptId"]));
        Assert.That(failure["DbErrorCode"], Is.EqualTo(original.ErrorCode));
        Assert.That(failure["ErrorType"], Is.Not.Null);
        Assert.That(failure["AccountKey"], Is.EqualTo("a"));
        Assert.That(failure["Phase"], Is.EqualTo(phase switch
        {
            "factory" => "ConnectionFactory", "open" => "ConnectionOpen", "begin" => "TransactionBegin",
            "anchor" => "AnchorLock", _ => "PlayerLoad"
        }));
        Assert.That(store.ConnectionsDisposed, Is.EqualTo(phase == "factory" ? 0 : 1));
        Assert.That(store.TransactionsDisposed, Is.EqualTo(phase is "anchor" or "load" ? 1 : 0));
        Assert.That(log.Stages, Does.Not.Contain(uncertain ? "RolledBack" : "CommitUnknown"));
        Assert.That(session.IsAuthenticated, Is.False);
        Assert.That(registry.Players, Is.Empty);
        if (rollbackFails) Assert.That(log.Errors.OfType<AggregateException>().Single().InnerExceptions[0], Is.SameAs(original));
        else Assert.That(log.Errors, Does.Contain(original));
    }

    [Test]
    public async Task Save_acquisition_rollback_failure_is_not_logged_as_confirmed_rollback()
    {
        var store = new FaultingPlayerStore(); var log = new OperationLogger();
        using var registry = new PlayerRegistry<ExecutionPlayer>(store, logger: log);
        var session = registry.Wrap(new PlayersConfig<ExecutionSession>(), c => new ExecutionSession(c))(new FakeConnection(1, new FakeTransport()));
        var player = (await session.SignInAsync("a")).Player;
        player.MarkDirty();
        var original = new MappingException("anchor");
        store.BeforeLock = () => throw original;
        store.RollbackFailure = true;
        log.Entries.Clear(); log.Errors.Clear();
        await registry.SaveAsync(player);
        Assert.That(log.Stages, Does.Contain("CommitUnknown"));
        Assert.That(log.Stages, Does.Not.Contain("RolledBack"));
        Assert.That(registry.TryGet("a"), Is.Null);
        Assert.That(store.Writes, Is.Zero);
    }
    [Test]
    public async Task Canceled_acquisition_with_unacknowledged_rollback_keeps_rejected_action_and_uncertain_diagnostic()
    {
        var store = new FaultingPlayerStore { RollbackFailure = true, BeforeLock = () => throw new OperationCanceledException() };
        var log = new OperationLogger();
        using var registry = new PlayerRegistry<ExecutionPlayer>(store, logger: log);
        var operation = new TransferOperation();
        Assert.That((await registry.ExecuteAsync(operation)).Outcome, Is.EqualTo(PlayerOperationOutcome.Rejected));
        Assert.That(operation.ApplyCalls, Is.Zero);
        Assert.That(log.Stages, Does.Contain("CommitUnknown"));
        Assert.That(log.Stages, Does.Not.Contain("RolledBack"));
        Assert.That(store.Writes, Is.Zero);
    }
}
