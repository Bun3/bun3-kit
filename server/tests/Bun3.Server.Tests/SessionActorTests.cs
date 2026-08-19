using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class SessionActorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // ---- test session/server ----

    private sealed class ScriptedSession : Session
    {
        private readonly Func<ScriptedSession, ReadOnlyMemory<byte>, ValueTask> _onPacket;
        private readonly Func<Exception, ErrorDecision>? _onError;
        public readonly TaskCompletionSource<Exception?> Disconnected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ScriptedSession(
            IConnection connection,
            Func<ScriptedSession, ReadOnlyMemory<byte>, ValueTask> onPacket,
            Func<Exception, ErrorDecision>? onError = null)
            : base(connection)
        {
            _onPacket = onPacket;
            _onError = onError;
        }

        protected override ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet) => _onPacket(this, packet);

        protected override ErrorDecision OnHandlerError(Exception ex) =>
            _onError?.Invoke(ex) ?? base.OnHandlerError(ex);

        protected override ValueTask OnDisconnectedAsync(Exception? error)
        {
            Disconnected.TrySetResult(error);
            return default;
        }
    }

    private sealed class TestServer : ServerBase<ScriptedSession>
    {
        private readonly Func<IConnection, ScriptedSession> _factory;

        public TestServer(
            ITransportListener transport,
            Func<IConnection, ScriptedSession> factory,
            int maxQueuedPackets = 256,
            ILogger? logger = null)
            : base(transport, logger, maxQueuedPackets)
        {
            _factory = factory;
        }

        protected override ScriptedSession CreateSession(IConnection connection) => _factory(connection);
    }

    // ---- tests ----

    [Test]
    public async Task Packets_are_processed_in_order()
    {
        var transport = new FakeTransport();
        var processed = new List<int>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, packet) =>
        {
            processed.Add(BitConverter.ToInt32(packet.Span));
            if (processed.Count == 100) done.TrySetResult();
            return default;
        }));
        await server.StartAsync();

        var conn = transport.Connect(1);
        for (var i = 0; i < 100; i++) conn.ReceivePacket(BitConverter.GetBytes(i));

        await done.Task.WaitAsync(Timeout);
        Assert.That(processed, Is.EqualTo(Enumerable.Range(0, 100)));
    }

    [Test]
    public async Task Handlers_of_one_session_never_overlap()
    {
        var transport = new FakeTransport();
        var concurrent = 0;
        var overlapped = false;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var server = new TestServer(transport, conn => new ScriptedSession(conn, async (_, _) =>
        {
            var now = Interlocked.Increment(ref concurrent);
            if (now > 1) Volatile.Write(ref overlapped, true);
            await Task.Delay(1);
            Interlocked.Decrement(ref concurrent);
            if (Interlocked.Increment(ref count) == 50) done.TrySetResult();
        }));
        await server.StartAsync();

        var conn = transport.Connect(1);
        for (var i = 0; i < 50; i++) conn.ReceivePacket(new byte[] { 1 });

        await done.Task.WaitAsync(Timeout);
        Assert.That(Volatile.Read(ref overlapped), Is.False);
    }

    [Test]
    public async Task Inbox_overflow_kicks_the_connection()
    {
        var transport = new FakeTransport();
        var firstPacketEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(
            transport,
            conn => new ScriptedSession(conn, async (_, _) =>
            {
                firstPacketEntered.TrySetResult();
                await release.Task; // block on the first packet to build up the queue
            }),
            maxQueuedPackets: 8);
        await server.StartAsync();

        var conn = transport.Connect(1);
        conn.ReceivePacket(new byte[] { 0 });
        await firstPacketEntered.Task.WaitAsync(Timeout);
        var session = server.Sessions.Single(); // capture the session before shutdown
        for (var i = 0; i < 20; i++) conn.ReceivePacket(new byte[] { 1 }); // exceeds 8

        release.TrySetResult(); // unblock so the loop consumes the shutdown signal
        await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
    }

    [Test]
    public async Task Handler_exception_closes_session_by_default()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(conn,
            (_, _) => throw new InvalidOperationException("boom")));
        await server.StartAsync();

        var conn = transport.Connect(1);
        var session = server.Sessions.Single();
        conn.ReceivePacket(new byte[] { 1 });

        await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
    }

    [Test]
    public async Task Throwing_logger_does_not_zombify_session_on_handler_error()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(conn,
            (_, _) => throw new InvalidOperationException("boom")), logger: new ThrowingLogger());
        await server.StartAsync();

        var conn = transport.Connect(1);
        var session = server.Sessions.Single();
        conn.ReceivePacket(new byte[] { 1 });

        await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        Assert.That(server.Sessions, Is.Empty);
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("logger failure");
    }

    [Test]
    public async Task OnHandlerError_Continue_keeps_session_alive()
    {
        var transport = new FakeTransport();
        var processed = new List<byte>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(transport, conn => new ScriptedSession(
            conn,
            (_, packet) =>
            {
                if (packet.Span[0] == 1) throw new InvalidOperationException("boom");
                processed.Add(packet.Span[0]);
                done.TrySetResult();
                return default;
            },
            onError: _ => ErrorDecision.Continue));
        await server.StartAsync();

        var conn = transport.Connect(1);
        conn.ReceivePacket(new byte[] { 1 }); // throws — ignored
        conn.ReceivePacket(new byte[] { 2 }); // must still be processed

        await done.Task.WaitAsync(Timeout);
        Assert.That(processed, Is.EqualTo(new byte[] { 2 }));
        Assert.That(conn.IsOpen, Is.True);
    }

    [Test]
    public async Task Remote_close_fires_OnDisconnected_with_error_and_removes_session()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, _) => default));
        await server.StartAsync();

        var conn = transport.Connect(1);
        var session = server.Sessions.Single();
        var error = new IOException("connection reset");
        conn.FailWith(error);

        var received = await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(received, Is.SameAs(error));
        Assert.That(server.Sessions, Is.Empty);
    }

    [Test]
    public async Task StopAsync_kicks_all_sessions_and_stops_transport()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, _) => default));
        await server.StartAsync();
        var c1 = transport.Connect(1);
        var c2 = transport.Connect(2);
        var sessions = server.Sessions.ToArray();

        await server.StopAsync();

        Assert.That(transport.Stopped, Is.True);
        Assert.That(c1.IsOpen, Is.False);
        Assert.That(c2.IsOpen, Is.False);
        foreach (var s in sessions)
        {
            await s.Disconnected.Task.WaitAsync(Timeout);
        }
        Assert.That(server.IsRunning, Is.False);
    }

    [Test]
    public async Task Queued_packets_before_close_are_dropped()
    {
        var transport = new FakeTransport();
        var processed = new List<byte>();
        var firstPacketEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(transport, conn => new ScriptedSession(conn, async (_, packet) =>
        {
            processed.Add(packet.Span[0]);
            firstPacketEntered.TrySetResult();
            await release.Task; // block on the first packet — later packets stay queued
        }));
        await server.StartAsync();

        var conn = transport.Connect(1);
        var session = server.Sessions.Single();
        conn.ReceivePacket(new byte[] { 1 });
        await firstPacketEntered.Task.WaitAsync(Timeout);
        conn.ReceivePacket(new byte[] { 2 });
        conn.ReceivePacket(new byte[] { 3 });

        conn.Close(); // close signal — queued 2 and 3 must not be processed
        release.TrySetResult();

        await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(processed, Is.EqualTo(new byte[] { 1 }));
    }

    [Test]
    public async Task Throwing_OnHandlerError_closes_session()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(
            conn,
            (_, _) => throw new InvalidOperationException("boom"),
            onError: _ => throw new InvalidOperationException("hook failure")));
        await server.StartAsync();

        var conn = transport.Connect(1);
        var session = server.Sessions.Single();
        conn.ReceivePacket(new byte[] { 1 });

        await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        Assert.That(server.Sessions, Is.Empty);
    }

    [Test]
    public async Task Duplicate_connection_id_closes_new_connection_and_keeps_existing()
    {
        var transport = new FakeTransport();
        var processed = new List<byte>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, packet) =>
        {
            processed.Add(packet.Span[0]);
            done.TrySetResult();
            return default;
        }));
        await server.StartAsync();

        var first = transport.Connect(1);
        var session = server.Sessions.Single();
        var duplicate = transport.Connect(1); // simulate a transport contract violation — same id reused

        Assert.That(duplicate.IsOpen, Is.False); // only the new connection is rejected
        Assert.That(first.IsOpen, Is.True);
        Assert.That(server.Sessions.Single(), Is.SameAs(session)); // existing session kept

        first.ReceivePacket(new byte[] { 7 }); // existing session keeps working
        await done.Task.WaitAsync(Timeout);
        Assert.That(processed, Is.EqualTo(new byte[] { 7 }));
    }

    [Test]
    public async Task Double_OnClosed_notifies_session_once_and_is_harmless()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, _) => default));
        await server.StartAsync();

        var conn = transport.Connect(1);
        var session = server.Sessions.Single();

        conn.Close(); // first OnClosed
        await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.DoesNotThrow(() => transport.RaiseClosed(conn, new IOException("late"))); // second — must be harmless
        Assert.That(server.Sessions, Is.Empty);
        Assert.That(await session.Disconnected.Task, Is.Null); // first result (null) is kept
    }

    [Test]
    public async Task Connection_after_stop_is_closed_immediately()
    {
        var transport = new FakeTransport();
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, _) => default));
        await server.StartAsync();
        await server.StopAsync();

        var conn = transport.Connect(1);

        Assert.That(conn.IsOpen, Is.False);
        Assert.That(server.Sessions, Is.Empty);
    }

    [Test]
    public async Task StopAsync_returns_when_ct_cancels_during_drain()
    {
        var transport = new FakeTransport();
        var firstPacketEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(transport, conn => new ScriptedSession(conn, async (_, _) =>
        {
            firstPacketEntered.TrySetResult();
            await release.Task; // session that cannot drain
        }));
        await server.StartAsync();
        var conn = transport.Connect(1);
        conn.ReceivePacket(new byte[] { 1 });
        await firstPacketEntered.Task.WaitAsync(Timeout);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await server.StopAsync(TimeSpan.FromSeconds(30), cts.Token).WaitAsync(Timeout); // returns via cancel instead of the 30s wait

        release.TrySetResult(); // clean up the blocked handler
    }

    [Test]
    public async Task Kick_during_OnConnected_still_disconnects_cleanly()
    {
        var transport = new FakeTransport();
        RejectingSession? created = null;
        var server = new RejectingServer(transport, conn => created = new RejectingSession(conn));
        await server.StartAsync();

        var conn = transport.Connect(1);

        await created!.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        Assert.That(server.Sessions, Is.Empty);
    }

    private sealed class RejectingSession : Session
    {
        public readonly TaskCompletionSource<Exception?> Disconnected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RejectingSession(IConnection connection) : base(connection) { }

        protected override ValueTask OnConnectedAsync()
        {
            Kick();
            return default;
        }

        protected override ValueTask OnPacketAsync(ReadOnlyMemory<byte> packet) => default;

        protected override ValueTask OnDisconnectedAsync(Exception? error)
        {
            Disconnected.TrySetResult(error);
            return default;
        }
    }

    private sealed class RejectingServer : ServerBase<RejectingSession>
    {
        private readonly Func<IConnection, RejectingSession> _factory;

        public RejectingServer(ITransportListener transport, Func<IConnection, RejectingSession> factory)
            : base(transport)
        {
            _factory = factory;
        }

        protected override RejectingSession CreateSession(IConnection connection) => _factory(connection);
    }
}
