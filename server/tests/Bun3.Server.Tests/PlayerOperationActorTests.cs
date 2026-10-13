using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerOperationActorTests
{
    [Test]
    public async Task Admin_actor_is_not_loaded_as_participant()
    {
        using var h = new PlayerOperationHarness();
        var loaded = new List<string>();
        h.Store.BeforeLoad = key => { loaded.Add(key); return default; };
        var operation = new ActorOperation(new(PlayerOperationActorKind.Administrator, "operator:x"));
        var result = await h.Registry.ExecuteAsync(operation);
        Assert.That(result.Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
        Assert.That(loaded, Is.EqualTo(new[] { "a" }));
        Assert.That(h.Store.Receipts[operation.Id].Actor.Kind, Is.EqualTo(PlayerOperationActorKind.Administrator));
    }

    [Test]
    public async Task Equal_keys_of_different_actor_kinds_do_not_match()
    {
        using var h = new PlayerOperationHarness();
        var id = Guid.NewGuid();
        await h.Registry.ExecuteAsync(new ActorOperation(new(PlayerOperationActorKind.Administrator, "a"), id));
        var replay = new ActorOperation(new(PlayerOperationActorKind.Player, "a"), id);
        Assert.That((await h.Registry.ExecuteAsync(replay)).Status, Is.EqualTo(RpcStatus.OperationConflict));
        Assert.That(replay.ApplyCalls, Is.Zero);
    }

    [Test]
    public void Player_actor_must_be_a_participant() => Assert.Throws<ArgumentException>(() =>
        new ActorOperation(new(PlayerOperationActorKind.Player, "elsewhere")));

    [Test]
    public void Default_or_unknown_actor_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ActorOperation(default));
        Assert.Throws<ArgumentException>(() => new ActorOperation(new((PlayerOperationActorKind)99, "a")));
        Assert.Throws<ArgumentException>(() => new PlayerOperationRecord(Guid.NewGuid(), "grant", default(PlayerOperationActor),
            new[] { "a" }, new byte[32], 1, new byte[1]));
    }

    [Test]
    public async Task Game_rpc_rejects_admin_actor()
    {
        using var h = new PlayerOperationHarness();
        var session = await h.Login("a");
        var operation = new ActorOperation(new(PlayerOperationActorKind.Administrator, "a"));
        var result = await h.Registry.ExecuteForSessionAsync(session, operation, default);
        Assert.That(result.Status, Is.EqualTo(RpcStatus.Unauthenticated));
        Assert.That(operation.ApplyCalls, Is.Zero);
    }

    internal sealed class ActorOperation : PlayerOperation<ExecutionPlayer, TransferResult>
    {
        internal int ApplyCalls;
        internal ActorOperation(PlayerOperationActor actor, Guid? id = null)
            : base(id ?? Guid.NewGuid(), "grant", actor, new[] { "a" }, new byte[32], new TransferCodec()) { }
        public override int Validate(PlayerOperationContext<ExecutionPlayer> context) => 0;
        public override TransferResult Apply(PlayerOperationContext<ExecutionPlayer> context)
        {
            ApplyCalls++;
            var player = context.GetPlayer("a");
            player.Gold += 7; player.MarkDirty();
            return new TransferResult { RecipientBalance = player.Gold };
        }
    }
}
