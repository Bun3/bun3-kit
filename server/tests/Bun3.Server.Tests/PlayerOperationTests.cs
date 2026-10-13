using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Abstractions;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerOperationTests
{
    [Test]
    public async Task Retirement_continues_after_throwing_disconnect_and_releases_every_player()
    {
        using var h = new PlayerOperationHarness();
        var factory = h.Registry.Wrap(new PlayersConfig<ThrowingKickSession>(), c => new ThrowingKickSession(c));
        var a = (await factory(new FakeConnection(1, new FakeTransport())).SignInAsync("a")).Player;
        var b = (await factory(new FakeConnection(2, new FakeTransport())).SignInAsync("b")).Player;
        a.Gold = 71; a.MarkDirty();
        b.Gold = 42; b.MarkDirty();

        await h.Registry.RetireAllAsync();

        Assert.That(h.Registry.Players, Is.Empty);
        Assert.That((h.Store.Rows["a"].Gold, h.Store.Rows["b"].Gold), Is.EqualTo((71L, 42L)));
        Assert.That((a.ReleaseCalls, b.ReleaseCalls), Is.EqualTo((1, 1)));
        Assert.That(a.IsValid || b.IsValid, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Replacement_login_preserves_its_outcome_when_old_disconnect_throws(bool attachFails)
    {
        using var h = new PlayerOperationHarness();
        var factory = h.Registry.Wrap(new PlayersConfig<ThrowingKickSession>(), c => new ThrowingKickSession(c));
        var old = factory(new FakeConnection(1, new FakeTransport()));
        var player = (await old.SignInAsync("a")).Player;
        var attachError = new ArgumentException("attach failure");
        if (attachFails) player.Attached = () => throw attachError;
        var replacement = h.NewSession();

        if (attachFails)
        {
            Assert.That(Assert.ThrowsAsync<ArgumentException>(async () => await replacement.SignInAsync("a")), Is.SameAs(attachError));
            Assert.That(replacement.IsAuthenticated, Is.False);
            Assert.That(h.Registry.Players, Is.Empty);
            Assert.That(player.ReleaseCalls, Is.EqualTo(1));
        }
        else
        {
            Assert.That((await replacement.SignInAsync("a")).Player, Is.SameAs(player));
            Assert.That(replacement.Player, Is.SameAs(player));
            Assert.That(h.Registry.TryGet("a"), Is.SameAs(player));
            Assert.That(player.CurrentSession, Is.SameAs(replacement));
        }
        Assert.That(old.IsAuthenticated, Is.False);
    }

    private sealed class ThrowingKickSession(IConnection connection) : PlayerSession<ExecutionPlayer>(connection)
    {
        public override void Kick(int code) => throw new InvalidOperationException("disconnect");
    }

    [Test]
    public async Task Disconnect_error_does_not_skip_participant_cleanup()
    {
        using var h = new PlayerOperationHarness();
        var factory = h.Registry.Wrap(new PlayersConfig<ThrowingKickSession>(), c => new ThrowingKickSession(c));
        var session = factory(new FakeConnection(42, new FakeTransport()));
        var a = (await session.SignInAsync("a")).Player;
        var b = (await h.Login("b")).Player!;
        h.Store.FailWriteAt = 2;
        var result = await h.Registry.ExecuteAsync(new TransferOperation());
        Assert.That(result.Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That((a.ReleaseCalls, b.ReleaseCalls), Is.EqualTo((1, 1)));
        Assert.That(h.Registry.Players, Is.Empty);
    }

    [Test]
    public void Protobuf_codec_roundtrips_and_rejects_unsupported_schema()
    {
        var codec = new ProtobufPlayerResultCodec<GetGoldResponse>(GetGoldResponse.Parser, 7);
        var original = new GetGoldResponse { Gold = 91 };
        var bytes = codec.Encode(original);
        original.Gold = 0;
        Assert.That(codec.Decode(7, bytes).Gold, Is.EqualTo(91));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(6, bytes));
    }

    [Test]
    public async Task Partially_loaded_temporary_players_are_released_on_load_failure()
    {
        using var h = new PlayerOperationHarness();
        h.Store.BeforeLoad = key => key == "b" ? throw new InvalidOperationException("load") : default;
        var op = new TransferOperation();
        Assert.That((await h.Registry.ExecuteAsync(op)).Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(op.ApplyCalls, Is.Zero);
        Assert.That(h.Store.Loaded.Count, Is.EqualTo(1));
        Assert.That(h.Store.Loaded[0].ReleaseCalls, Is.EqualTo(1));
        Assert.That(h.Registry.Players, Is.Empty);
    }

    [Test]
    public void Operation_identity_owns_sorted_unique_keys_and_fingerprint()
    {
        var keys = new[] { "b", "a", "a" };
        var fingerprint = new byte[32];
        var operation = new TransferOperation(keys: keys, fingerprint: fingerprint);
        keys[0] = "c"; fingerprint[0] = 1;
        Assert.That(operation.ParticipantKeys, Is.EqualTo(new[] { "a", "b" }));
        Assert.That(operation.InputFingerprint.Span[0], Is.Zero);
    }

    [Test]
    public async Task Sqlite_second_participant_write_failure_rolls_back_all_rows_and_receipt()
    {
        using var store = new SqlitePlayerStore { FailWriteAt = 2 };
        using (var db = store.Open())
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "UPDATE players SET key='a'; INSERT INTO players VALUES('b',8,20)";
            cmd.ExecuteNonQuery();
        }
        using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var result = await registry.ExecuteAsync(new TransferOperation());
        Assert.That(result.Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(store.Writes, Is.EqualTo(2));
        using (var db = store.Open())
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT gold,version FROM players ORDER BY key";
            using (var rows = cmd.ExecuteReader())
            {
                Assert.That(rows.Read(), Is.True);
                Assert.That((rows.GetInt64(0), rows.GetInt64(1)), Is.EqualTo((100L, 3L)));
                Assert.That(rows.Read(), Is.True);
                Assert.That((rows.GetInt64(0), rows.GetInt64(1)), Is.EqualTo((20L, 8L)));
            }
            cmd.CommandText = "SELECT COUNT(*) FROM operations";
            Assert.That(Convert.ToInt64(cmd.ExecuteScalar()), Is.Zero);
        }
    }

    [Test]
    public async Task Online_to_offline_transfer_is_one_commit()
    {
        using var h = new PlayerOperationHarness();
        var a = (await h.Login("a")).Player!;
        var op = new TransferOperation();
        var result = await h.Registry.ExecuteAsync(op);
        Assert.That(result.Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That((result.Value!.SenderBalance, result.Value.RecipientBalance), Is.EqualTo((90L, 30L)));
        Assert.That(h.Store.Rows["a"], Is.EqualTo((90L, 4L)));
        Assert.That(h.Store.Rows["b"], Is.EqualTo((30L, 9L)));
        Assert.That(h.Store.WriteCommits, Is.EqualTo(1));
        Assert.That(h.Store.Receipts.Count, Is.EqualTo(1));
        Assert.That(h.Registry.TryGet("a"), Is.SameAs(a));
        Assert.That(h.Registry.TryGet("b"), Is.Null);
        Assert.That(h.Store.Loaded[1].ReleaseCalls, Is.EqualTo(1));
        Assert.That(h.Store.Loaded[1].CurrentSession, Is.Null);
        Assert.That(a.IsDirty, Is.False);
        Assert.Throws<ObjectDisposedException>(() => op.CapturedContext!.GetPlayer("a"));
    }

    [Test]
    public async Task Grace_target_reuses_unsaved_state()
    {
        using var h = new PlayerOperationHarness();
        var bSession = await h.Login("b");
        var b = bSession.Player!;
        await h.Registry.HandleSessionClosedAsync(bSession);
        b.Gold = 25; b.MarkDirty();
        var result = await h.Registry.ExecuteAsync(new TransferOperation());
        Assert.That(result.Value!.RecipientBalance, Is.EqualTo(35));
        Assert.That(h.Registry.TryGet("b"), Is.SameAs(b));
        Assert.That(h.Store.Loaded.Count, Is.EqualTo(2));
        Assert.That(b.ReleaseCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Reject_and_load_failure_preserve_existing_dirty_state(bool failLoad)
    {
        using var h = new PlayerOperationHarness();
        var a = (await h.Login("a")).Player!;
        a.Gold = 7; a.MarkDirty();
        if (failLoad) h.Store.BeforeLoad = _ => throw new InvalidOperationException("load");
        var op = new TransferOperation();
        var result = await h.Registry.ExecuteAsync(op);
        Assert.That(result.Outcome, Is.EqualTo(failLoad ? PlayerOperationOutcome.Failed : PlayerOperationOutcome.Rejected));
        Assert.That(op.ApplyCalls, Is.Zero);
        Assert.That(h.Registry.TryGet("a"), Is.SameAs(a));
        Assert.That(a.IsDirty, Is.True);
        Assert.That(a.Gold, Is.EqualTo(7));
        Assert.That(a.ReleaseCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Apply_or_second_write_failure_invalidates_every_participant(bool writeFailure)
    {
        using var h = new PlayerOperationHarness();
        var sa = await h.Login("a"); var a = sa.Player!;
        var sb = await h.Login("b"); var b = sb.Player!;
        var op = new TransferOperation();
        if (writeFailure) h.Store.FailWriteAt = 2;
        else op.Applying = () => throw new InvalidOperationException("apply");
        var result = await h.Registry.ExecuteAsync(op);
        Assert.That(result.Outcome, Is.EqualTo(PlayerOperationOutcome.Failed));
        Assert.That(h.Registry.Players, Is.Empty);
        Assert.That(sa.IsAuthenticated || sb.IsAuthenticated, Is.False);
        Assert.That((a.ReleaseCalls, b.ReleaseCalls), Is.EqualTo((1, 1)));
        Assert.That(h.Store.Rows["a"], Is.EqualTo((100L, 3L)));
        Assert.That(h.Store.Rows["b"], Is.EqualTo((20L, 8L)));
        await h.Registry.SaveAsync(a);
        await h.Registry.HandleSessionClosedAsync(sa);
        Assert.That(h.Store.WriteCommits, Is.Zero);
    }

    [TestCase("actor")]
    [TestCase("kind")]
    [TestCase("keys")]
    [TestCase("fingerprint")]
    public async Task Receipt_identity_mismatch_rejects_without_apply(string mismatch)
    {
        using var h = new PlayerOperationHarness();
        var original = new TransferOperation();
        await h.Registry.ExecuteAsync(original);
        var fingerprint = new byte[32]; if (mismatch == "fingerprint") fingerprint[0] = 1;
        var op = new TransferOperation(original.Id, mismatch == "actor" ? "b" : "a", mismatch == "kind" ? "other" : "transfer",
            mismatch == "keys" ? new[] { "a", "c" } : null, fingerprint);
        var result = await h.Registry.ExecuteAsync(op);
        Assert.That(result.Status, Is.EqualTo(RpcStatus.OperationConflict));
        Assert.That(result.Value, Is.Null);
        Assert.That(op.ApplyCalls, Is.Zero);
    }

    [Test]
    public async Task Repeated_id_returns_frozen_result_once()
    {
        using var h = new PlayerOperationHarness();
        var op = new TransferOperation();
        var first = await h.Registry.ExecuteAsync(op);
        op.MutableResult!.SenderBalance = -100;
        var second = await h.Registry.ExecuteAsync(op);
        Assert.That(first.Value!.SenderBalance, Is.EqualTo(90));
        Assert.That(second.Value!.SenderBalance, Is.EqualTo(90));
        Assert.That(second.Outcome, Is.EqualTo(PlayerOperationOutcome.AlreadyCommitted));
        Assert.That(op.ApplyCalls, Is.EqualTo(1));
        Assert.That(h.Store.Receipts.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Invalid_keys_and_nested_operation_fail_without_leaks()
    {
        using var h = new PlayerOperationHarness();
        Assert.Throws<ArgumentException>(() => new TransferOperation(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new TransferOperation(keys: new[] { "a", "" }));
        var oversized = new TransferOperation(keys: Enumerable.Range(0, 17).Select(i => i.ToString()).ToArray(), actor: "0");
        Assert.That((await h.Registry.ExecuteAsync(oversized)).Status, Is.EqualTo(RpcStatus.InvalidOperation));
        var op = new TransferOperation(keys: new[] { "b", "a", "a" });
        op.Applying = () => Assert.That(h.Registry.ExecuteAsync(new TransferOperation()).Result.Status, Is.EqualTo(RpcStatus.InvalidOperation));
        Assert.That((await h.Registry.ExecuteAsync(op)).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That((await h.Registry.ExecuteAsync(new TransferOperation())).Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
    }

    [Test]
    public async Task Version_conflict_before_apply_invalidates_existing_participants()
    {
        using var h = new PlayerOperationHarness();
        var a = (await h.Login("a")).Player!;
        var b = (await h.Login("b")).Player!;
        h.Store.Rows["b"] = (30, 9);
        var op = new TransferOperation();
        var result = await h.Registry.ExecuteAsync(op);
        Assert.That(result.Status, Is.EqualTo(RpcStatus.PersistenceFailure));
        Assert.That(op.ApplyCalls, Is.Zero);
        Assert.That((a.ReleaseCalls, b.ReleaseCalls), Is.EqualTo((1, 1)));
    }

    [Test]
    public async Task Codec_failure_invalidates_and_missing_player_rejects()
    {
        using var h = new PlayerOperationHarness();
        var a = (await h.Login("a")).Player!;
        var missing = await h.Registry.ExecuteAsync(new TransferOperation(keys: new[] { "a", "missing" }));
        Assert.That(missing.Status, Is.EqualTo(RpcStatus.PlayerNotFound));
        Assert.That(h.Registry.TryGet("a"), Is.SameAs(a));
        var result = await h.Registry.ExecuteAsync(new TransferOperation(codec: new TransferCodec { FailEncode = true }));
        Assert.That(result.Status, Is.EqualTo(RpcStatus.PersistenceFailure));
        Assert.That(a.ReleaseCalls, Is.EqualTo(1));
        Assert.That(h.Store.Loaded[1].ReleaseCalls, Is.EqualTo(1));
    }
}
