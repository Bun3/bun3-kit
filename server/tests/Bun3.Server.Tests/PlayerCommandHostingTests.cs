using Bun3.Server.Hosting;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Ticking;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class PlayerCommandHostingTests
{
    [Test]
    public async Task Command_worker_stops_before_offline_publication()
    {
        var control = new AuthorityStoreHarness();
        var store = new OwnedStoreHarness(control, new(PlayerWriteRole.Server, Guid.NewGuid()));
        var commands = new CommandStoreHarness(control); var entered = PlayerExecutionHarness.Barrier(); var release = PlayerExecutionHarness.Barrier();
        commands.BeforeComplete = async _ => { entered.TrySetResult(true); await release.Task; };
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddPlayerServer<ExecutionSession, ExecutionPlayer, PlayersRequest, PlayersResponse, PlayersUpdate>(
            _ => store, config =>
            {
                config.OnRequestUnauthenticated<LoginRequest, LoginResponse>(async (s, r) => new LoginResponse { Gold = (await s.SignInAsync(r.DeviceId)).Player.Gold });
                config.OnRequest<AddGoldRequest, AddGoldResponse>((s, _) => new(new AddGoldResponse { Gold = s.Player!.Gold }));
                config.OnRequest<GetGoldRequest, GetGoldResponse>((s, _) => new(new GetGoldResponse { Gold = s.Player!.Gold }));
            }, serverOptions: o => o.Port = 0);
        builder.Services.AddPlayerCommands<ExecutionPlayer>(_ => commands, catalog => catalog.Register("grant", 1, true, e => new CommandGrant(e)),
            o => o.PollInterval = TimeSpan.FromMilliseconds(10));
        using var host = builder.Build(); var queue = host.Services.GetRequiredService<PlayerCommandQueue>(); var command = CommandHarness.Envelope();
        await queue.SubmitAsync(command); await host.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = host.StopAsync();
        try
        {
            Assert.That(stopping.IsCompleted, Is.False); Assert.That(control.Row!.State, Is.Not.EqualTo(PlayerWriteState.Offline));
            Assert.That(store.Rows["a"], Is.EqualTo(100));
        }
        finally { release.TrySetResult(true); }
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That((await queue.GetAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Committed));
        Assert.That(store.Rows["a"], Is.EqualTo(107)); Assert.That(control.Row!.State, Is.EqualTo(PlayerWriteState.Offline));
    }

    [Test]
    public async Task Offline_worker_never_attaches_or_ticks()
    {
        var store = OwnedStoreHarness.Offline(); var commands = new CommandStoreHarness(store.Control);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddOfflinePlayerCommands<ExecutionPlayer>(_ => store, _ => commands,
            catalog => catalog.Register("grant", 1, true, e => new CommandGrant(e)), o => o.PollInterval = TimeSpan.FromMilliseconds(10));
        using var host = builder.Build();
        var queue = host.Services.GetService<PlayerCommandQueue>(); Assert.That(queue, Is.Not.Null);
        var command = CommandHarness.Envelope(); await queue!.SubmitAsync(command);
        await host.StartAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while ((await queue.GetAsync(command.Id, timeout.Token))!.Status == PlayerCommandStatus.Pending) await Task.Delay(10, timeout.Token);
            Assert.That(store.Rows["a"], Is.EqualTo(107));
            Assert.That(host.Services.GetService<TickLoop>(), Is.Null); Assert.That(host.Services.GetService<TcpTransportListener>(), Is.Null);
            Assert.That(host.Services.GetRequiredService<PlayerRegistry<ExecutionPlayer>>().Players, Is.Empty);
        }
        finally { await host.StopAsync(); }
    }
    [Test]
    public void Offline_worker_rejects_server_binding()
    {
        var store = OwnedStoreHarness.Online(); var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddOfflinePlayerCommands<ExecutionPlayer>(_ => store, _ => new CommandStoreHarness(store.Control), _ => { });
        using var host = builder.Build();
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<PlayerRegistry<ExecutionPlayer>>());
    }
    [Test]
    public async Task Unsupported_pending_commands_do_not_starve_supported_commands()
    {
        var store = OwnedStoreHarness.Offline(); var commands = new CommandStoreHarness(store.Control);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddOfflinePlayerCommands<ExecutionPlayer>(_ => store, _ => commands,
            catalog =>
            {
                catalog.Register("grant", 1, true, e => new CommandGrant(e));
                catalog.Register("online-only", 1, false, e => new CommandGrant(e));
            }, o => { o.BatchSize = 1; o.PollInterval = TimeSpan.FromMilliseconds(10); });
        using var host = builder.Build(); var queue = host.Services.GetRequiredService<PlayerCommandQueue>();
        var unsupported = new[] { CommandHarness.Envelope(kind: "unknown"), CommandHarness.Envelope(schema: 2), CommandHarness.Envelope(kind: "online-only") };
        foreach (var command in unsupported) await queue.SubmitAsync(command);
        var supported = CommandHarness.Envelope(); await queue.SubmitAsync(supported);
        await host.StartAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while ((await queue.GetAsync(supported.Id, timeout.Token))!.Status == PlayerCommandStatus.Pending) await Task.Delay(10, timeout.Token);
            Assert.That(store.Rows["a"], Is.EqualTo(107));
            foreach (var command in unsupported) Assert.That((await queue.GetAsync(command.Id))!.Status, Is.EqualTo(PlayerCommandStatus.Pending));
        }
        finally { await host.StopAsync(); }
    }
    [Test]
    public void Invalid_worker_options_are_rejected()
    {
        var store = OwnedStoreHarness.Offline(); var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddOfflinePlayerCommands<ExecutionPlayer>(_ => store, _ => new CommandStoreHarness(store.Control), _ => { }, o => o.MaxConcurrency = 0);
        using var host = builder.Build();
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await host.StartAsync());
    }
}
