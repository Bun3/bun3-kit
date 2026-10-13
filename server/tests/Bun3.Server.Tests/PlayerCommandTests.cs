using Bun3.Server.Players;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerCommandTests
{
    [Test]
    public async Task Submission_copies_mutable_input()
    {
        using var h = new CommandHarness(); var bytes = new byte[] { 7 }; var fingerprint = new byte[32]; var keys = new[] { "a" };
        var command = new PlayerCommandEnvelope(Guid.NewGuid(), "test-scope", new(PlayerOperationActorKind.Administrator, "admin"), "grant", 1, keys, fingerprint, bytes);
        await h.Queue.SubmitAsync(command); bytes[0] = 99; keys[0] = "b"; fingerprint[0] = 99;
        var saved = (await h.Queue.GetAsync(command.Id))!;
        Assert.That(saved.Envelope.InputPayload.ToArray(), Is.EqualTo(new byte[] { 7 }));
        Assert.That(saved.Envelope.ParticipantKeys, Is.EqualTo(new[] { "a" }));
        Assert.That(saved.Envelope.InputFingerprint.ToArray(), Is.EqualTo(new byte[32]));
    }
    [TestCase("actor")]
    [TestCase("scope")]
    [TestCase("payload")]
    public async Task Duplicate_id_with_different_actor_scope_or_payload_conflicts(string field)
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        var different = CommandHarness.Envelope(command.Id, actor: field == "actor" ? "other" : "admin",
            scope: field == "scope" ? "other" : "test-scope", amount: field == "payload" ? (byte)8 : (byte)7);
        Assert.ThrowsAsync<PlayerCommandConflictException>(async () => await h.Queue.SubmitAsync(different));
        Assert.That(h.Commands.Rows, Has.Count.EqualTo(1)); Assert.That(h.Operations, Is.Empty);
    }
    [Test]
    public async Task Admin_actor_is_not_a_loaded_player()
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        var result = await h.Dispatcher.ExecuteAsync(command.Id);
        Assert.That(result!.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        Assert.That(h.Store.Locked, Is.EqualTo(new[] { "a" })); Assert.That(h.Store.Rows["a"], Is.EqualTo(107));
        Assert.That(h.Registry.Players, Is.Empty);
    }
    [Test]
    public async Task Factory_cannot_change_accepted_identity()
    {
        using var h = new CommandHarness(); h.Factory = e => new CommandGrant(e, Guid.NewGuid());
        var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        Assert.ThrowsAsync<PlayerCommandConflictException>(async () => await h.Dispatcher.ExecuteAsync(command.Id));
        Assert.That(h.Store.Loaded, Is.Empty); Assert.That(h.Operations[0].Applies, Is.Zero);
        Assert.That((await h.Queue.GetAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Pending));
    }
    [Test]
    public async Task Offline_unsupported_command_remains_pending()
    {
        using var h = new CommandHarness(supportsOffline: false); var command = CommandHarness.Envelope(); await h.Queue.SubmitAsync(command);
        Assert.That((await h.Dispatcher.ExecuteAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Pending));
        Assert.That(h.Operations, Is.Empty); Assert.That(h.Store.Loaded, Is.Empty);
    }
    [TestCase("unknown", 1)]
    [TestCase("grant", 2)]
    public async Task Unknown_kind_or_schema_remains_pending(string kind, int schema)
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(kind: kind, schema: schema); await h.Queue.SubmitAsync(command);
        Assert.That((await h.Dispatcher.ExecuteAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Pending));
        Assert.That(h.Operations, Is.Empty);
    }
    [Test]
    public async Task Foreign_scope_never_runs_and_catalog_freezes()
    {
        using var h = new CommandHarness(); var command = CommandHarness.Envelope(scope: "other"); await h.Queue.SubmitAsync(command);
        var dispatcher = h.Dispatcher;
        Assert.That((await dispatcher.ExecuteAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Pending));
        Assert.Throws<InvalidOperationException>(() => h.Catalog.Register("other", 1, true, e => new CommandGrant(e)));
        Assert.That(h.Operations, Is.Empty);
    }
}
