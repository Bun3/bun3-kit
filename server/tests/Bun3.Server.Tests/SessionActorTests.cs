using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Tests.Helpers;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class SessionActorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // ---- 테스트용 세션/서버 ----

    private sealed class ScriptedSession : Session
    {
        private readonly Func<ScriptedSession, ReadOnlyMemory<byte>, ValueTask> _onFrame;
        private readonly Func<Exception, ErrorDecision>? _onError;
        public readonly TaskCompletionSource<Exception?> Disconnected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ScriptedSession(
            IConnection connection,
            Func<ScriptedSession, ReadOnlyMemory<byte>, ValueTask> onFrame,
            Func<Exception, ErrorDecision>? onError = null)
            : base(connection)
        {
            _onFrame = onFrame;
            _onError = onError;
        }

        protected override ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame) => _onFrame(this, frame);

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
            int maxQueuedFrames = 256,
            IServerLogger? logger = null)
            : base(transport, logger, maxQueuedFrames)
        {
            _factory = factory;
        }

        protected override ScriptedSession CreateSession(IConnection connection) => _factory(connection);
    }

    // ---- 테스트 ----

    [Test]
    public async Task Frames_are_processed_in_order()
    {
        var transport = new FakeTransport();
        var processed = new List<int>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(transport, conn => new ScriptedSession(conn, (_, frame) =>
        {
            processed.Add(BitConverter.ToInt32(frame.Span));
            if (processed.Count == 100) done.TrySetResult();
            return default;
        }));
        await server.StartAsync();

        var conn = transport.Connect(1);
        for (var i = 0; i < 100; i++) conn.ReceiveFrame(BitConverter.GetBytes(i));

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
        for (var i = 0; i < 50; i++) conn.ReceiveFrame(new byte[] { 1 });

        await done.Task.WaitAsync(Timeout);
        Assert.That(Volatile.Read(ref overlapped), Is.False);
    }

    [Test]
    public async Task Inbox_overflow_kicks_the_connection()
    {
        var transport = new FakeTransport();
        var firstFrameEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestServer(
            transport,
            conn => new ScriptedSession(conn, async (_, _) =>
            {
                firstFrameEntered.TrySetResult();
                await release.Task; // 첫 프레임에서 블록 → 큐 적체 유도
            }),
            maxQueuedFrames: 8);
        await server.StartAsync();

        var conn = transport.Connect(1);
        conn.ReceiveFrame(new byte[] { 0 });
        await firstFrameEntered.Task.WaitAsync(Timeout);
        var session = server.Sessions.Single(); // 종료 전에 세션 캡처
        for (var i = 0; i < 20; i++) conn.ReceiveFrame(new byte[] { 1 }); // 8개 초과

        release.TrySetResult(); // 블록 해제 → 루프가 종료 신호를 소비
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
        conn.ReceiveFrame(new byte[] { 1 });

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
        conn.ReceiveFrame(new byte[] { 1 });

        await session.Disconnected.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        Assert.That(server.Sessions, Is.Empty);
    }

    private sealed class ThrowingLogger : IServerLogger
    {
        public void Log(ServerLogLevel level, string message, Exception? exception = null) =>
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
            (_, frame) =>
            {
                if (frame.Span[0] == 1) throw new InvalidOperationException("boom");
                processed.Add(frame.Span[0]);
                done.TrySetResult();
                return default;
            },
            onError: _ => ErrorDecision.Continue));
        await server.StartAsync();

        var conn = transport.Connect(1);
        conn.ReceiveFrame(new byte[] { 1 }); // 예외 — 무시됨
        conn.ReceiveFrame(new byte[] { 2 }); // 계속 처리되어야 함

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

        protected override ValueTask OnFrameAsync(ReadOnlyMemory<byte> frame) => default;

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
