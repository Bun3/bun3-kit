using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;
using static Bun3.Server.Tests.Helpers.PlayerExecutionHarness;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerOperationRecoveryTests
{
    [Test]
    public async Task Offline_receipt_replay_releases_resolved_recovery_gates()
    {
        using var h = new PlayerOperationHarness();
        h.Store.LostCommitAck = true;
        var operation = new TransferOperation();
        Assert.That((await h.Registry.ExecuteAsync(operation)).Outcome, Is.EqualTo(PlayerOperationOutcome.Unknown));
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var table = typeof(PlayerRegistry<ExecutionPlayer>).GetField("_execution", flags)!.GetValue(h.Registry)!;
        var gates = (System.Collections.IDictionary)typeof(PlayerExecutionTable).GetField("_gates", flags)!.GetValue(table)!;
        Assert.That(gates.Count, Is.EqualTo(2), "unresolved participants retain recovery ownership");
        h.Store.LostCommitAck = false;
        Assert.That((await h.Registry.ExecuteAsync(new TransferOperation(operation.Id, kind: "different"))).Status,
            Is.EqualTo(RpcStatus.OperationConflict));
        Assert.That(gates.Count, Is.EqualTo(2), "a conflicting receipt does not resolve this operation");

        var replay = await h.Registry.ExecuteAsync(operation);

        Assert.That(replay.Outcome, Is.EqualTo(PlayerOperationOutcome.AlreadyCommitted));
        Assert.That(replay.Value!.SenderBalance, Is.EqualTo(90));
        Assert.That(operation.ApplyCalls, Is.EqualTo(1));
        Assert.That(h.Store.Writes, Is.EqualTo(2));
        Assert.That(h.Registry.Players, Is.Empty);
        Assert.That(gates.Count, Is.Zero, "confirmed offline recovery must not retain keys indefinitely");
        Assert.That((await h.Registry.ExecuteAsync(new TransferOperation())).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That(gates.Count, Is.Zero);
    }

    [Test]
    public void Stop_rejects_new_login_without_loading()
    {
        using var h = new PlayerOperationHarness();
        var session = h.NewSession();
        h.Registry.StopAcceptingOperations();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await session.SignInAsync("a"));
        Assert.That(h.Store.Loaded, Is.Empty);
        Assert.That(session.IsAuthenticated, Is.False);
        Assert.That(h.Registry.Players, Is.Empty);
    }

    [Test]
    public async Task Stop_rejects_cached_rebind_without_displacing_current_session()
    {
        using var h = new PlayerOperationHarness();
        var current = await h.Login("a");
        var replacement = h.NewSession();
        h.Registry.StopAcceptingOperations();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await replacement.SignInAsync("a"));
        Assert.That(h.Registry.TryGet("a"), Is.SameAs(current.Player));
        Assert.That(current.IsAuthenticated, Is.True);
        Assert.That(replacement.IsAuthenticated, Is.False);
        Assert.That(h.Store.Loaded.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Stop_during_login_load_releases_state_without_cache_or_binding()
    {
        using var h = new PlayerOperationHarness();
        var entered = Barrier(); var proceed = Barrier();
        h.Store.BeforeLoad = async _ => { entered.TrySetResult(true); await proceed.Task; };
        var session = h.NewSession();
        var login = session.SignInAsync("a").AsTask();
        await entered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        h.Registry.StopAcceptingOperations();
        proceed.TrySetResult(true);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await login.WaitAsync(PlayerExecutionHarness.Timeout));
        Assert.That(session.IsAuthenticated, Is.False);
        Assert.That(h.Registry.Players, Is.Empty);
        Assert.That(h.Store.Loaded.Count, Is.EqualTo(1));
        Assert.That(h.Store.Loaded[0].ReleaseCalls, Is.EqualTo(1));
        Assert.That(h.Store.Loaded[0].IsValid, Is.False);
        Assert.That(h.Store.Loaded[0].CurrentSession, Is.Null);
    }

    [Test]
    public async Task Canceled_retirement_wait_does_not_abandon_active_writer()
    {
        using var h = new PlayerOperationHarness();
        using var cancel = new CancellationTokenSource();
        var entered = Barrier(); var proceed = Barrier();
        h.Store.BeforeCommit = async () => { entered.TrySetResult(true); await proceed.Task; };
        var running = h.Registry.ExecuteAsync(new TransferOperation()).AsTask();
        await entered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        var retiring = h.Registry.RetireAllAsync(cancel.Token).AsTask();
        cancel.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await retiring.WaitAsync(PlayerExecutionHarness.Timeout));
        Assert.That(running.IsCompleted, Is.False);
        var retryRetirement = h.Registry.RetireAllAsync().AsTask();
        Assert.That(retryRetirement.IsCompleted, Is.False);
        proceed.TrySetResult(true);
        Assert.That((await running.WaitAsync(PlayerExecutionHarness.Timeout)).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        await retryRetirement.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(h.Store.WriteCommits, Is.EqualTo(1));
    }

    [TestCase("load")]
    [TestCase("apply")]
    [TestCase("commit")]
    public async Task Login_during_offline_operation_waits(string stage)
    {
        using var h = new PlayerOperationHarness();
        var entered = Barrier(); var proceed = Barrier();
        var op = new TransferOperation();
        if (stage == "load") h.Store.BeforeLoad = async key => { if (key == "b") { entered.TrySetResult(true); await proceed.Task; } };
        if (stage == "apply") op.Applying = () => { entered.TrySetResult(true); proceed.Task.Wait(PlayerExecutionHarness.Timeout); };
        if (stage == "commit") h.Store.BeforeCommit = async () => { entered.TrySetResult(true); await proceed.Task; };
        var operation = Task.Run(async () => await h.Registry.ExecuteAsync(op));
        await entered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        var login = h.Login("b");
        Assert.That(login.IsCompleted, Is.False);
        proceed.TrySetResult(true);
        Assert.That((await operation.WaitAsync(PlayerExecutionHarness.Timeout)).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        var session = await login.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(session.Player!.Gold, Is.EqualTo(30));
        Assert.That(h.Store.Loaded.Count(p => p.AccountKey == "b"), Is.EqualTo(2));
    }

    [Test]
    public async Task Opposite_participant_order_and_disjoint_work()
    {
        using var h = new PlayerOperationHarness();
        var entered = Barrier(); var proceed = Barrier();
        var first = new TransferOperation();
        first.Applying = () => { entered.TrySetResult(true); Assert.That(proceed.Task.Wait(PlayerExecutionHarness.Timeout), Is.True); };
        var running = Task.Run(async () => await h.Registry.ExecuteAsync(first));
        await entered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        var opposite = new TransferOperation(keys: new[] { "b", "a", "a" });
        var queued = h.Registry.ExecuteAsync(opposite).AsTask();
        Assert.That(queued.IsCompleted, Is.False);
        var disjoint = new TransferOperation(actor: "c", keys: new[] { "d", "c" });
        Assert.That((await h.Registry.ExecuteAsync(disjoint).AsTask().WaitAsync(PlayerExecutionHarness.Timeout)).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That(opposite.ApplyCalls, Is.Zero);
        proceed.TrySetResult(true);
        await running.WaitAsync(PlayerExecutionHarness.Timeout); await queued.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(h.Store.Rows["a"].Gold, Is.EqualTo(80));
        Assert.That(h.Store.Rows["b"].Gold, Is.EqualTo(40));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Lost_commit_ack_blocks_reuse_until_database_settles(bool commitApplied)
    {
        using var h = new PlayerOperationHarness();
        var old = (await h.Login("a")).Player!;
        h.Store.LostCommitAck = commitApplied;
        h.Store.FailCommitBeforeApply = !commitApplied;
        var op = new TransferOperation();
        var unknown = await h.Registry.ExecuteAsync(op);
        Assert.That(unknown.Outcome, Is.EqualTo(PlayerOperationOutcome.Unknown));
        Assert.That(unknown.Status, Is.EqualTo(RpcStatus.OutcomeUnknown));
        Assert.That(h.Registry.TryGet("a"), Is.Null);
        Assert.That(old.ReleaseCalls, Is.EqualTo(1));
        Assert.That(h.Store.Rows["a"], Is.EqualTo(commitApplied ? (90L, 4L) : (100L, 3L)));
        Assert.That(h.Store.Rows["b"], Is.EqualTo(commitApplied ? (30L, 9L) : (20L, 8L)));
        Assert.That(h.Store.Receipts.ContainsKey(op.Id), Is.EqualTo(commitApplied));
        h.Store.LostCommitAck = false;
        h.Store.FailCommitBeforeApply = false;
        var entered = Barrier(); var proceed = Barrier();
        var retryEntered = Barrier(); var retryProceed = Barrier();
        var anchorCalls = 0;
        h.Store.BeforeLock = async _ =>
        {
            if (Interlocked.Increment(ref anchorCalls) == 1) { entered.TrySetResult(true); await proceed.Task; }
            else { retryEntered.TrySetResult(true); await retryProceed.Task; }
        };
        var login = h.Login("a");
        await entered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        var retry = h.Registry.ExecuteAsync(op).AsTask();
        Assert.That(login.IsCompleted || retry.IsCompleted, Is.False);
        Assert.That(op.ApplyCalls, Is.EqualTo(1));
        proceed.TrySetResult(true);
        Assert.That((await login.WaitAsync(PlayerExecutionHarness.Timeout)).Player!.Gold, Is.EqualTo(commitApplied ? 90 : 100));
        await retryEntered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(retry.IsCompleted, Is.False);
        Assert.That(op.ApplyCalls, Is.EqualTo(1));
        Assert.That(h.Store.Rows["a"], Is.EqualTo(commitApplied ? (90L, 4L) : (100L, 3L)));
        Assert.That(h.Store.Rows["b"], Is.EqualTo(commitApplied ? (30L, 9L) : (20L, 8L)));
        retryProceed.TrySetResult(true);
        Assert.That((await retry.WaitAsync(PlayerExecutionHarness.Timeout)).Outcome,
            Is.EqualTo(commitApplied ? PlayerOperationOutcome.AlreadyCommitted : PlayerOperationOutcome.Committed));
        Assert.That(op.ApplyCalls, Is.EqualTo(commitApplied ? 1 : 2));
        Assert.That(h.Store.Rows["a"], Is.EqualTo((90L, 4L)));
        Assert.That(h.Store.Rows["b"], Is.EqualTo((30L, 9L)));
        Assert.That(h.Store.Receipts.Count, Is.EqualTo(1));
        Assert.That((await h.Registry.ExecuteAsync(op)).Outcome, Is.EqualTo(PlayerOperationOutcome.AlreadyCommitted));
        Assert.That(op.ApplyCalls, Is.EqualTo(commitApplied ? 1 : 2));
    }

    [Test]
    public async Task Cancellation_after_apply_does_not_abandon_save()
    {
        using var h = new PlayerOperationHarness();
        using var cancel = new CancellationTokenSource();
        var entered = Barrier(); var proceed = Barrier();
        h.Store.BeforeCommit = async () => { entered.TrySetResult(true); await proceed.Task; };
        h.Store.OnWrite = cancel.Cancel;
        var op = new TransferOperation();
        var running = h.Registry.ExecuteAsync(op, cancel.Token).AsTask();
        await entered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(cancel.IsCancellationRequested, Is.True);
        Assert.That(running.IsCompleted, Is.False);
        var next = h.Registry.ExecuteAsync(new TransferOperation()).AsTask();
        Assert.That(next.IsCompleted, Is.False);
        proceed.TrySetResult(true);
        Assert.That((await running.WaitAsync(PlayerExecutionHarness.Timeout)).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        await next.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(h.Store.Rows["a"].Gold, Is.EqualTo(80));
    }

    [Test]
    public async Task Queued_operation_rechecks_requesting_session()
    {
        using var h = new PlayerOperationHarness();
        var old = await h.Login("a");
        var held = await h.Registry.AcquireAsync("a");
        var replacement = h.Login("a");
        var op = new TransferOperation();
        var queued = h.Registry.ExecuteForSessionAsync(old, op, default).AsTask();
        Assert.That(replacement.IsCompleted || queued.IsCompleted, Is.False);
        held.Dispose();
        var current = await replacement.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That((await queued.WaitAsync(PlayerExecutionHarness.Timeout)).Status, Is.EqualTo(RpcStatus.Unauthenticated));
        Assert.That(op.ApplyCalls, Is.Zero);
        Assert.That((await h.Registry.ExecuteForSessionAsync(current, op, default)).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Admission_and_cancellation_are_rechecked_after_load(bool cancelRequest)
    {
        using var h = new PlayerOperationHarness();
        using var cancel = new CancellationTokenSource();
        var entered = Barrier(); var proceed = Barrier();
        h.Store.BeforeLoad = async _ => { entered.TrySetResult(true); await proceed.Task; };
        var op = new TransferOperation();
        var running = h.Registry.ExecuteAsync(op, cancel.Token).AsTask();
        await entered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        if (cancelRequest) cancel.Cancel(); else h.Registry.StopAcceptingOperations();
        proceed.TrySetResult(true);
        Assert.That((await running.WaitAsync(PlayerExecutionHarness.Timeout)).Status, Is.EqualTo(RpcStatus.InvalidOperation));
        Assert.That(op.ApplyCalls, Is.Zero);
        Assert.That(h.Store.Loaded.All(p => p.ReleaseCalls == 1), Is.True);
    }

    [Test]
    public async Task Retirement_drains_temporary_only_background_operations()
    {
        using var h = new PlayerOperationHarness();
        var entered = Barrier(); var proceed = Barrier();
        h.Store.BeforeCommit = async () => { entered.TrySetResult(true); await proceed.Task; };
        var running = h.Registry.ExecuteAsync(new TransferOperation()).AsTask();
        await entered.Task.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(h.Registry.Players, Is.Empty);
        var retiring = h.Registry.RetireAllAsync().AsTask();
        Assert.That(retiring.IsCompleted, Is.False);
        Assert.That((await h.Registry.ExecuteAsync(new TransferOperation())).Status, Is.EqualTo(RpcStatus.InvalidOperation));
        proceed.TrySetResult(true);
        Assert.That((await running.WaitAsync(PlayerExecutionHarness.Timeout)).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        await retiring.WaitAsync(PlayerExecutionHarness.Timeout);
        Assert.That(h.Store.Loaded.All(p => p.ReleaseCalls == 1), Is.True);
    }
}
