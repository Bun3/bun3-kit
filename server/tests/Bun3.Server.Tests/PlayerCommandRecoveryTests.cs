using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerCommandRecoveryTests
{
    [Test]
    public async Task Existing_receipt_cannot_be_downgraded_to_failed_by_an_unsupported_codec()
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        h.Store.Rows["a"] = 107;
        h.Store.Receipts.Add(command.Id, new PlayerOperationRecord(command.Id, command.Kind, command.Actor, command.ParticipantKeys,
            command.InputFingerprint, 2, new byte[] { 1, 2, 3 }));
        var result = (await h.Dispatcher.ExecuteAsync(command.Id))!;
        Assert.That(result.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        Assert.That(result.ResultSchemaVersion, Is.EqualTo(2)); Assert.That(h.Operations[0].Applies, Is.Zero);
        Assert.That(h.Store.Rows["a"], Is.EqualTo(107));
    }
    [TestCase("none")]
    [TestCase("player")]
    [TestCase("receipt")]
    [TestCase("command")]
    public async Task Success_receipt_command_and_players_commit_together(string fault)
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        h.Store.FailWrite = fault == "player"; h.Store.FailReceipt = fault == "receipt";
        h.Commands.BeforeComplete = r => r.Status == PlayerCommandStatus.Committed && fault == "command"
            ? throw new InvalidOperationException("command result failed") : default;
        var result = (await h.Dispatcher.ExecuteAsync(command.Id))!;
        Assert.That(result.Status, Is.EqualTo(fault == "none" ? PlayerCommandStatus.Committed : PlayerCommandStatus.Failed));
        Assert.That(h.Store.Rows["a"], Is.EqualTo(fault == "none" ? 107 : 100));
        Assert.That(h.Store.Receipts.Count, Is.EqualTo(fault == "none" ? 1 : 0));
        Assert.That((await h.Queue.GetAsync(command.Id))!.Status, Is.EqualTo(result.Status));
    }
    [Test]
    public async Task Two_workers_apply_a_command_once()
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        using var otherRegistry = new PlayerRegistry<ExecutionPlayer>(h.Store);
        var other = new PlayerCommandDispatcher<ExecutionPlayer>(h.Queue, h.Catalog, otherRegistry);
        var results = await Task.WhenAll(h.Dispatcher.ExecuteAsync(command.Id).AsTask(), other.ExecuteAsync(command.Id).AsTask());
        Assert.That(results.All(r => r!.Status == PlayerCommandStatus.Committed), Is.True);
        Assert.That(h.Store.Rows["a"], Is.EqualTo(107)); Assert.That(h.Operations.Sum(o => o.Applies), Is.EqualTo(1));
    }
    [Test]
    public async Task Commit_ack_loss_replays_stored_result()
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        h.Factory = e => new CommandGrant(e) { Applying = () => h.Store.Control.LoseCommitAck = true };
        var result = (await h.Dispatcher.ExecuteAsync(command.Id))!;
        Assert.That(result.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        h.Store.Control.LoseCommitAck = false;
        var replay = (await h.Dispatcher.ExecuteAsync(command.Id))!;
        Assert.That(replay.ResultPayload.ToArray(), Is.EqualTo(result.ResultPayload.ToArray()));
        Assert.That(h.Store.Rows["a"], Is.EqualTo(107)); Assert.That(h.Operations.Sum(o => o.Applies), Is.EqualTo(1));
        Assert.That(h.Store.Loaded.All(p => !p.IsValid), Is.True);
    }
    [Test]
    public async Task Failure_recording_observes_competing_terminal_result()
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        using var otherRegistry = new PlayerRegistry<ExecutionPlayer>(h.Store);
        var other = new PlayerCommandDispatcher<ExecutionPlayer>(h.Queue, h.Catalog, otherRegistry);
        var competed = false; h.Store.FailWrite = true;
        h.Store.Control.BeforeLock = async () =>
        {
            if (h.Store.Writes == 0 || competed) return;
            competed = true; h.Store.FailWrite = false;
            Task<PlayerCommandRecord?> competing;
            using (ExecutionContext.SuppressFlow()) competing = Task.Run(async () => await other.ExecuteAsync(command.Id));
            Assert.That((await competing)!.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        };
        var result = (await h.Dispatcher.ExecuteAsync(command.Id))!;
        Assert.That(competed, Is.True); Assert.That(result.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        Assert.That(h.Store.Rows["a"], Is.EqualTo(107)); Assert.That(h.Store.Receipts, Has.Count.EqualTo(1));
    }
    [Test]
    public async Task Canceled_before_apply_leaves_pending()
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        using var ct = new CancellationTokenSource(); ct.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await h.Dispatcher.ExecuteAsync(command.Id, ct.Token));
        Assert.That((await h.Queue.GetAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Pending));
        Assert.That(h.Store.Writes, Is.Zero);
    }
    [Test]
    public async Task Cancellation_after_apply_does_not_abandon_persistence()
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        using var ct = new CancellationTokenSource(); h.Factory = e => new CommandGrant(e) { Applying = ct.Cancel };
        Assert.That((await h.Dispatcher.ExecuteAsync(command.Id, ct.Token))!.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        Assert.That(h.Store.Rows["a"], Is.EqualTo(107));
    }
    [Test]
    public async Task Validation_rejection_is_durable_without_player_writes()
    {
        using var h = new CommandHarness(); h.Factory = e => new CommandGrant(e) { ValidationStatus = -42 };
        var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        Assert.That((await h.Dispatcher.ExecuteAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Rejected));
        Assert.That((await h.Queue.GetAsync(command.Id))!.StatusCode, Is.EqualTo(-42));
        Assert.That(h.Store.Writes, Is.Zero); Assert.That(h.Store.Receipts, Is.Empty);
    }
}
