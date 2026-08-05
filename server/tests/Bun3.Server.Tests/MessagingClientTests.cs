using Bun3.Server.Abstractions;
using Bun3.Server.Messaging;
using Bun3.Server.Messaging.ControlMessages;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Tests.Helpers;
using Google.Protobuf;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class MessagingClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>수신 원시 패킷마다 콜백을 실행하는 스크립트형 서버 대역.</summary>
    private sealed class ScriptedResponder : IConnectionHandler
    {
        public Action<IConnection, byte[]>? OnPacketReceived;
        public IConnection? Connection;

        public void OnConnected(IConnection connection) => Connection = connection;

        public void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet) =>
            OnPacketReceived?.Invoke(connection, packet.ToArray());

        public void OnClosed(IConnection connection, Exception? error) { }
    }

    private static byte[] Wrap(byte channel, IMessage message)
    {
        var body = message.ToByteArray();
        var packet = new byte[1 + body.Length];
        packet[0] = channel;
        body.CopyTo(packet, 1);
        return packet;
    }

    private static Task<MessagingClient<Request, Response, Update>> ConnectAsync(
        ScriptedResponder responder, MessagingClientOptions? options = null)
    {
        return MessagingClient<Request, Response, Update>
            .ConnectAsync(new InMemoryConnector(responder), options).AsTask();
    }

    private static void RespondOk(IConnection serverConn, byte[] packet)
    {
        var request = Request.Parser.ParseFrom(packet.AsSpan(1).ToArray());
        var response = new Response { RequestId = request.RequestId, Status = 0 };
        if (request.BodyCase == Request.BodyOneofCase.GetServerTime)
        {
            response.GetServerTime = new GetServerTimeResponse { UnixMs = 42 };
        }
        else
        {
            response.BuyItem = new BuyItemResponse { RemainingGold = 1000 + request.BuyItem.ItemId };
        }
        _ = serverConn.SendAsync(Wrap(Channels.Response, response));
    }

    [Test]
    public async Task Request_response_roundtrip()
    {
        var responder = new ScriptedResponder { OnPacketReceived = RespondOk };
        var client = await ConnectAsync(responder);

        var reply = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
            .AsTask().WaitAsync(Timeout);

        Assert.That(reply.IsOk, Is.True);
        Assert.That(reply.Value!.UnixMs, Is.EqualTo(42));
    }

    [Test]
    public async Task Failed_status_arrives_as_reply_value()
    {
        var responder = new ScriptedResponder();
        responder.OnPacketReceived = (conn, packet) =>
        {
            var request = Request.Parser.ParseFrom(packet.AsSpan(1).ToArray());
            _ = conn.SendAsync(Wrap(Channels.Response, new Response { RequestId = request.RequestId, Status = -7 }));
        };
        var client = await ConnectAsync(responder);

        var reply = await client.RequestAsync<BuyItemResponse>(new BuyItemRequest { ItemId = 3 })
            .AsTask().WaitAsync(Timeout);

        Assert.That(reply.Status, Is.EqualTo(-7));
        Assert.That(reply.Value, Is.Null);
    }

    [Test]
    public async Task Silent_server_causes_TimeoutException()
    {
        var responder = new ScriptedResponder();   // 응답하지 않음
        var client = await ConnectAsync(responder, new MessagingClientOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(200),
        });

        Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
                .AsTask().WaitAsync(Timeout));
    }

    [Test]
    public async Task Connection_close_fails_pending_requests()
    {
        var responder = new ScriptedResponder();
        responder.OnPacketReceived = (conn, _) => conn.Close();   // 응답 대신 끊음
        var client = await ConnectAsync(responder);

        Assert.ThrowsAsync<ConnectionClosedException>(async () =>
            await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
                .AsTask().WaitAsync(Timeout));
        Assert.That(client.IsConnected, Is.False);
    }

    [Test]
    public async Task Registered_update_handler_receives_push()
    {
        var responder = new ScriptedResponder();
        var connector = new InMemoryConnector(responder);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var client = await MessagingClient<Request, Response, Update>.ConnectAsync(connector).AsTask();
        client.OnUpdate<BroadcastedUpdate>(u => received.TrySetResult(u.Text));

        _ = connector.ServerConnection!.SendAsync(
            Wrap(Channels.Update, new Update { Broadcasted = new BroadcastedUpdate { Text = "hello" } }));

        Assert.That(await received.Task.WaitAsync(Timeout), Is.EqualTo("hello"));
    }

    [Test]
    public async Task Unregistered_update_is_ignored_without_closing()
    {
        var responder = new ScriptedResponder { OnPacketReceived = RespondOk };
        var connector = new InMemoryConnector(responder);
        var client = await MessagingClient<Request, Response, Update>.ConnectAsync(connector).AsTask();

        _ = connector.ServerConnection!.SendAsync(
            Wrap(Channels.Update, new Update { Broadcasted = new BroadcastedUpdate { Text = "nobody listens" } }));

        // 여전히 정상 동작
        var reply = await client.RequestAsync<GetServerTimeResponse>(new GetServerTimeRequest())
            .AsTask().WaitAsync(Timeout);
        Assert.That(reply.IsOk, Is.True);
        Assert.That(client.IsConnected, Is.True);
    }

    [Test]
    public async Task Ping_loop_measures_rtt()
    {
        var responder = new ScriptedResponder();
        responder.OnPacketReceived = (conn, packet) =>
        {
            if (packet[0] != Channels.Control) return;
            var control = Control.Parser.ParseFrom(packet.AsSpan(1).ToArray());
            if (control.BodyCase != Control.BodyOneofCase.Ping) return;
            _ = conn.SendAsync(Wrap(Channels.Control, new Control
            {
                Pong = new Pong { ClientTimeUnixMs = control.Ping.ClientTimeUnixMs },
            }));
        };
        var client = await ConnectAsync(responder, new MessagingClientOptions
        {
            PingInterval = TimeSpan.FromMilliseconds(100),
        });

        for (var i = 0; i < 50 && client.LastRttMs < 0; i++)
        {
            await Task.Delay(50);
        }

        Assert.That(client.LastRttMs, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task Mismatched_TRes_throws_ArgumentException()
    {
        var responder = new ScriptedResponder();
        var client = await ConnectAsync(responder);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await client.RequestAsync<BuyItemResponse>(new GetServerTimeRequest()));
    }
}
