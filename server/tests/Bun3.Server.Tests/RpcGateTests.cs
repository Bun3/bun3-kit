using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Rpc.ControlMessages;
using Bun3.Server.Tests.GameProtocol;
using Bun3.Server.Tests.Helpers;
using Google.Protobuf;
using NUnit.Framework;
using static Bun3.Server.Tests.Helpers.PacketTestHelper;

namespace Bun3.Server.Tests;

[TestFixture]
public class RpcGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>BuyItemRequest만 상태 7로 거부하는 게이트.</summary>
    private sealed class GatedSession : RpcSession
    {
        public int HandlerCalls;

        public GatedSession(IConnection connection) : base(connection) { }

        protected override int OnGateRequest(Type requestType) =>
            requestType == typeof(BuyItemRequest) ? 7 : RpcStatus.Ok;
    }

    private static async Task<(RpcServer<GatedSession, Request, Response, Update> server, FakeTransport transport)>
        StartAsync()
    {
        var config = new RpcConfig<GatedSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>((s, req) =>
        {
            s.HandlerCalls++;
            return new ValueTask<Reply<GetServerTimeResponse>>(new GetServerTimeResponse { UnixMs = 1 });
        });
        config.OnRequest<BuyItemRequest, BuyItemResponse>((s, req) =>
        {
            s.HandlerCalls++;
            return new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse());
        });
        var transport = new FakeTransport();
        var server = new RpcServer<GatedSession, Request, Response, Update>(
            transport, conn => new GatedSession(conn), config);
        await server.StartAsync();
        return (server, transport);
    }

    private static async Task<Response> NextResponseAsync(FakeConnection conn)
    {
        await conn.SentSignal.WaitAsync(Timeout);
        Assert.That(conn.SentPackets.TryDequeue(out var packet), Is.True);
        Assert.That(packet![0], Is.EqualTo(Channels.Response));
        return Response.Parser.ParseFrom(packet.AsSpan(1).ToArray());
    }

    [Test]
    public async Task Gated_request_is_rejected_without_reaching_the_handler()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = server.Sessions.Single();

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 1, BuyItem = new BuyItemRequest { ItemId = 5 } }));

        var response = await NextResponseAsync(conn);
        Assert.That(response.Status, Is.EqualTo(7));
        Assert.That(response.RequestId, Is.EqualTo(1));
        Assert.That(response.BodyCase, Is.EqualTo(Response.BodyOneofCase.None));
        Assert.That(session.HandlerCalls, Is.EqualTo(0));
        Assert.That(conn.IsOpen, Is.True);   // 게이트 거부는 위반이 아니다 — 세션 유지
        await server.StopAsync();
    }

    [Test]
    public async Task Ungated_request_and_control_ping_pass_through()
    {
        var (server, transport) = await StartAsync();
        var conn = transport.Connect(1);
        var session = server.Sessions.Single();

        conn.ReceivePacket(Wrap(Channels.Request, new Request { RequestId = 2, GetServerTime = new GetServerTimeRequest() }));
        var response = await NextResponseAsync(conn);
        Assert.That(response.Status, Is.EqualTo(RpcStatus.Ok));
        Assert.That(session.HandlerCalls, Is.EqualTo(1));

        conn.ReceivePacket(Wrap(Channels.Control, new Control { Ping = new Ping { ClientTimeUnixMs = 9 } }));
        await conn.SentSignal.WaitAsync(Timeout);
        Assert.That(conn.SentPackets.TryDequeue(out var pong), Is.True);
        Assert.That(pong![0], Is.EqualTo(Channels.Control));   // Ping은 게이트 무관
        await server.StopAsync();
    }
}
