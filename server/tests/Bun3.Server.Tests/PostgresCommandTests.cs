using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture, Category("PostgreSQL")]
public class PostgresCommandTests
{
    private static PostgresOwnedPlayerStore Store(PostgresAuthorityFixture f) => new(f, new(PlayerWriteRole.OfflineAdministrator, Guid.Empty));
    private static PlayerCommandCatalog<ExecutionPlayer> Catalog(Action? applying = null)
    { var catalog = new PlayerCommandCatalog<ExecutionPlayer>(); catalog.Register("grant", 1, true, e => new CommandGrant(e) { Audit = true, Applying = applying }); return catalog; }
    [Test]
    public async Task Candidate_filter_is_applied_before_the_batch_limit()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); var queue = new PlayerCommandQueue(new PostgresCommandStore(f));
        var unsupported = CommandHarness.Envelope(schema: 2); var supported = CommandHarness.Envelope();
        await queue.SubmitAsync(unsupported); await queue.SubmitAsync(supported);
        var candidates = await queue.FindPendingAsync("test-scope", 1, Catalog().GetSupportedInputs(true));
        Assert.That(candidates, Is.EqualTo(new[] { supported.Id }));
        Assert.That((await queue.GetAsync(unsupported.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Pending));
    }
    [Test]
    public async Task Two_offline_workers_commit_one_effect()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); var queue = new PlayerCommandQueue(new PostgresCommandStore(f));
        var command = CommandHarness.Envelope(); await queue.SubmitAsync(command);
        using var first = new PlayerRegistry<ExecutionPlayer>(Store(f)); using var second = new PlayerRegistry<ExecutionPlayer>(Store(f));
        var applies = 0; var catalog = Catalog(() => Interlocked.Increment(ref applies));
        var results = await Task.WhenAll(new PlayerCommandDispatcher<ExecutionPlayer>(queue, catalog, first).ExecuteAsync(command.Id).AsTask(),
            new PlayerCommandDispatcher<ExecutionPlayer>(queue, catalog, second).ExecuteAsync(command.Id).AsTask());
        Assert.That(results.All(r => r!.Status == PlayerCommandStatus.Committed), Is.True); Assert.That(applies, Is.EqualTo(1));
        Assert.That(await f.ScalarAsync("SELECT gold FROM players WHERE key='a'"), Is.EqualTo(107));
        Assert.That(await f.ScalarAsync("SELECT count(*) FROM operations WHERE id=@id", ("id", command.Id)), Is.EqualTo(1));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task Player_receipt_command_audit_are_atomic(bool fail)
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); var store = Store(f);
        var commands = new PostgresCommandStore(f) { FailCompletion = fail }; var queue = new PlayerCommandQueue(commands);
        var command = CommandHarness.Envelope(); await queue.SubmitAsync(command);
        using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var result = await new PlayerCommandDispatcher<ExecutionPlayer>(queue, Catalog(), registry).ExecuteAsync(command.Id);
        Assert.That(result!.Status, Is.EqualTo(fail ? PlayerCommandStatus.Failed : PlayerCommandStatus.Committed));
        Assert.That(await f.ScalarAsync("SELECT gold FROM players WHERE key='a'"), Is.EqualTo(fail ? 100 : 107));
        Assert.That(await f.ScalarAsync("SELECT count(*) FROM operations"), Is.EqualTo(fail ? 0 : 1));
        Assert.That(await f.ScalarAsync("SELECT count(*) FROM audits"), Is.EqualTo(fail ? 0 : 1));
        Assert.That((await queue.GetAsync(command.Id))!.Status, Is.EqualTo(result.Status));
    }
    [Test]
    public async Task Lost_real_commit_acknowledgement_reuses_the_durable_result()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); var queue = new PlayerCommandQueue(new PostgresCommandStore(f));
        var command = CommandHarness.Envelope(); await queue.SubmitAsync(command);
        var store = Store(f); store.LoseAcknowledgement = true; using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var applies = 0; var dispatcher = new PlayerCommandDispatcher<ExecutionPlayer>(queue, Catalog(() => applies++), registry);
        Assert.That((await dispatcher.ExecuteAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        Assert.That((await dispatcher.ExecuteAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        Assert.That(applies, Is.EqualTo(1)); Assert.That(await f.ScalarAsync("SELECT gold FROM players WHERE key='a'"), Is.EqualTo(107));
    }
    [Test]
    public async Task Unknown_commit_resolution_waits_for_previous_writer()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); var queue = new PlayerCommandQueue(new PostgresCommandStore(f));
        var command = CommandHarness.Envelope(); await queue.SubmitAsync(command);
        var release = PlayerExecutionHarness.Barrier(); var attempting = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = Store(f); store.DelayedCommit = release.Task; var locks = 0;
        store.Attempting = pid => { if (Interlocked.Increment(ref locks) == 2) attempting.TrySetResult(pid); };
        using var registry = new PlayerRegistry<ExecutionPlayer>(store); var applies = 0;
        var executing = new PlayerCommandDispatcher<ExecutionPlayer>(queue, Catalog(() => applies++), registry).ExecuteAsync(command.Id).AsTask();
        try
        {
            await f.WaitBlockedAsync(await attempting.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(executing.IsCompleted, Is.False); Assert.That(applies, Is.EqualTo(1));
        }
        finally { release.TrySetResult(true); }
        Assert.That((await executing)!.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        await store.FaultConnection!.PendingCommit.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(await f.ScalarAsync("SELECT gold FROM players WHERE key='a'"), Is.EqualTo(107));
    }
}
