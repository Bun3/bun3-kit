using System.Threading.Channels;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Items;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class GeneratedOptionSaveTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task Retry_acknowledges_only_captured_payload()
    {
        var state = new EquipmentOptionState();
        state.Entries.Add(new EquipmentOptionEntry { Id = 11, Level = 1 });
        var inventory = CreateInventory(state);
        var io = new ControlledOptionIo();
        var pending = new PendingOptionSave(inventory, new GeneratedOptionMapping(), io.WriteAsync);
        state.Entries[0].Level = 10;
        pending.Capture();
        var captured = pending.Payload;
        Assert.That(captured, Has.Count.EqualTo(1));
        Assert.That(captured![0].Kind, Is.EqualTo(ItemChangeKind.Updated));
        Assert.That(inventory.HasChanges, Is.False);
        var failed = pending.SaveAsync();
        var first = await io.NextAsync();
        first.Completion.SetException(new IOException("write failed"));
        Assert.ThrowsAsync<IOException>(async () => await failed);
        Assert.That(pending.Payload, Is.SameAs(captured));

        var retry = pending.SaveAsync();
        var second = await io.NextAsync();
        state.Entries[0].Level = 20;
        pending.Capture();
        Assert.That(second.Payload, Is.SameAs(captured));
        Assert.That(Level(second.Payload), Is.EqualTo(10));
        Assert.That(inventory.HasChanges, Is.True, "Capture must not drain again before ACK.");
        Assert.ThrowsAsync<InvalidOperationException>(async () => await pending.SaveAsync());
        second.Completion.SetResult();
        await retry;
        Assert.That(pending.Payload, Is.Null);
        Assert.That(inventory.HasChanges, Is.True);

        pending.Capture();
        Assert.That(pending.Payload![0].Kind, Is.EqualTo(ItemChangeKind.Updated));
        Assert.That(Level(pending.Payload), Is.EqualTo(20));
        var next = pending.SaveAsync();
        var third = await io.NextAsync();
        third.Completion.SetResult();
        await next;
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(pending.Payload, Is.Null);
        Assert.That(Level(captured), Is.EqualTo(10), "Captured payload cannot retain live state references.");
    }

    [Test]
    public void Rollback_and_memory_commit_preserve_preexisting_pending_save()
    {
        var tx = new Transaction();
        var state = new EquipmentOptionState();
        state.Entries.Add(new EquipmentOptionEntry { Id = 11, Level = 1 });
        var inventory = CreateInventory(state, transaction: tx);
        var pending = new PendingOptionSave(inventory, new GeneratedOptionMapping(), _ => Task.CompletedTask);
        state.Entries[0].Level = 10;
        pending.Capture();
        var captured = pending.Payload;
        using (tx.Begin()) state.Entries[0].Level = 99;
        Assert.That(state.Entries[0].Level, Is.EqualTo(10));
        Assert.That(inventory.HasChanges, Is.False);
        Assert.That(pending.Payload, Is.SameAs(captured));
        using (var scope = tx.Begin())
        {
            state.Entries[0].Level = 20;
            scope.Commit();
        }
        Assert.That(inventory.HasChanges, Is.True);
        Assert.That(pending.Payload, Is.SameAs(captured));
        Assert.That(Level(captured!), Is.EqualTo(10));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Player_session_save_preserves_new_dirty_on_success_and_discards_on_failure(bool failFirst)
    {
        await using var harness = await SaveHarness.StartAsync();
        var firstConnection = await harness.LoginAsync();
        await OnActor(firstConnection.Session, () => harness.Player.State.Entries[0].Level = 10);
        firstConnection.Client.Close();
        var first = await harness.Player.Attempts.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        var firstIo = await harness.Player.Io.NextAsync();
        Assert.That(Level(firstIo.Payload), Is.EqualTo(10));
        // The save hook runs this mutation in its own actor flow while the I/O is pending.
        first.Mutation.SetResult(player => player.State.Entries[0].Level = 20);
        await first.Mutated.Task.WaitAsync(Timeout);
        if (failFirst) firstIo.Completion.SetException(new IOException("write failed"));
        else firstIo.Completion.SetResult();
        await firstConnection.Session.Closed.Task.WaitAsync(Timeout);
        Assert.That(harness.Player.IsDirty, Is.True);

        if (failFirst)
        {
            var discarded = harness.Player;
            Assert.That(harness.CachedPlayer, Is.Null);
            Assert.That(discarded.Attempts.Reader.TryRead(out _), Is.False, "Failed memory must not be retried.");
            var reloaded = await harness.LoginAsync();
            Assert.That(harness.Player, Is.Not.SameAs(discarded));
            Assert.That(harness.Player.State.Entries[0].Level, Is.EqualTo(1), "Fresh load restores authoritative state.");
            reloaded.Client.Close();
            await reloaded.Session.Closed.Task.WaitAsync(Timeout);
            Assert.That(harness.Player.Attempts.Reader.TryRead(out _), Is.False);
            return;
        }

        var finalConnection = await harness.LoginAsync();
        finalConnection.Client.Close();
        var final = await harness.Player.Attempts.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        var finalIo = await harness.Player.Io.NextAsync();
        Assert.That(Level(finalIo.Payload), Is.EqualTo(20));
        final.Mutation.SetResult(_ => { });
        finalIo.Completion.SetResult();
        await finalConnection.Session.Closed.Task.WaitAsync(Timeout);
        Assert.That(harness.Player.IsDirty, Is.False);
    }

    private static int Level(IReadOnlyList<OptionSaveRow> payload) =>
        JObject.Parse(payload[0].Json!)["RerollOptions"]![0]!["Level"]!.Value<int>();

    private static ItemInventory<EquipmentOptionState> CreateInventory(EquipmentOptionState state,
        Action? onDirty = null, Transaction? transaction = null)
    {
        var catalog = new ItemCatalogBuilder<string>().Register("sword", "Sword", unstackable: true).Build();
        var inventory = new ItemInventory<EquipmentOptionState>(catalog, () => 2, _ => new(), onDirty: onDirty, transaction: transaction);
        Assert.That(inventory.TryLoadInstance(1, catalog.GetRequired("sword"), 1, 0, state), Is.EqualTo(InventoryError.None));
        return inventory;
    }

    private static async Task OnActor(SaveSession session, Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.That(session.Post(() =>
        {
            try { work(); done.SetResult(); }
            catch (Exception error) { done.SetException(error); }
            return default;
        }), Is.True);
        await done.Task.WaitAsync(Timeout);
    }

    private sealed class SaveAttempt
    {
        public readonly TaskCompletionSource<Action<SavePlayer>> Mutation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Mutated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SavePlayer : Player
    {
        public readonly EquipmentOptionState State = new();
        public readonly ControlledOptionIo Io = new();
        public readonly Channel<SaveAttempt> Attempts = Channel.CreateUnbounded<SaveAttempt>();
        private readonly List<SaveAttempt> _allAttempts = new();
        private bool _releaseControls;
        private readonly ItemInventory<EquipmentOptionState> _inventory;
        private readonly PendingOptionSave _pending;

        public SavePlayer()
        {
            State.Entries.Add(new EquipmentOptionEntry { Id = 11, Level = 1 });
            _inventory = CreateInventory(State, MarkDirty);
            _pending = new PendingOptionSave(_inventory, new GeneratedOptionMapping(), Io.WriteAsync);
        }

        protected internal override async ValueTask OnSaveAsync()
        {
            _pending.Capture();
            var saving = _pending.SaveAsync();
            var attempt = new SaveAttempt();
            lock (_allAttempts)
            {
                _allAttempts.Add(attempt);
                if (_releaseControls) attempt.Mutation.TrySetResult(_ => { });
            }
            Attempts.Writer.TryWrite(attempt);
            var mutation = await attempt.Mutation.Task;
            mutation(this);
            attempt.Mutated.SetResult();
            await saving;
            if (_inventory.HasChanges) MarkDirty();
        }

        public void ReleaseControls()
        {
            lock (_allAttempts)
            {
                _releaseControls = true;
                foreach (var attempt in _allAttempts) attempt.Mutation.TrySetResult(_ => { });
            }
            Io.ReleaseAll();
        }
    }

    private sealed class SaveSession(IConnection connection) : PlayerSession<SavePlayer>(connection)
    {
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override ValueTask OnPlayerSessionClosedAsync(Exception? error)
        {
            Closed.TrySetResult();
            return default;
        }
    }

    private sealed class SaveHarness : IAsyncDisposable
    {
        public SavePlayer Player = new();
        private int _loads;
        public SavePlayer? CachedPlayer => _registry.TryGet("save-fixture");
        private PlayerRegistry<SavePlayer> _registry = null!;
        private TcpTransportListener _listener = null!;
        private RpcServer<SaveSession, PlayersRequest, PlayersResponse, PlayersUpdate> _server = null!;

        public static async Task<SaveHarness> StartAsync()
        {
            var h = new SaveHarness();
            h._registry = new PlayerRegistry<SavePlayer>(_ =>
            {
                if (h._loads++ > 0) h.Player = new SavePlayer();
                return new ValueTask<SavePlayer>(h.Player);
            });
            var config = new PlayersConfig<SaveSession>();
            config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (session, request) =>
            {
                var result = await session.SignInAsync("save-fixture");
                return new LoginResponse { IsReconnect = result.IsReconnect };
            });
            config.OnRequest<AddGoldRequest, AddGoldResponse>((_, _) => new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse()));
            config.OnRequest<GetGoldRequest, GetGoldResponse>((_, _) => new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse()));
            h._listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
            h._server = new RpcServer<SaveSession, PlayersRequest, PlayersResponse, PlayersUpdate>(h._listener,
                h._registry.Wrap(config, connection => new SaveSession(connection)), config.Rpc);
            await h._server.StartAsync();
            return h;
        }

        public async Task<(RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate> Client, SaveSession Session)> LoginAsync()
        {
            var client = await RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = _listener.BoundPort!.Value }));
            var reply = await client.RequestAsync<LoginResponse>(new LoginRequest()).AsTask().WaitAsync(Timeout);
            Assert.That(reply.IsOk, Is.True);
            return (client, (SaveSession)Player.CurrentSession!);
        }

        public async ValueTask DisposeAsync()
        {
            // Release controlled I/O before stopping the real session actor, even if an assertion fails.
            Player.ReleaseControls();
            await _server.StopAsync().WaitAsync(Timeout);
            await _registry.RetireAllAsync();
        }
    }
}

internal sealed record OptionSaveRow(long InstanceId, ItemChangeKind Kind, string? Json);

internal sealed class PendingOptionSave
{
    private readonly ItemInventory<EquipmentOptionState> _inventory;
    private readonly GeneratedOptionMapping _mapping;
    private readonly Func<IReadOnlyList<OptionSaveRow>, Task> _write;
    private bool _saving;

    public PendingOptionSave(ItemInventory<EquipmentOptionState> inventory, GeneratedOptionMapping mapping,
        Func<IReadOnlyList<OptionSaveRow>, Task> write)
    {
        _inventory = inventory;
        _mapping = mapping;
        _write = write;
    }

    public IReadOnlyList<OptionSaveRow>? Payload { get; private set; }

    public void Capture()
    {
        if (Payload != null) return;
        var changes = new List<ItemChange<EquipmentOptionState>>();
        _inventory.DrainChanges(changes);
        if (changes.Count == 0) return;
        Payload = Array.AsReadOnly(changes.Select(change => new OptionSaveRow(change.InstanceId, change.Kind,
            change.Instance == null ? null : _mapping.SaveJson(change.Instance.State))).ToArray());
    }

    public async Task SaveAsync()
    {
        if (_saving) throw new InvalidOperationException("Only one save may run at a time.");
        var payload = Payload;
        if (payload == null) return;
        _saving = true;
        try
        {
            await _write(payload);
            Payload = null;
        }
        finally { _saving = false; }
    }
}

internal sealed class ControlledOptionIo
{
    internal sealed record Write(IReadOnlyList<OptionSaveRow> Payload)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly Channel<Write> _writes = Channel.CreateUnbounded<Write>();
    private readonly List<Write> _all = new();
    private bool _released;
    public Task WriteAsync(IReadOnlyList<OptionSaveRow> payload)
    {
        var write = new Write(payload);
        lock (_all)
        {
            _all.Add(write);
            if (_released) write.Completion.TrySetResult();
        }
        _writes.Writer.TryWrite(write);
        return write.Completion.Task;
    }
    public Task<Write> NextAsync() => _writes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    public void ReleaseAll()
    {
        lock (_all)
        {
            _released = true;
            foreach (var write in _all) write.Completion.TrySetResult();
        }
    }
}
