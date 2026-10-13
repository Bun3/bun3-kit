using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Tests.OperationsProtocol;
using NUnit.Framework;
using Bun3.Server.Items;
using Bun3.Server.Core;
namespace Bun3.Server.Tests;
[TestFixture]
public class PlayerOperationNotificationTests
{
    [Test]
    public async Task Target_callback_update_waits_for_commit_and_freezes_message()
    {
        await using var h = new PlayerOperationRpcHarness(); await h.Start();
        var a = await h.Login(1, "a"); var b = await h.Login(2, "b");
        var entered = PlayerExecutionHarness.Barrier(); var release = PlayerExecutionHarness.Barrier();
        h.Store.BeforeCommit = async () => { entered.TrySetResult(true); await release.Task; };
        var operation = new TransferOperation();
        operation.Applying = () =>
        {
            var message = new BalanceChanged { Balance = 90 };
            _ = h.Registry.TryGet("a")!.PushUpdateAsync(message);
            _ = h.Registry.TryGet("b")!.PushUpdateAsync(message);
            message.Balance = 999;
        };
        var running = h.Registry.ExecuteAsync(operation).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Assert.That(a.SentPackets, Is.Empty); Assert.That(b.SentPackets, Is.Empty); }
        finally { release.TrySetResult(true); }
        Assert.That((await running).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        foreach (var c in new[] { a, b })
        {
            Assert.That(c.SentPackets.TryDequeue(out var packet), Is.True);
            Assert.That(packet![0], Is.EqualTo(Channels.Update));
            Assert.That(OperationUpdate.Parser.ParseFrom(packet.AsSpan(1).ToArray()).BalanceChanged.Balance, Is.EqualTo(90));
        }
    }
    [Test]
    public async Task Invalid_update_in_actual_push_callback_aborts_operation()
    {
        await using var h = new PlayerOperationRpcHarness(); await h.Start(); await h.Login(1, "a");
        var operation = new TransferOperation { Applying = () =>
        {
            var catalog = new ItemCatalogBuilder<string>().Register("item", "Item").Build();
            var transaction = new Transaction();
            var inventory = new ItemInventory<object>(catalog, () => 1, _ => new object(), transaction: transaction,
                onApplied: changes => { _ = h.Registry.TryGet("a")!.PushUpdateAsync(new GetBalanceResponse()); });
            using var scope = transaction.Begin();
            inventory.TryAdd(catalog.GetRequired("item"), 1);
            scope.Commit();
        }};
        Assert.That((await h.Registry.ExecuteAsync(operation)).Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(h.Store.Rows["a"].Gold, Is.EqualTo(100));
        Assert.That(h.Store.Receipts, Is.Empty);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task Failed_or_unknown_commit_drops_frozen_updates(bool unknown)
    {
        await using var h = new PlayerOperationRpcHarness(); await h.Start();
        var a = await h.Login(1, "a"); var b = await h.Login(2, "b");
        h.Store.FailWriteAt = unknown ? 0 : 2;
        h.Store.LostCommitAck = unknown;
        var operation = new TransferOperation { Applying = () =>
        {
            _ = h.Registry.TryGet("a")!.PushUpdateAsync(new BalanceChanged { Balance = 90 });
            _ = h.Registry.TryGet("b")!.PushUpdateAsync(new BalanceChanged { Balance = 30 });
        }};
        var result = await h.Registry.ExecuteAsync(operation);
        Assert.That(result.Outcome, Is.EqualTo(unknown ? PlayerOperationOutcome.Unknown : PlayerOperationOutcome.Failed));
        Assert.That(a.SentPackets.Concat(b.SentPackets).Any(p => p[0] == Channels.Update), Is.False);
    }

    [Test]
    public async Task Delivery_error_does_not_invalidate_committed_players_and_waits_outside_ownership()
    {
        var log = new OperationLogger();
        await using var h = new PlayerOperationRpcHarness(log); await h.Start();
        var c = await h.Login(1, "a");
        var player = h.Registry.TryGet("a")!;
        var sendStarted = PlayerExecutionHarness.Barrier(); var sendRelease = PlayerExecutionHarness.Barrier();
        c.SendOverride = async packet =>
        {
            if (packet.Span[0] != Channels.Update) return;
            sendStarted.TrySetResult(true);
            await sendRelease.Task;
            throw new InvalidOperationException("delivery");
        };
        var operation = new TransferOperation { Applying = () => _ = player.PushUpdateAsync(new BalanceChanged { Balance = 90 }) };
        var running = h.Registry.ExecuteAsync(operation).AsTask();
        await sendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var replay = await h.Registry.ExecuteAsync(operation).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(replay.Outcome, Is.EqualTo(PlayerOperationOutcome.AlreadyCommitted));
        }
        finally { sendRelease.TrySetResult(true); }
        Assert.That((await running).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That(h.Registry.TryGet("a"), Is.SameAs(player));
        Assert.That(operation.ApplyCalls, Is.EqualTo(1));
        Assert.That(log.Stages, Does.Contain("DeliveryFailed"));
    }
    [Test]
    public async Task Caught_callback_encoding_failure_still_prevents_commit()
    {
        await using var h = new PlayerOperationRpcHarness(); await h.Start(); await h.Login(1, "a");
        var operation = new TransferOperation { Applying = () =>
        {
            try { _ = h.Registry.TryGet("a")!.PushUpdateAsync(new GetBalanceResponse()); }
            catch (InvalidOperationException) { }
        }};
        Assert.That((await h.Registry.ExecuteAsync(operation)).Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(h.Store.Receipts, Is.Empty);
    }
}
