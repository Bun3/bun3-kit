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
public class RpcExecutionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class HookSession : RpcSession
    {
        public readonly List<string> Events = new();
        public HookSession(IConnection connection) : base(connection) { }

        protected override async ValueTask<(int Status, IMessage? Response)> InvokeRequestAsync(
            IMessage request, RpcRequestHandler handler)
        {
            Events.Add("enter");
            try { return await handler(this, request); }
            finally { Events.Add("exit"); }
        }

        protected override int OnGateRequest(Type requestType) =>
            requestType == typeof(BuyItemRequest) ? 7 : RpcStatus.Ok;
    }

    private static async Task<(RpcServer<HookSession, Request, Response, Update> Server, FakeTransport Transport)>
        StartAsync(Func<HookSession, ValueTask<Reply<GetServerTimeResponse>>> handler)
    {
        var config = new RpcConfig<HookSession>();
        config.OnRequest<GetServerTimeRequest, GetServerTimeResponse>((s, _) => handler(s));
        config.OnRequest<BuyItemRequest, BuyItemResponse>((_, _) =>
            new ValueTask<Reply<BuyItemResponse>>(new BuyItemResponse()));
        var transport = new FakeTransport();
        var server = new RpcServer<HookSession, Request, Response, Update>(
            transport, conn => new HookSession(conn), config);
        await server.StartAsync();
        return (server, transport);
    }

    private static async Task<Response> ResponseAsync(FakeConnection conn)
    {
        await conn.SentSignal.WaitAsync(Timeout);
        Assert.That(conn.SentPackets.TryDequeue(out var packet), Is.True);
        return Response.Parser.ParseFrom(packet!.AsSpan(1).ToArray());
    }

    [Test]
    public async Task Request_hook_encloses_handler_and_async_completion()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (server, transport) = await StartAsync(async session =>
        {
            session.Events.Add("handler-start");
            entered.TrySetResult();
            await release.Task;
            session.Events.Add("handler-end");
            return new GetServerTimeResponse { UnixMs = 12 };
        });
        try
        {
            var conn = transport.Connect(1);
            var session = server.Sessions.Single();
            conn.ReceivePacket(Wrap(Channels.Request,
                new Request { RequestId = 1, GetServerTime = new GetServerTimeRequest() }));
            await entered.Task.WaitAsync(Timeout);
            Assert.That(session.Events, Is.EqualTo(new[] { "enter", "handler-start" }));
            release.TrySetResult();
            Assert.That((await ResponseAsync(conn)).Status, Is.EqualTo(RpcStatus.Ok));
            Assert.That(session.Events, Is.EqualTo(new[] { "enter", "handler-start", "handler-end", "exit" }));
        }
        finally { release.TrySetResult(); await server.StopAsync(); }
    }

    [Test]
    public async Task Request_hook_exits_when_handler_throws()
    {
        var (server, transport) = await StartAsync(session =>
        {
            session.Events.Add("handler-start");
            throw new InvalidOperationException("failure");
        });
        try
        {
            var conn = transport.Connect(1);
            var session = server.Sessions.Single();
            conn.ReceivePacket(Wrap(Channels.Request,
                new Request { RequestId = 1, GetServerTime = new GetServerTimeRequest() }));
            Assert.That((await ResponseAsync(conn)).Status, Is.EqualTo(RpcStatus.HandlerException));
            Assert.That(session.Events, Is.EqualTo(new[] { "enter", "handler-start", "exit" }));
        }
        finally { await server.StopAsync(); }
    }

    [Test]
    public async Task Gate_rejection_and_ping_skip_request_hook()
    {
        var (server, transport) = await StartAsync(_ =>
            throw new AssertionException("handler should not run"));
        try
        {
            var conn = transport.Connect(1);
            var session = server.Sessions.Single();
            conn.ReceivePacket(Wrap(Channels.Request,
                new Request { RequestId = 1, BuyItem = new BuyItemRequest { ItemId = 4 } }));
            Assert.That((await ResponseAsync(conn)).Status, Is.EqualTo(7));
            conn.ReceivePacket(Wrap(Channels.Control,
                new Control { Ping = new Ping { ClientTimeUnixMs = 9 } }));
            await conn.SentSignal.WaitAsync(Timeout);
            Assert.That(conn.SentPackets.TryDequeue(out _), Is.True);
            Assert.That(session.Events, Is.Empty);
        }
        finally { await server.StopAsync(); }
    }
}
