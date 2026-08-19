using System.Collections.Concurrent;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Tests.PlayersProtocol;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class SessionPostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Thread-safe collecting logger — for verifying watchdog warnings and work exception logs.</summary>
    private sealed class CollectingLogger : ILogger
    {
        public readonly ConcurrentQueue<(LogLevel Level, string Message)> Entries = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private sealed class PostSession : RpcSession
    {
        public PostSession(IConnection connection) : base(connection) { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public readonly CollectingLogger Logger = new();
        public readonly ConcurrentQueue<string> Order = new();
        public RpcServer<PostSession, PlayersRequest, PlayersResponse, PlayersUpdate> Server = null!;
        public TcpTransportListener Listener = null!;

        public static async Task<Harness> StartAsync(TimeSpan? slowWorkWarning = null, int maxQueued = 256)
        {
            var h = new Harness();
            var config = new RpcConfig<PostSession>();
            // Schema validation (RpcSchema.Validate) requires handlers for every PlayersRequest
            // oneof case — these tests only use get_gold, so the rest are stubs.
            config.OnRequest<LoginRequest, LoginResponse>((s, req) =>
                new ValueTask<Reply<LoginResponse>>(new LoginResponse()));
            config.OnRequest<AddGoldRequest, AddGoldResponse>((s, req) =>
                new ValueTask<Reply<AddGoldResponse>>(new AddGoldResponse()));
            config.OnRequest<GetGoldRequest, GetGoldResponse>((s, req) =>
            {
                h.Order.Enqueue("handler");
                return new ValueTask<Reply<GetGoldResponse>>(new GetGoldResponse { Gold = 1 });
            });
            h.Listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
            h.Server = new RpcServer<PostSession, PlayersRequest, PlayersResponse, PlayersUpdate>(
                h.Listener, conn => new PostSession(conn), config,
                new RpcServerOptions
                {
                    MaxQueuedPackets = maxQueued,
                    SlowWorkWarning = slowWorkWarning ?? TimeSpan.FromSeconds(1),
                },
                h.Logger);
            await h.Server.StartAsync();
            return h;
        }

        public ValueTask<RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>> ConnectAsync() =>
            RpcClient<PlayersRequest, PlayersResponse, PlayersUpdate>.ConnectAsync(
                new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = Listener.BoundPort!.Value }));

        public async Task<PostSession> GetSessionAsync()
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                foreach (var session in Server.Sessions)
                {
                    return session;
                }
                await Task.Delay(10);
            }
            throw new TimeoutException("session was not created");
        }

        public async ValueTask DisposeAsync() => await Server.StopAsync();
    }

    [Test]
    public async Task Posted_work_interleaves_in_order_with_packets()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        var workDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.That(session.Post(() =>
        {
            h.Order.Enqueue("work");
            workDone.TrySetResult(true);
            return default;
        }), Is.True);

        var reply = await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
        Assert.That(reply.IsOk, Is.True);
        await workDone.Task.WaitAsync(Timeout);

        // Post entered the queue before the request, so it runs first (same queue, sequential)
        Assert.That(h.Order.ToArray(), Is.EqualTo(new[] { "work", "handler" }));
        client.Close();
    }

    [Test]
    public async Task Post_returns_false_after_session_closed()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        client.Close();
        var deadline = DateTime.UtcNow + Timeout;
        while (session.Connection.IsOpen && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        await Task.Delay(100);   // slack for NotifyClosed propagation

        Assert.That(session.Post(() => default), Is.False);
    }

    [Test]
    public async Task Post_returns_false_when_queue_is_full()
    {
        await using var h = await Harness.StartAsync(maxQueued: 4);
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        // one work item that blocks the consume loop + fill the queue to its cap
        var block = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.That(session.Post(async () => await block.Task), Is.True);
        await Task.Delay(100);   // time for the blocker to be dequeued into a running (waiting) state

        var accepted = 0;
        var sawFalse = false;
        for (var i = 0; i < 10; i++)
        {
            if (session.Post(() => default)) accepted++;
            else { sawFalse = true; break; }
        }

        Assert.That(sawFalse, Is.True);
        Assert.That(accepted, Is.LessThanOrEqualTo(4));
        block.TrySetResult(true);   // cleanup
        client.Close();
    }

    [Test]
    public async Task Posted_work_exception_is_logged_and_session_survives()
    {
        await using var h = await Harness.StartAsync();
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        Assert.That(session.Post(() => throw new InvalidOperationException("posted-boom")), Is.True);

        // requests on the same session still succeed after the exception — proves the session survives
        var reply = await client.RequestAsync<GetGoldResponse>(new GetGoldRequest()).AsTask().WaitAsync(Timeout);
        Assert.That(reply.IsOk, Is.True);
        Assert.That(h.Logger.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("posted work")),
            Is.True, "the work exception must be logged");
        client.Close();
    }

    [Test]
    public async Task Slow_work_triggers_watchdog_warning_and_completes()
    {
        await using var h = await Harness.StartAsync(slowWorkWarning: TimeSpan.FromMilliseconds(50));
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Post(async () =>
        {
            await Task.Delay(250);
            done.TrySetResult(true);
        });

        await done.Task.WaitAsync(Timeout);   // watchdog only logs — the work still runs to completion
        Assert.That(h.Logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("blocked")),
            Is.True, "the watchdog warning must be logged");
        client.Close();
    }

    [Test]
    public async Task Fast_work_produces_no_watchdog_warning()
    {
        await using var h = await Harness.StartAsync(slowWorkWarning: TimeSpan.FromMilliseconds(200));
        var client = await h.ConnectAsync();
        var session = await h.GetSessionAsync();

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Post(() => { done.TrySetResult(true); return default; });
        await done.Task.WaitAsync(Timeout);
        await Task.Delay(300);   // a spurious delayed warning would surface within this window

        Assert.That(h.Logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("blocked")), Is.False);
        client.Close();
    }
}
