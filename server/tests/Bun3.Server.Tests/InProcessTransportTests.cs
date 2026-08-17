using System.Text;
using Bun3.Server.Abstractions;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Transport.InProcess;
using NUnit.Framework;

namespace Bun3.Server.Tests;

/// <summary>
/// Transport.Tcp 계약 테스트(TcpTransportTests/TcpConnectorTests)와 동일한 시나리오를
/// 인프로세스 전송에 적용하고, 인프로세스 고유 계약(복사, 드레인, 백프레셔)을 추가로 검증한다.
/// </summary>
[TestFixture]
public class InProcessTransportTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static async Task<(InProcessTransport transport, RecordingHandler serverHandler)> StartTransportAsync(
        int maxQueuedPackets = 256)
    {
        var serverHandler = new RecordingHandler();
        var transport = new InProcessTransport(maxQueuedPackets);
        await transport.Listener.StartAsync(serverHandler);
        return (transport, serverHandler);
    }

    [Test]
    public async Task Connect_raises_client_OnConnected_before_returning()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        var clientHandler = new RecordingHandler();

        var connection = await transport.Connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);

        Assert.That(clientHandler.Connected.Task.IsCompletedSuccessfully, Is.True); // 반환 전에 이미 호출됨
        Assert.That(connection.IsOpen, Is.True);
        Assert.That(connection.Id, Is.GreaterThan(0));
        var serverConn = await serverHandler.Connected.Task.WaitAsync(Timeout);
        Assert.That(serverConn.IsOpen, Is.True);
        connection.Close();
    }

    [Test]
    public void Connect_before_start_throws()
    {
        var transport = new InProcessTransport();
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.Connector.ConnectAsync(new RecordingHandler()));
    }

    [Test]
    public async Task Connect_after_stop_throws()
    {
        var (transport, _) = await StartTransportAsync();
        await transport.Listener.StopAsync();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.Connector.ConnectAsync(new RecordingHandler()));
    }

    [Test]
    public async Task Stop_does_not_close_existing_connections()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        var clientHandler = new RecordingHandler();
        var clientConn = await transport.Connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);
        var serverConn = await serverHandler.Connected.Task.WaitAsync(Timeout);

        await transport.Listener.StopAsync();

        Assert.That(clientConn.IsOpen, Is.True);
        await clientConn.SendAsync(Encoding.UTF8.GetBytes("still alive"));
        await serverHandler.PacketSignal.WaitAsync(Timeout);
        Assert.That(serverHandler.Packets.TryDequeue(out var received), Is.True);
        Assert.That(received, Is.EqualTo(Encoding.UTF8.GetBytes("still alive")));
        Assert.That(serverConn.IsOpen, Is.True);
        clientConn.Close();
    }

    [Test]
    public async Task Packets_flow_both_directions()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        var clientHandler = new RecordingHandler();
        var clientConn = await transport.Connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);
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

    [Test]
    public async Task Received_packet_is_a_copy_of_the_send_buffer()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        var clientConn = await transport.Connector.ConnectAsync(new RecordingHandler()).AsTask().WaitAsync(Timeout);
        var buffer = new byte[] { 1, 2, 3 };

        await clientConn.SendAsync(buffer);
        await serverHandler.PacketSignal.WaitAsync(Timeout);
        buffer[0] = 99; // 송신자가 버퍼를 재사용해도

        Assert.That(serverHandler.Packets.TryDequeue(out var received), Is.True);
        Assert.That(received, Is.EqualTo(new byte[] { 1, 2, 3 })); // 수신 패킷은 영향을 받지 않는다
        Assert.That(received, Is.Not.SameAs(buffer));
        clientConn.Close();
    }

    [Test]
    public async Task Close_raises_OnClosed_null_on_both_ends()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        var clientHandler = new RecordingHandler();
        var clientConn = await transport.Connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);
        var serverConn = await serverHandler.Connected.Task.WaitAsync(Timeout);

        clientConn.Close();

        Assert.That(await clientHandler.Closed.Task.WaitAsync(Timeout), Is.Null);
        Assert.That(await serverHandler.Closed.Task.WaitAsync(Timeout), Is.Null);
        Assert.That(clientConn.IsOpen, Is.False);
        Assert.That(serverConn.IsOpen, Is.False);
    }

    [Test]
    public async Task OnClosed_fires_exactly_once_per_endpoint_even_when_both_ends_close()
    {
        var serverHandler = new CountingClosedHandler();
        var transport = new InProcessTransport();
        await transport.Listener.StartAsync(serverHandler);
        var clientHandler = new CountingClosedHandler();
        var clientConn = await transport.Connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);
        var serverConn = serverHandler.Connection!;

        clientConn.Close();
        serverConn.Close(); // 양쪽에서 동시에 닫아도
        clientConn.Close(); // 멱등

        await clientHandler.ClosedOnce.Task.WaitAsync(Timeout);
        await serverHandler.ClosedOnce.Task.WaitAsync(Timeout);
        await Task.Delay(100); // 중복 통지가 있다면 드러날 시간
        Assert.That(clientHandler.ClosedCount, Is.EqualTo(1));
        Assert.That(serverHandler.ClosedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Packets_sent_before_close_are_drained_to_peer()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        // 서버 펌프가 느려도 순서가 보장되는지 보기 위해 여러 개를 보낸 직후 닫는다
        var clientConn = await transport.Connector.ConnectAsync(new RecordingHandler()).AsTask().WaitAsync(Timeout);
        for (byte i = 0; i < 5; i++)
        {
            await clientConn.SendAsync(new[] { i });
        }

        clientConn.Close();

        var error = await serverHandler.Closed.Task.WaitAsync(Timeout);
        Assert.That(error, Is.Null);
        Assert.That(serverHandler.Packets.Count, Is.EqualTo(5)); // 큐잉분은 OnClosed 전에 전부 전달
        for (byte i = 0; i < 5; i++)
        {
            Assert.That(serverHandler.Packets.TryDequeue(out var p), Is.True);
            Assert.That(p, Is.EqualTo(new[] { i }));
        }
    }

    [Test]
    public async Task Send_after_close_is_noop()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        var clientHandler = new RecordingHandler();
        var clientConn = await transport.Connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);

        clientConn.Close();
        await clientHandler.Closed.Task.WaitAsync(Timeout);
        await serverHandler.Closed.Task.WaitAsync(Timeout);

        Assert.DoesNotThrowAsync(async () => await clientConn.SendAsync(new byte[] { 1 }));
        Assert.That(serverHandler.Packets, Is.Empty);
    }

    [Test]
    public async Task Two_connections_get_distinct_ids_on_both_ends()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        var h1 = new RecordingHandler();
        var h2 = new RecordingHandler();

        var c1 = await transport.Connector.ConnectAsync(h1).AsTask().WaitAsync(Timeout);
        var c2 = await transport.Connector.ConnectAsync(h2).AsTask().WaitAsync(Timeout);

        await serverHandler.ConnectedSignal.WaitAsync(Timeout);
        await serverHandler.ConnectedSignal.WaitAsync(Timeout);
        var serverIds = serverHandler.ConnectionIds.ToArray();
        var allIds = new[] { c1.Id, c2.Id, serverIds[0], serverIds[1] };
        Assert.That(allIds, Is.Unique); // 클라/서버 끝점 4개 모두 서로 다른 Id
        c1.Close();
        c2.Close();
    }

    [Test]
    public async Task Throwing_client_OnConnected_propagates_and_closes_the_pair()
    {
        var (transport, serverHandler) = await StartTransportAsync();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.Connector.ConnectAsync(new ThrowingConnectHandler()).AsTask().WaitAsync(Timeout));

        // 페어가 정리되었음을 서버 측 종료 통지로 증명한다(TcpConnector 계약과 동일)
        await serverHandler.Connected.Task.WaitAsync(Timeout);
        Assert.That(await serverHandler.Closed.Task.WaitAsync(Timeout), Is.Null);
    }

    [Test]
    public async Task Throwing_server_OnConnected_still_connects_client_then_closes_it()
    {
        var serverHandler = new ThrowingConnectHandler();
        var transport = new InProcessTransport();
        await transport.Listener.StartAsync(serverHandler);
        var clientHandler = new RecordingHandler();

        // 원격 거부의 TCP 관측과 동일: 클라 연결 자체는 성공하고 곧 닫힌다
        var clientConn = await transport.Connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);

        Assert.That(clientHandler.Connected.Task.IsCompletedSuccessfully, Is.True);
        Assert.That(await clientHandler.Closed.Task.WaitAsync(Timeout), Is.Null);
        Assert.That(clientConn.IsOpen, Is.False);

        // 서버 OnConnected가 던져도 수락은 계속된다(Tcp의 accept 루프 생존 계약과 동일)
        var secondClient = new RecordingHandler();
        var second = await transport.Connector.ConnectAsync(secondClient).AsTask().WaitAsync(Timeout);
        Assert.That(secondClient.Connected.Task.IsCompletedSuccessfully, Is.True);
        Assert.That(await secondClient.Closed.Task.WaitAsync(Timeout), Is.Null); // 이번에도 거부되지만 수락 자체는 산다
        _ = second;
    }

    [Test]
    public async Task Throwing_OnPacket_closes_connection_with_that_error()
    {
        var serverHandler = new ThrowOnPacketHandler();
        var transport = new InProcessTransport();
        await transport.Listener.StartAsync(serverHandler);
        var clientHandler = new RecordingHandler();
        var clientConn = await transport.Connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);

        await clientConn.SendAsync(new byte[] { 1 });

        var error = await serverHandler.Closed.Task.WaitAsync(Timeout);
        Assert.That(error, Is.InstanceOf<InvalidOperationException>()); // Tcp 수신 루프와 동일
        Assert.That(await clientHandler.Closed.Task.WaitAsync(Timeout), Is.Null);
    }

    [Test]
    public async Task Full_inbox_applies_backpressure_and_close_releases_blocked_sender()
    {
        var serverHandler = new BlockingPacketHandler();
        var transport = new InProcessTransport(maxQueuedPacketsPerConnection: 2);
        await transport.Listener.StartAsync(serverHandler);
        var clientConn = await transport.Connector.ConnectAsync(new RecordingHandler()).AsTask().WaitAsync(Timeout);

        // 펌프가 1개를 꺼내 블록 + 인박스 2개 = 3개까지는 즉시, 4번째는 슬롯 대기
        await clientConn.SendAsync(new byte[] { 1 }).AsTask().WaitAsync(Timeout);
        await serverHandler.Entered.Task.WaitAsync(Timeout); // 펌프가 OnPacket에서 블록됨을 확정
        await clientConn.SendAsync(new byte[] { 2 }).AsTask().WaitAsync(Timeout);
        await clientConn.SendAsync(new byte[] { 3 }).AsTask().WaitAsync(Timeout);
        var blocked = clientConn.SendAsync(new byte[] { 4 }).AsTask();

        await Task.Delay(200);
        Assert.That(blocked.IsCompleted, Is.False); // 백프레셔로 대기 중

        // 수신 측이 닫히면 대기 중인 송신자는 예외 없이 풀려난다
        var serverConn = serverHandler.Connection!;
        serverConn.Close();
        serverHandler.Unblock.Release();
        Assert.DoesNotThrowAsync(async () => await blocked.WaitAsync(Timeout));
    }

    private sealed class ThrowingConnectHandler : IConnectionHandler
    {
        public void OnConnected(IConnection connection) => throw new InvalidOperationException("reject");
        public void OnPacket(IConnection connection, byte[] packet) { }
        public void OnClosed(IConnection connection, Exception? error) { }
    }

    private sealed class ThrowOnPacketHandler : IConnectionHandler
    {
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnConnected(IConnection connection) { }
        public void OnPacket(IConnection connection, byte[] packet) => throw new InvalidOperationException("boom");
        public void OnClosed(IConnection connection, Exception? error) => Closed.TrySetResult(error);
    }

    private sealed class BlockingPacketHandler : IConnectionHandler
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly SemaphoreSlim Unblock = new(0);
        public IConnection? Connection;

        public void OnConnected(IConnection connection) => Connection = connection;

        public void OnPacket(IConnection connection, byte[] packet)
        {
            Entered.TrySetResult();
            Unblock.Wait(TimeSpan.FromSeconds(5));
        }

        public void OnClosed(IConnection connection, Exception? error) { }
    }

    private sealed class CountingClosedHandler : IConnectionHandler
    {
        public readonly TaskCompletionSource ClosedOnce = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IConnection? Connection;
        private int _closedCount;

        public int ClosedCount => Volatile.Read(ref _closedCount);

        public void OnConnected(IConnection connection) => Connection = connection;
        public void OnPacket(IConnection connection, byte[] packet) { }

        public void OnClosed(IConnection connection, Exception? error)
        {
            Interlocked.Increment(ref _closedCount);
            ClosedOnce.TrySetResult();
        }
    }
}
