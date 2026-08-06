using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Messaging;
using Bun3.Server.Messaging.ControlMessages;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Tests.Helpers;
using Google.Protobuf;
using NUnit.Framework;
using static Bun3.Server.Tests.Helpers.PacketTestHelper;

namespace Bun3.Server.Tests;

[TestFixture]
public class MessagingServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class TestSession : MessagingSession
    {
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TestSession(IConnection connection) : base(connection) { }

        protected override ValueTask OnSessionClosedAsync(Exception? error)
        {
            Closed.TrySetResult(error);
            return default;
        }
    }

    private static MessagingConfig<TestSession> DefaultConfig()
    {
        var config = new MessagingConfig<TestSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 123 }));
        config.OnRequest<BuyItemRequest, BuyItemResponse>((s, req) =>
        {
            if (req.ItemId == 666) throw new InvalidOperationException("boom");
            if (req.ItemId == 1) return new ValueTask<Reply<BuyItemResponse>>(Reply.Fail(-1001));
            return new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse { RemainingGold = 1000 + req.ItemId });
        });
        return config;
    }

    private static async Task<(MessagingServer<TestSession, Request, Response, Update> server, FakeTransport transport)>
        StartAsync(MessagingServerOptions? options = null, MessagingConfig<TestSession>? config = null)
    {
        var transport = new FakeTransport();
        var server = new MessagingServer<TestSession, Request, Response, Update>(
            transport, conn => new TestSession(conn), config ?? DefaultConfig(), options);
        await server.StartAsync();
        return (server, transport);
    }

    private static async Task<(byte Channel, T Message)> NextSentAsync<T>(FakeConnection conn, MessageParser<T> parser)
        where T : class, IMessage<T>
    {
        await conn.SentSignal.WaitAsync(Timeout);
        Assert.That(conn.SentPackets.TryDequeue(out var packet), Is.True);
        return (packet![0], parser.ParseFrom(packet.AsSpan(1).ToArray()));
    }

    [Test]
    public async Task Request_roundtrip_returns_ok_response_with_same_request_id()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);

        conn.ReceivePacket(Wrap(Channels.Request, new Request
        {
            RequestId = 7,
            GetServerTime = new GetServerTimeRequest(),
        }));

        var (channel, response) = await NextSentAsync(conn, Response.Parser);
        Assert.That(channel, Is.EqualTo(Channels.Response));
        Assert.That(response.RequestId, Is.EqualTo(7));
        Assert.That(response.Status, Is.EqualTo(0));
        Assert.That(response.GetServerTime.UnixMs, Is.EqualTo(123));
        await server.StopAsync();
    }

    [Test]
    public async Task Failed_reply_returns_status_without_body()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 8, BuyItem = new BuyItemRequest { ItemId = 1 } }));

        var (_, response) = await NextSentAsync(conn, Response.Parser);
        Assert.That(response.Status, Is.EqualTo(-1001));
        Assert.That(response.BodyCase, Is.EqualTo(Response.BodyOneofCase.None));
        await server.StopAsync();
    }

    [Test]
    public async Task Handler_exception_returns_status_2_and_keeps_session()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 9, BuyItem = new BuyItemRequest { ItemId = 666 } }));
        var (_, errorResponse) = await NextSentAsync(conn, Response.Parser);
        Assert.That(errorResponse.Status, Is.EqualTo(2));
        Assert.That(conn.IsOpen, Is.True);

        // 세션이 살아 있어 후속 요청이 정상 처리된다
        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 10, GetServerTime = new GetServerTimeRequest() }));
        var (_, next) = await NextSentAsync(conn, Response.Parser);
        Assert.That(next.Status, Is.EqualTo(0));
        await server.StopAsync();
    }

    [Test]
    public async Task Unknown_channel_kicks_the_session()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        conn.ReceivePacket(new byte[] { 0x7F, 1, 2, 3 });

        await session.Closed.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        await server.StopAsync();
    }

    [Test]
    public async Task Malformed_request_body_kicks_the_session()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        conn.ReceivePacket(new byte[] { Channels.Request, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

        await session.Closed.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        await server.StopAsync();
    }

    [Test]
    public async Task Client_sending_response_channel_is_a_violation()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        conn.ReceivePacket(Wrap(Channels.Response, new Response { RequestId = 1 }));

        await session.Closed.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        await server.StopAsync();
    }

    [Test]
    public async Task Ping_is_answered_with_echoing_pong()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);

        conn.ReceivePacket(Wrap(Channels.Control, new Control { Ping = new Ping { ClientTimeUnixMs = 555 } }));

        var (channel, control) = await NextSentAsync(conn, Control.Parser);
        Assert.That(channel, Is.EqualTo(Channels.Control));
        Assert.That(control.BodyCase, Is.EqualTo(Control.BodyOneofCase.Pong));
        Assert.That(control.Pong.ClientTimeUnixMs, Is.EqualTo(555));
        Assert.That(conn.IsOpen, Is.True);
        await server.StopAsync();
    }

    [Test]
    public async Task SendUpdateAsync_wraps_payload_into_update_envelope()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        await session.SendUpdateAsync(new BroadcastedUpdate { Text = "hi" });

        var (channel, update) = await NextSentAsync(conn, Update.Parser);
        Assert.That(channel, Is.EqualTo(Channels.Update));
        Assert.That(update.Broadcasted.Text, Is.EqualTo("hi"));
        await server.StopAsync();
    }

    [Test]
    public async Task SendUpdateAsync_with_type_outside_oneof_throws()
    {
        var (server, transport) = await StartAsync();
        transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await session.SendUpdateAsync(new BuyItemRequest()));
        await server.StopAsync();
    }

    [Test]
    public void Incomplete_config_fails_server_construction()
    {
        var config = new MessagingConfig<TestSession>();  // 아무 핸들러 없음
        Assert.Throws<MessagingValidationException>(() =>
            new MessagingServer<TestSession, Request, Response, Update>(
                new FakeTransport(), conn => new TestSession(conn), config));
    }

    private sealed class StrictSession : MessagingSession
    {
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StrictSession(IConnection connection) : base(connection) { }

        protected override ErrorDecision OnHandlerError(Exception ex) => ErrorDecision.CloseSession;

        protected override ValueTask OnSessionClosedAsync(Exception? error)
        {
            Closed.TrySetResult(error);
            return default;
        }
    }

    [Test]
    public async Task OnHandlerError_override_can_close_instead_of_status2()
    {
        var transport = new FakeTransport();
        var config = new MessagingConfig<StrictSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse()));
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => throw new InvalidOperationException("boom"));
        var server = new MessagingServer<StrictSession, Request, Response, Update>(
            transport, conn => new StrictSession(conn), config);
        await server.StartAsync();
        var conn = transport.Connect(1);
        var session = server.Sessions.Single();

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 1, BuyItem = new BuyItemRequest { ItemId = 666 } }));

        await session.Closed.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        Assert.That(conn.SentPackets.IsEmpty, Is.True);   // 응답 없이 종료
        await server.StopAsync();
    }

    [Test]
    public async Task Idle_session_is_kicked_after_timeout()
    {
        var (server, transport) = await StartAsync(new MessagingServerOptions
        {
            IdleKickTimeout = TimeSpan.FromMilliseconds(200),
        });
        var conn = transport.Connect(1);
        var session = (TestSession)server.Sessions.Single();

        await session.Closed.Task.WaitAsync(Timeout);   // 패킷 없이 방치 → 킥
        Assert.That(conn.IsOpen, Is.False);
        await server.StopAsync();
    }

    private sealed class OpenThrowsSession : MessagingSession
    {
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OpenThrowsSession(IConnection connection) : base(connection) { }

        protected override ValueTask OnSessionOpenedAsync() => throw new InvalidOperationException("load failed");

        protected override ValueTask OnSessionClosedAsync(Exception? error)
        {
            Closed.TrySetResult(error);
            return default;
        }
    }

    [Test]
    public async Task Throwing_OnSessionOpened_kicks_the_session()
    {
        var transport = new FakeTransport();
        var config = new MessagingConfig<OpenThrowsSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>(
            (s, req) => new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse()));
        config.OnRequest<BuyItemRequest, BuyItemResponse>(
            (s, req) => new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));
        // 세션을 팩토리 클로저로 캡처한다: OnSessionOpenedAsync가 동기적으로 던지므로
        // FakeTransport의 동기 콜백 체인상 Connect()가 반환하기 전에 이미 킥·제거가 끝나
        // server.Sessions.Single()은 빈 컬렉션을 볼 수 있다(SessionActorTests의
        // Kick_during_OnConnected_still_disconnects_cleanly와 동일한 패턴).
        OpenThrowsSession? session = null;
        var server = new MessagingServer<OpenThrowsSession, Request, Response, Update>(
            transport, conn => session = new OpenThrowsSession(conn), config);
        await server.StartAsync();

        var conn = transport.Connect(1);

        await session!.Closed.Task.WaitAsync(Timeout);
        Assert.That(conn.IsOpen, Is.False);
        Assert.That(server.Sessions, Is.Empty);
        await server.StopAsync();
    }
}
