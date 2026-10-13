using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Bun3.Server.Tests;

public class PlayerStoreSqliteTests
{
    private static ExecutionSession Session(PlayerRegistry<ExecutionPlayer> registry) => registry.Wrap(new PlayersConfig<ExecutionSession>(), c => new ExecutionSession(c))(new FakeConnection(1, new FakeTransport()));
    [Test]
    public async Task Single_save_updates_state_and_version_atomically()
    {
        using var store = new SqlitePlayerStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var session = Session(registry); var player = (await session.SignInAsync("guest:a")).Player;
        player.Gold = 90; player.MarkDirty();
        await registry.SaveAsync(player);
        Assert.That(store.Read(), Is.EqualTo((90L,4L)));
        Assert.That(player.IsDirty, Is.False);
        Assert.That(player.SaveCalls, Is.Zero);
        await registry.RetireAllAsync();
        Assert.That(player.RetirementCalls, Is.Zero);
        Assert.That(player.ReleaseCalls, Is.EqualTo(1));
    }
    [Test]
    public async Task Write_failure_rolls_back_and_discards_cached_player()
    {
        using var store = new SqlitePlayerStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var player = (await Session(registry).SignInAsync("guest:a")).Player;
        player.Gold = 90; player.MarkDirty(); store.FailWrite = true;
        await registry.SaveAsync(player);
        Assert.That(store.Read(), Is.EqualTo((100L,3L)));
        Assert.That(registry.TryGet("guest:a"), Is.Null);
        store.FailWrite = false;
        Assert.That((await Session(registry).SignInAsync("guest:a")).Player.Gold, Is.EqualTo(100));
    }
    [Test]
    public async Task Version_mismatch_preserves_external_writer()
    {
        using var store = new SqlitePlayerStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var player = (await Session(registry).SignInAsync("guest:a")).Player;
        using (var db = store.Open()) { using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE players SET gold=120,version=4"; cmd.ExecuteNonQuery(); }
        player.Gold = 90; player.MarkDirty(); await registry.SaveAsync(player);
        Assert.That(store.Read(), Is.EqualTo((120L,4L))); Assert.That(registry.TryGet("guest:a"), Is.Null);
    }
    [Test]
    public async Task Load_waits_for_previous_writer()
    {
        using var store = new SqlitePlayerStore(); using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        using var db = store.Open(); using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "UPDATE players SET gold=120,version=4"; cmd.ExecuteNonQuery();
        var loading = Task.Run(async () => await Session(registry).SignInAsync("guest:a"));
        await store.Opening.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(loading.IsCompleted, Is.False);
        tx.Commit(); Assert.That((await loading).Player.Gold, Is.EqualTo(120));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task Sqlite_receipt_and_audit_share_the_state_transaction(bool rollback)
    {
        using var store = new SqlitePlayerStore();
        var id = Guid.NewGuid();
        var record = new PlayerOperationRecord(id,"spend","guest:a",new[] { "guest:a" },new byte[32],1,new byte[] { 7 });
        await using (var scope = await store.OpenScopeAsync(new[] { "guest:a" }, NullLogger.Instance, default))
        {
            await scope.WriteAsync(new PlayerSaveData("guest:a",3,1,90L,new[] { new PlayerAuditRecord(id,"spent",DateTime.UtcNow,new byte[] { 8 }) }), default);
            await scope.WriteOperationAsync(record, default);
            if (rollback) await scope.RollbackAsync(new Exception("abort"));
            else await scope.CommitAsync(default);
        }
        await using (var scope = await store.OpenScopeAsync(new[] { "guest:a" }, NullLogger.Instance, default))
        {
            var saved = await scope.ReadOperationAsync(id, default);
            if (rollback) Assert.That(saved, Is.Null);
            else
            {
                Assert.That(saved, Is.Not.Null);
                Assert.That(saved!.ParticipantKeys, Is.EqualTo(record.ParticipantKeys));
                Assert.That(saved.InputFingerprint.ToArray(), Is.EqualTo(record.InputFingerprint.ToArray()));
                Assert.That(saved.ResultPayload.ToArray(), Is.EqualTo(record.ResultPayload.ToArray()));
            }
        }
        using var db = store.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM audits";
        Assert.That(Convert.ToInt32(cmd.ExecuteScalar()), Is.EqualTo(rollback ? 0 : 1));
        Assert.That(store.Read(), Is.EqualTo(rollback ? (100L,3L) : (90L,4L)));
    }

    [Test]
    public async Task Administrator_receipt_preserves_actor_kind()
    {
        using var store = new SqlitePlayerStore();
        var actor = new PlayerOperationActor(PlayerOperationActorKind.Administrator, "guest:a");
        var record = new PlayerOperationRecord(Guid.NewGuid(), "grant", actor, new[] { "guest:a" }, new byte[32], 1, new byte[] { 7 });
        await using (var scope = await store.OpenScopeAsync(record.ParticipantKeys, NullLogger.Instance, default))
        {
            await scope.WriteOperationAsync(record, default);
            await scope.CommitAsync(default);
        }
        await using var reading = await store.OpenScopeAsync(record.ParticipantKeys, NullLogger.Instance, default);
        Assert.That((await reading.ReadOperationAsync(record.Id, default))!.Actor, Is.EqualTo(actor));
    }
}
