using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerWriteAuthorityTests
{
    internal static PlayerWriteTransition Start(Guid owner, Guid? id = null, string reason = "start") =>
        new(id ?? Guid.NewGuid(), PlayerWriteState.Offline, null, PlayerWriteState.Starting, owner, "server", reason);
    internal static PlayerWriteTransition Change(PlayerWriteState from, PlayerWriteState to, Guid owner) =>
        new(Guid.NewGuid(), from, owner, to, to == PlayerWriteState.Offline ? null : owner, "server", "lifecycle");

    [Test]
    public async Task Only_one_start_claim_commits()
    {
        var h = new AuthorityStoreHarness(); var a = h.Authority;
        var results = await Task.WhenAll(Task.Run(async () => await a.TransitionAsync(Start(Guid.NewGuid()))),
            Task.Run(async () => await a.TransitionAsync(Start(Guid.NewGuid()))));
        Assert.That(results.Count(r => r.Outcome == PlayerWriteTransitionOutcome.Committed), Is.EqualTo(1));
        Assert.That(h.Audits, Is.EqualTo(1));
    }
    [Test]
    public async Task Heartbeat_failure_never_changes_authority()
    {
        var h = new AuthorityStoreHarness(); var owner = Guid.NewGuid();
        h.Row = new(PlayerWriteState.Online, owner, Guid.NewGuid(), new byte[32]);
        await using var db = h.NewConnection(); await db.OpenAsync(); await using var tx = await db.BeginTransactionAsync();
        Assert.ThrowsAsync<PlayerWriteAuthorityException>(async () => await h.Authority.ProtectAsync(db, tx,
            new(PlayerWriteRole.OfflineAdministrator, Guid.Empty), PlayerStoreAccess.Operation, default));
        Assert.That(h.Row.State, Is.EqualTo(PlayerWriteState.Online)); Assert.That(h.Writes, Is.Zero);
    }
    [Test]
    public async Task Transition_retry_preserves_later_owner()
    {
        var h = new AuthorityStoreHarness(); var a = h.Authority; var oldOwner = Guid.NewGuid(); var start = Start(oldOwner);
        Assert.That((await a.TransitionAsync(start)).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Committed));
        Assert.That((await a.TransitionAsync(start)).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Committed));
        await a.TransitionAsync(Change(PlayerWriteState.Starting, PlayerWriteState.Online, oldOwner));
        await a.TransitionAsync(Change(PlayerWriteState.Online, PlayerWriteState.Draining, oldOwner));
        await a.TransitionAsync(Change(PlayerWriteState.Draining, PlayerWriteState.Offline, oldOwner));
        var newOwner = Guid.NewGuid(); await a.TransitionAsync(Start(newOwner));
        Assert.That((await a.TransitionAsync(start)).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Conflict));
        Assert.That((await a.ReadAsync()).InstanceId, Is.EqualTo(newOwner));
    }
    [Test]
    public async Task Transition_id_reuse_with_changed_reason_conflicts()
    {
        var h = new AuthorityStoreHarness(); var owner = Guid.NewGuid(); var id = Guid.NewGuid();
        await h.Authority.TransitionAsync(Start(owner, id));
        Assert.That((await h.Authority.TransitionAsync(Start(owner, id, "changed"))).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Conflict));
        Assert.That(h.Writes, Is.EqualTo(1));
    }
    [Test]
    public async Task Missing_control_row_denies_access()
    {
        var h = new AuthorityStoreHarness { Row = null };
        await using var db = h.NewConnection(); await db.OpenAsync(); await using var tx = await db.BeginTransactionAsync();
        Assert.ThrowsAsync<PlayerWriteAuthorityException>(async () => await h.Authority.ProtectAsync(db, tx,
            new(PlayerWriteRole.OfflineAdministrator, Guid.Empty), PlayerStoreAccess.Operation, default));
    }
    [Test]
    public async Task Shared_guard_uses_the_supplied_transaction()
    {
        var h = new AuthorityStoreHarness();
        await using var db = h.NewConnection(); await db.OpenAsync(); await using var tx = await db.BeginTransactionAsync();
        await h.Authority.ProtectAsync(db, tx, new(PlayerWriteRole.OfflineAdministrator, Guid.Empty), PlayerStoreAccess.Operation, default);
        Assert.That(h.ObservedTransaction, Is.SameAs(tx)); Assert.That(h.ObservedLock, Is.EqualTo(PlayerWriteLock.Shared));
        Assert.That(h.Connections, Is.Zero); Assert.That(h.Gate.CurrentCount, Is.Zero);
    }
    [Test]
    public void Failed_audit_rolls_back_transition()
    {
        var h = new AuthorityStoreHarness { FailAudit = true };
        Assert.ThrowsAsync<InvalidOperationException>(async () => await h.Authority.TransitionAsync(Start(Guid.NewGuid())));
        Assert.That(h.Row!.State, Is.EqualTo(PlayerWriteState.Offline)); Assert.That(h.Audits, Is.Zero);
        Assert.That(h.Gate.CurrentCount, Is.EqualTo(1));
    }
    [Test]
    public async Task Commit_ack_loss_is_unknown_and_same_transition_can_be_confirmed()
    {
        var h = new AuthorityStoreHarness { LoseCommitAck = true }; var start = Start(Guid.NewGuid());
        var result = await h.Authority.TransitionAsync(start);
        Assert.That(result.Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Unknown));
        h.LoseCommitAck = false;
        Assert.That((await h.Authority.TransitionAsync(start)).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Committed));
        Assert.That(h.Audits, Is.EqualTo(1));
    }
    [TestCase(PlayerWriteState.Starting, PlayerStoreAccess.Load, true)]
    [TestCase(PlayerWriteState.Starting, PlayerStoreAccess.Save, false)]
    [TestCase(PlayerWriteState.Online, PlayerStoreAccess.Operation, true)]
    [TestCase(PlayerWriteState.Draining, PlayerStoreAccess.Load, false)]
    [TestCase(PlayerWriteState.Draining, PlayerStoreAccess.Save, true)]
    [TestCase(PlayerWriteState.Draining, PlayerStoreAccess.Operation, true)]
    [TestCase(PlayerWriteState.RecoveryRequired, PlayerStoreAccess.Recovery, false)]
    public async Task Server_guard_checks_state_and_instance(PlayerWriteState state, int accessValue, bool allowed)
    {
        var access = (PlayerStoreAccess)accessValue; var owner = Guid.NewGuid();
        var h = new AuthorityStoreHarness { Row = new(state, owner, Guid.NewGuid(), new byte[32]) };
        await using var db = h.NewConnection(); await db.OpenAsync(); await using var tx = await db.BeginTransactionAsync();
        if (allowed) await h.Authority.ProtectAsync(db, tx, new(PlayerWriteRole.Server, owner), access, default);
        else Assert.ThrowsAsync<PlayerWriteAuthorityException>(async () => await h.Authority.ProtectAsync(db, tx, new(PlayerWriteRole.Server, owner), access, default));
        Assert.ThrowsAsync<PlayerWriteAuthorityException>(async () => await h.Authority.ProtectAsync(db, tx, new(PlayerWriteRole.Server, Guid.NewGuid()), access, default));
    }
    [Test]
    public async Task Invalid_edge_does_not_write()
    {
        var h = new AuthorityStoreHarness();
        var transition = new PlayerWriteTransition(Guid.NewGuid(), PlayerWriteState.Offline, null, PlayerWriteState.Online, Guid.NewGuid(), "server", "invalid");
        Assert.That((await h.Authority.TransitionAsync(transition)).Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Conflict));
        Assert.That(h.Writes, Is.Zero);
    }
    [Test]
    public async Task Recovery_verification_failure_keeps_recovery_required()
    {
        var owner = Guid.NewGuid();
        var h = new AuthorityStoreHarness { Row = new(PlayerWriteState.RecoveryRequired, owner, Guid.NewGuid(), new byte[32]), FailRecovery = true };
        Assert.ThrowsAsync<InvalidOperationException>(async () => await h.Authority.TransitionAsync(Change(PlayerWriteState.RecoveryRequired, PlayerWriteState.Offline, owner)));
        Assert.That(h.Row!.State, Is.EqualTo(PlayerWriteState.RecoveryRequired));
        Assert.That(h.RecoveryChecks, Is.EqualTo(1)); Assert.That(h.Writes, Is.Zero);
        await using var db = h.NewConnection(); await db.OpenAsync(); await using var tx = await db.BeginTransactionAsync();
        Assert.ThrowsAsync<ArgumentException>(async () => await h.Authority.ProtectAsync(db, tx, default, PlayerStoreAccess.Operation, default));
    }
}
