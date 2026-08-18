using System.Net;
using System.Net.Sockets;
using System.Text;
using Bun3.Common.Network;
using Bun3.Server.Abstractions;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Transport.Tcp;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class TcpTransportTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static async Task<(TcpTransportListener listener, RecordingHandler handler)> StartListenerAsync(
        int maxPacketSize = 1024 * 1024)
    {
        var handler = new RecordingHandler();
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0, MaxPacketSize = maxPacketSize });
        await listener.StartAsync(handler);
        return (listener, handler);
    }

    private static async Task<TcpClient> ConnectAsync(TcpTransportListener listener)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);
        return client;
    }

    [Test]
    public async Task Start_on_port_zero_reports_bound_port()
    {
        var (listener, _) = await StartListenerAsync();
        try
        {
            Assert.That(listener.BoundPort, Is.Not.Null);
            Assert.That(listener.BoundPort, Is.GreaterThan(0));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Client_connect_raises_OnConnected_with_remote_address()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            var connection = await handler.Connected.Task.WaitAsync(Timeout);

            Assert.That(connection.IsOpen, Is.True);
            Assert.That(connection.Id, Is.GreaterThan(0));
            Assert.That(connection.RemoteAddress, Does.Contain("127.0.0.1"));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Connections_over_MaxConnections_are_rejected_until_capacity_frees()
    {
        var handler = new RecordingHandler();
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0, MaxConnections = 1 });
        await listener.StartAsync(handler);
        try
        {
            using var first = await ConnectAsync(listener);
            await handler.ConnectedSignal.WaitAsync(Timeout);

            // 상한 초과 — 수락 즉시 닫힌다(핸들러 OnConnected 없이 원격 종료 관측: EOF 또는 리셋)
            using var second = await ConnectAsync(listener);
            int got;
            try
            {
                got = await second.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(Timeout);
            }
            catch (IOException)
            {
                got = 0;
            }
            Assert.That(got, Is.Zero);

            // 자리가 나면 다시 수락된다
            first.Close();
            await handler.Closed.Task.WaitAsync(Timeout);
            using var third = await ConnectAsync(listener);
            await handler.ConnectedSignal.WaitAsync(Timeout);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Client_packet_reaches_handler_intact()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            await handler.Connected.Task.WaitAsync(Timeout);
            var payload = Encoding.UTF8.GetBytes("ping from client");

            await PacketFormat.WritePacketAsync(client.GetStream(), payload);

            await handler.PacketSignal.WaitAsync(Timeout);
            Assert.That(handler.Packets.TryDequeue(out var received), Is.True);
            Assert.That(received, Is.EqualTo(payload));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Server_send_reaches_client_intact()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            var connection = await handler.Connected.Task.WaitAsync(Timeout);
            var payload = Encoding.UTF8.GetBytes("pong from server");

            await connection.SendAsync(payload);

            var received = await PacketFormat.ReadPacketAsync(client.GetStream(), 1024 * 1024)
                .AsTask().WaitAsync(Timeout);
            Assert.That(received, Is.EqualTo(payload));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Client_disconnect_raises_OnClosed_with_null_error()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            var client = await ConnectAsync(listener);
            var connection = await handler.Connected.Task.WaitAsync(Timeout);

            client.Close();

            var error = await handler.Closed.Task.WaitAsync(Timeout);
            Assert.That(error, Is.Null);
            Assert.That(connection.IsOpen, Is.False);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Remote_reset_raises_OnClosed_with_non_null_error()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            var client = await ConnectAsync(listener);
            await handler.Connected.Task.WaitAsync(Timeout);

            // TcpClient.Close는 GetStream 미호출 시 Shutdown(FIN)을 먼저 보내 EOF가 되므로,
            // 소켓을 직접 linger 0으로 닫아 RST를 강제한다.
            client.Client.LingerState = new LingerOption(true, 0);
            client.Client.Close();

            var error = await handler.Closed.Task.WaitAsync(Timeout);
            Assert.That(error, Is.Not.Null);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Send_with_precanceled_token_throws_and_keeps_connection_open()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            var connection = await handler.Connected.Task.WaitAsync(Timeout);

            var canceled = new CancellationToken(canceled: true);
            Assert.CatchAsync<OperationCanceledException>(
                async () => await connection.SendAsync(new byte[] { 1 }, canceled));
            Assert.That(connection.IsOpen, Is.True); // 취소는 연결을 닫지 않는다

            var payload = Encoding.UTF8.GetBytes("still alive");
            await connection.SendAsync(payload); // 이후 송신은 정상 동작
            var received = await PacketFormat.ReadPacketAsync(client.GetStream(), 1024 * 1024)
                .AsTask().WaitAsync(Timeout);
            Assert.That(received, Is.EqualTo(payload));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Oversize_packet_closes_connection_with_InvalidDataException()
    {
        var (listener, handler) = await StartListenerAsync(maxPacketSize: 16);
        try
        {
            using var client = await ConnectAsync(listener);
            await handler.Connected.Task.WaitAsync(Timeout);

            await PacketFormat.WritePacketAsync(client.GetStream(), new byte[17]);

            var error = await handler.Closed.Task.WaitAsync(Timeout);
            Assert.That(error, Is.InstanceOf<InvalidDataException>());
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Send_after_close_is_noop()
    {
        var (listener, handler) = await StartListenerAsync();
        try
        {
            using var client = await ConnectAsync(listener);
            var connection = await handler.Connected.Task.WaitAsync(Timeout);

            connection.Close();
            await handler.Closed.Task.WaitAsync(Timeout);

            Assert.DoesNotThrowAsync(async () => await connection.SendAsync(new byte[] { 1 }));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Two_connections_get_distinct_ids()
    {
        var handler = new RecordingHandler();
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        await listener.StartAsync(handler);
        try
        {
            using var c1 = new TcpClient();
            using var c2 = new TcpClient();
            await c1.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);
            await c2.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);

            await handler.ConnectedSignal.WaitAsync(Timeout);
            await handler.ConnectedSignal.WaitAsync(Timeout);
            var ids = handler.ConnectionIds.ToArray();
            Assert.That(ids, Has.Length.EqualTo(2));
            Assert.That(ids[0], Is.Not.EqualTo(ids[1]));
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Test]
    public async Task Throwing_OnConnected_does_not_kill_the_accept_loop()
    {
        var handler = new ThrowOnFirstConnectHandler();
        var listener = new TcpTransportListener(new TcpTransportOptions { Port = 0 });
        await listener.StartAsync(handler);
        try
        {
            using var first = new TcpClient();
            await first.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);

            using var second = new TcpClient();
            await second.ConnectAsync(IPAddress.Loopback, listener.BoundPort!.Value);

            var connection = await handler.SecondConnected.Task.WaitAsync(Timeout);
            Assert.That(connection.IsOpen, Is.True);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    private sealed class ThrowOnFirstConnectHandler : IConnectionHandler
    {
        private int _count;
        public readonly TaskCompletionSource<IConnection> SecondConnected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnConnected(IConnection connection)
        {
            if (Interlocked.Increment(ref _count) == 1) throw new InvalidOperationException("reject");
            SecondConnected.TrySetResult(connection);
        }

        public void OnPacket(IConnection connection, byte[] packet) { }
        public void OnClosed(IConnection connection, Exception? error) { }
    }
}
