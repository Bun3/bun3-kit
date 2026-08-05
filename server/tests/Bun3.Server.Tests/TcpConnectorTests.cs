using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Common.Network;
using Bun3.Server.Abstractions;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class TcpConnectorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class RecordingHandler : IConnectionHandler
    {
        public readonly TaskCompletionSource<IConnection> Connected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentQueue<byte[]> Packets = new();
        public readonly SemaphoreSlim PacketSignal = new(0);

        public void OnConnected(IConnection connection) => Connected.TrySetResult(connection);

        public void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet)
        {
            Packets.Enqueue(packet.ToArray());
            PacketSignal.Release();
        }

        public void OnClosed(IConnection connection, Exception? error) => Closed.TrySetResult(error);
    }

    private static async Task<(TcpTransportListener listener, RecordingHandler serverHandler)> StartListenerAsync()
    {
        var handler = new RecordingHandler();
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        await listener.StartAsync(handler);
        return (listener, handler);
    }

    [Test]
    public async Task Connect_raises_client_OnConnected_before_returning()
    {
        var (listener, _) = await StartListenerAsync();
        try
        {
            var clientHandler = new RecordingHandler();
            var connector = new TcpConnector(new TcpConnectorOptions
            {
                Host = "127.0.0.1",
                Port = listener.BoundPort!.Value,
            });

            var connection = await connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);

            Assert.That(clientHandler.Connected.Task.IsCompletedSuccessfully, Is.True); // 반환 전에 이미 호출됨
            Assert.That(connection.IsOpen, Is.True);
            connection.Close();
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Packets_flow_both_directions()
    {
        var (listener, serverHandler) = await StartListenerAsync();
        try
        {
            var clientHandler = new RecordingHandler();
            var connector = new TcpConnector(new TcpConnectorOptions
            {
                Host = "127.0.0.1",
                Port = listener.BoundPort!.Value,
            });
            var clientConn = await connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);
            var serverConn = await serverHandler.Connected.Task.WaitAsync(Timeout);

            await clientConn.SendAsync(Encoding.UTF8.GetBytes("to server"));
            await serverHandler.PacketSignal.WaitAsync(Timeout);
            Assert.That(serverHandler.Packets.TryDequeue(out var p1), Is.True);
            Assert.That(p1, Is.EqualTo(Encoding.UTF8.GetBytes("to server")));

            await serverConn.SendAsync(Encoding.UTF8.GetBytes("to client"));
            await clientHandler.PacketSignal.WaitAsync(Timeout);
            Assert.That(clientHandler.Packets.TryDequeue(out var p2), Is.True);
            Assert.That(p2, Is.EqualTo(Encoding.UTF8.GetBytes("to client")));

            clientConn.Close();
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Server_close_raises_client_OnClosed_with_null()
    {
        var (listener, serverHandler) = await StartListenerAsync();
        try
        {
            var clientHandler = new RecordingHandler();
            var connector = new TcpConnector(new TcpConnectorOptions
            {
                Host = "127.0.0.1",
                Port = listener.BoundPort!.Value,
            });
            var clientConn = await connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);
            var serverConn = await serverHandler.Connected.Task.WaitAsync(Timeout);

            serverConn.Close();

            var error = await clientHandler.Closed.Task.WaitAsync(Timeout);
            Assert.That(error, Is.Null);
            Assert.That(clientConn.IsOpen, Is.False);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public void Connect_to_dead_port_throws_SocketException()
    {
        // 청취자 없는 포트: OS가 즉시 거부한다
        var deadPortListener = new TcpListener(IPAddress.Loopback, 0);
        deadPortListener.Start();
        var deadPort = ((IPEndPoint)deadPortListener.LocalEndpoint).Port;
        deadPortListener.Stop();

        var connector = new TcpConnector(new TcpConnectorOptions { Host = "127.0.0.1", Port = deadPort });
        Assert.ThrowsAsync<SocketException>(async () =>
            await connector.ConnectAsync(new RecordingHandler()).AsTask().WaitAsync(Timeout));
    }

    [Test]
    public async Task Throwing_OnConnected_propagates_and_closes_the_socket()
    {
        var (listener, serverHandler) = await StartListenerAsync();
        try
        {
            var connector = new TcpConnector(new TcpConnectorOptions
            {
                Host = "127.0.0.1",
                Port = listener.BoundPort!.Value,
            });

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await connector.ConnectAsync(new ThrowingHandler()).AsTask().WaitAsync(Timeout));

            // 소켓이 정리되었음을 서버 측 종료 통지로 증명한다
            await serverHandler.Connected.Task.WaitAsync(Timeout);
            var serverError = await serverHandler.Closed.Task.WaitAsync(Timeout);
            Assert.That(serverError, Is.Null);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    private sealed class ThrowingHandler : IConnectionHandler
    {
        public void OnConnected(IConnection connection) => throw new InvalidOperationException("reject");
        public void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet) { }
        public void OnClosed(IConnection connection, Exception? error) { }
    }
}
