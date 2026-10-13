using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture, Category("PostgreSQL")]
public class PostgresAuthorityTests
{
    private static PlayerWriteBinding Offline => new(PlayerWriteRole.OfflineAdministrator, Guid.Empty);
    [Test]
    public async Task Startup_waits_for_offline_transaction()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); var store = new PostgresOwnedPlayerStore(f, Offline);
        await using var scope = await store.OpenScopeAsync(new[] { "a" }, NullLogger.Instance, default);
        var attempted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapping = new PostgresAuthorityStore(f) { Attempting = (pid, _) => attempted.TrySetResult(pid) };
        var starting = new PlayerWriteAuthority(PostgresAuthorityFixture.Scope, mapping).TransitionAsync(PlayerWriteAuthorityTests.Start(Guid.NewGuid())).AsTask();
        try
        {
            await f.WaitBlockedAsync(await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.That(starting.IsCompleted, Is.False);
        }
        finally { await scope.CommitAsync(default); }
        Assert.That((await starting).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Committed));
    }
    [Test]
    public async Task Offline_transaction_denied_after_start_claim()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); await f.Authority.TransitionAsync(PlayerWriteAuthorityTests.Start(Guid.NewGuid()));
        using var registry = new PlayerRegistry<ExecutionPlayer>(new PostgresOwnedPlayerStore(f, Offline));
        var operation = new PlayerOperationActorTests.ActorOperation(new(PlayerOperationActorKind.Administrator, "admin"));
        Assert.That((await registry.ExecuteAsync(operation)).Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(operation.ApplyCalls, Is.Zero); Assert.That(await f.ScalarAsync("SELECT gold FROM players WHERE key='a'"), Is.EqualTo(100));
    }
    [Test]
    public async Task Unrelated_players_save_concurrently()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); var store = new PostgresOwnedPlayerStore(f, Offline);
        var first = await store.OpenScopeAsync(new[] { "a" }, NullLogger.Instance, default);
        var opening = store.OpenScopeAsync(new[] { "b" }, NullLogger.Instance, default).AsTask();
        try
        {
            var second = await opening.WaitAsync(TimeSpan.FromSeconds(5));
            await using (second)
            {
                await first.WriteAsync(new PlayerSaveData("a", null, 0, 107L, Array.Empty<PlayerAuditRecord>()), default);
                await second.WriteAsync(new PlayerSaveData("b", null, 0, 27L, Array.Empty<PlayerAuditRecord>()), default);
                Assert.That((await second.CommitAsync(default)).Outcome, Is.EqualTo(PlayerStoreOutcome.Committed));
                Assert.That((await first.CommitAsync(default)).Outcome, Is.EqualTo(PlayerStoreOutcome.Committed));
            }
        }
        finally { await first.DisposeAsync(); if (opening.IsCompletedSuccessfully) await opening.Result.DisposeAsync(); else { try { await (await opening).DisposeAsync(); } catch { } } }
        Assert.That(await f.ScalarAsync("SELECT sum(gold) FROM players WHERE scope_key='test-scope'"), Is.EqualTo(134));
        Assert.That(await f.ScalarAsync("SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='players' AND column_name='version'"), Is.Zero);
    }
    [Test]
    public async Task Two_starting_instances_have_one_winner()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync();
        var results = await Task.WhenAll(f.Authority.TransitionAsync(PlayerWriteAuthorityTests.Start(Guid.NewGuid())).AsTask(),
            f.Authority.TransitionAsync(PlayerWriteAuthorityTests.Start(Guid.NewGuid())).AsTask());
        Assert.That(results.Count(r => r.Outcome == PlayerWriteTransitionOutcome.Committed), Is.EqualTo(1));
        Assert.That(await f.ScalarAsync("SELECT count(*) FROM authority_audits"), Is.EqualTo(1));
    }
    [Test]
    public async Task Old_instance_write_after_restart_is_rejected()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); var oldStore = new PostgresOwnedPlayerStore(f, new(PlayerWriteRole.Server, Guid.NewGuid()));
        using var old = new PlayerRegistry<ExecutionPlayer>(oldStore); var lifecycle = new PlayerServerLifecycle<ExecutionPlayer>(old);
        await lifecycle.PrepareAsync(); await lifecycle.ActivateAsync();
        var stale = (await OwnedStoreHarness.Session(old).SignInAsync("a")).Player;
        await f.Authority.RecoverAsync(new(Guid.NewGuid(), oldStore.Binding.InstanceId, "operator", "old process stopped and restart disabled"));
        using (var admin = new PlayerRegistry<ExecutionPlayer>(new PostgresOwnedPlayerStore(f, Offline)))
            await admin.ExecuteAsync(new PlayerOperationActorTests.ActorOperation(new(PlayerOperationActorKind.Administrator, "admin")));
        var next = Guid.NewGuid(); await f.Authority.TransitionAsync(PlayerWriteAuthorityTests.Start(next));
        await f.Authority.TransitionAsync(PlayerWriteAuthorityTests.Change(PlayerWriteState.Starting, PlayerWriteState.Online, next));
        stale.Gold = 0; stale.MarkDirty(); await old.SaveAsync(stale); await old.AuthorityCleanup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(old.Players, Is.Empty); Assert.That(await f.ScalarAsync("SELECT gold FROM players WHERE key='a'"), Is.EqualTo(107));
    }
    [Test]
    public async Task Control_row_cannot_change_between_guard_and_commit()
    {
        await using var f = await PostgresAuthorityFixture.CreateAsync(); var store = new PostgresOwnedPlayerStore(f, Offline);
        await using var scope = await store.OpenScopeAsync(new[] { "a" }, NullLogger.Instance, default);
        await scope.WriteAsync(new("a", null, 0, 115L, Array.Empty<PlayerAuditRecord>()), default);
        var attempted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapping = new PostgresAuthorityStore(f) { Attempting = (pid, _) => attempted.TrySetResult(pid) };
        var transition = new PlayerWriteAuthority(PostgresAuthorityFixture.Scope, mapping).TransitionAsync(PlayerWriteAuthorityTests.Start(Guid.NewGuid())).AsTask();
        try { await f.WaitBlockedAsync(await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5))); Assert.That(transition.IsCompleted, Is.False); }
        finally { await scope.CommitAsync(default); }
        Assert.That((await transition).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Committed));
        Assert.That(await f.ScalarAsync("SELECT gold FROM players WHERE key='a'"), Is.EqualTo(115));
    }
}
