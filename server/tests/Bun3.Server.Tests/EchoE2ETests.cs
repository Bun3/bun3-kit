using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class EchoE2ETests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class EchoServer : ServerBase<EchoSession>
    {
        public EchoServer(ITransportListener transport) : base(transport) { }

        protected override EchoSession CreateSession(IConnection connection) => new(connection);
    }

    private static async Task<(EchoServer server, TcpTransportListener listener)> StartEchoServerAsync()
    {
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        var server = new EchoServer(listener);
        await server.StartAsync();
        return (server, listener);
    }

    private static async Task<TcpClient> ConnectAsync(TcpTransportListener listener)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);
        return client;
    }

    private static async Task AssertEchoAsync(NetworkStream stream, string message)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        await FrameFormat.WriteFrameAsync(stream, payload);
        var echoed = await FrameFormat.ReadFrameAsync(stream, 1024 * 1024).AsTask().WaitAsync(Timeout);
        Assert.That(echoed, Is.EqualTo(payload));
    }

    [Test]
    public async Task Client_receives_echo_of_each_frame()
    {
        var (server, listener) = await StartEchoServerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            var stream = client.GetStream();

            await AssertEchoAsync(stream, "hello");
            await AssertEchoAsync(stream, "bun3");
            await AssertEchoAsync(stream, "server");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task Two_clients_are_echoed_independently()
    {
        var (server, listener) = await StartEchoServerAsync();
        try
        {
            using var clientA = await ConnectAsync(listener);
            using var clientB = await ConnectAsync(listener);

            await AssertEchoAsync(clientA.GetStream(), "from A");
            await AssertEchoAsync(clientB.GetStream(), "from B");
            await AssertEchoAsync(clientA.GetStream(), "A again");

            Assert.That(server.Sessions, Has.Count.EqualTo(2));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Test]
    public async Task StopAsync_disconnects_client_gracefully()
    {
        var (server, listener) = await StartEchoServerAsync();
        using var client = await ConnectAsync(listener);
        var stream = client.GetStream();
        await AssertEchoAsync(stream, "warm-up"); // 세션 수립 보장

        await server.StopAsync();

        // 서버가 연결을 닫았으므로 클라이언트 읽기는 깨끗한 EOF(null) 또는 IO 예외로 끝난다
        try
        {
            var frame = await FrameFormat.ReadFrameAsync(stream, 1024 * 1024).AsTask().WaitAsync(Timeout);
            Assert.That(frame, Is.Null);
        }
        catch (IOException)
        {
            // RST로 끝나는 플랫폼 변형도 허용
        }
        Assert.That(server.IsRunning, Is.False);
        Assert.That(server.Sessions, Is.Empty);
    }

    [Test]
    public async Task New_connection_after_stop_is_refused()
    {
        var (server, listener) = await StartEchoServerAsync();
        var port = listener.BoundPort!.Value; // Stop 전에 캡처
        await server.StopAsync();

        var late = new TcpClient();
        Assert.ThrowsAsync<SocketException>(async () =>
            await late.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout));
    }
}
