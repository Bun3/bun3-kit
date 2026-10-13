using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using System.Text.Json;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerOperationE2ETests
{
    private sealed class PvpSettlementOperation : PlayerOperation<ExecutionPlayer, TransferResult>
    {
        internal PvpSettlementOperation()
            : base(Guid.NewGuid(), "pvp-settlement", "a", new[] { "a", "b" }, new byte[32], new TransferCodec()) { }

        public override int Validate(PlayerOperationContext<ExecutionPlayer> context) =>
            context.GetPlayer("b").Gold >= 5 ? 0 : -1;

        public override TransferResult Apply(PlayerOperationContext<ExecutionPlayer> context)
        {
            var winner = context.GetPlayer("a");
            var loser = context.GetPlayer("b");
            winner.Gold += 5;
            loser.Gold -= 5;
            winner.MarkDirty();
            loser.MarkDirty();
            return new TransferResult { SenderBalance = winner.Gold, RecipientBalance = loser.Gold };
        }
    }

    [Test]
    public async Task Sqlite_offline_pvp_settlement_uses_same_atomic_path()
    {
        using var store = new SqlitePlayerStore();
        using (var db = store.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "UPDATE players SET key='a'; INSERT INTO players VALUES('b',8,20)";
            command.ExecuteNonQuery();
        }
        using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var operation = new PvpSettlementOperation();
        var result = await registry.ExecuteAsync(operation);
        Assert.That(result.Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That((result.Value!.SenderBalance, result.Value.RecipientBalance), Is.EqualTo((105L, 15L)));
        using (var db = store.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "SELECT key,gold,version FROM players ORDER BY key";
            using (var rows = command.ExecuteReader())
            {
                Assert.That(rows.Read(), Is.True);
                Assert.That((rows.GetString(0), rows.GetInt64(1), rows.GetInt64(2)), Is.EqualTo(("a", 105L, 4L)));
                Assert.That(rows.Read(), Is.True);
                Assert.That((rows.GetString(0), rows.GetInt64(1), rows.GetInt64(2)), Is.EqualTo(("b", 15L, 9L)));
                Assert.That(rows.Read(), Is.False);
            }
            command.CommandText = "SELECT id,kind,actor,participants,fingerprint,schema_version,result FROM operations";
            using var receipt = command.ExecuteReader();
            Assert.That(receipt.Read(), Is.True);
            Assert.That(receipt.GetString(0), Is.EqualTo(operation.Id.ToString("D")));
            Assert.That(receipt.GetString(1), Is.EqualTo(operation.Kind));
            Assert.That(receipt.GetString(2), Is.EqualTo(operation.ActorKey));
            Assert.That(JsonSerializer.Deserialize<string[]>((byte[])receipt[3]), Is.EqualTo(operation.ParticipantKeys));
            Assert.That((byte[])receipt[4], Is.EqualTo(operation.InputFingerprint.ToArray()));
            var decoded = new TransferCodec().Decode(receipt.GetInt32(5), (byte[])receipt[6]);
            Assert.That((decoded.SenderBalance, decoded.RecipientBalance), Is.EqualTo((105L, 15L)));
            Assert.That(receipt.Read(), Is.False);
        }
        var writesAfterCommit = store.Writes;
        var replay = await registry.ExecuteAsync(operation);
        Assert.That(replay.Outcome, Is.EqualTo(PlayerOperationOutcome.AlreadyCommitted));
        Assert.That((replay.Value!.SenderBalance, replay.Value.RecipientBalance), Is.EqualTo((105L, 15L)));
        Assert.That(store.Writes, Is.EqualTo(writesAfterCommit), "replay must not write either participant again");
        Assert.That(registry.Players, Is.Empty);
    }

    [Test]
    public async Task Sqlite_transfer_to_offline_player_replays_receipt_without_second_apply()
    {
        using var store = new SqlitePlayerStore();
        using (var db = store.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "UPDATE players SET key='a'; INSERT INTO players VALUES('b',8,20)";
            command.ExecuteNonQuery();
        }
        using var registry = new PlayerRegistry<ExecutionPlayer>(store);
        var operation = new TransferOperation();
        var first = await registry.ExecuteAsync(operation);
        Assert.That(first.Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        var second = await registry.ExecuteAsync(operation);
        Assert.That(second.Outcome, Is.EqualTo(PlayerOperationOutcome.AlreadyCommitted));
        Assert.That(second.Value!.SenderBalance, Is.EqualTo(first.Value!.SenderBalance));
        Assert.That(second.Value.RecipientBalance, Is.EqualTo(first.Value.RecipientBalance));
        Assert.That(operation.ApplyCalls, Is.EqualTo(1));
        using (var db = store.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "SELECT key,gold,version FROM players ORDER BY key";
            using var rows = command.ExecuteReader();
            Assert.That(rows.Read(), Is.True);
            Assert.That((rows.GetString(0), rows.GetInt64(1), rows.GetInt64(2)), Is.EqualTo(("a", 90L, 4L)));
            Assert.That(rows.Read(), Is.True);
            Assert.That((rows.GetString(0), rows.GetInt64(1), rows.GetInt64(2)), Is.EqualTo(("b", 30L, 9L)));
        }
        Assert.That(registry.Players, Is.Empty);
    }
}
