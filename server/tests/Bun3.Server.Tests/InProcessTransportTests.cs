using System.Text;
using Bun3.Server.Abstractions;
using Bun3.Server.Tests.Helpers;
using Bun3.Server.Transport.InProcess;
using NUnit.Framework;

namespace Bun3.Server.Tests;

/// <summary>
/// Applies the Transport.Tcp contract scenarios (TcpTransportTests/TcpConnectorTests) to the
/// in-process transport, plus in-process-specific contracts (copying, draining, backpressure).
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

        Assert.That(clientHandler.Connected.Task.IsCompletedSuccessfully, Is.True); // already invoked before return
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
        buffer[0] = 99; // sender reuses the buffer

        Assert.That(serverHandler.Packets.TryDequeue(out var received), Is.True);
        Assert.That(received, Is.EqualTo(new byte[] { 1, 2, 3 })); // received packet is unaffected
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
        serverConn.Close(); // both ends closing concurrently
        clientConn.Close(); // idempotent

        await clientHandler.ClosedOnce.Task.WaitAsync(Timeout);
        await serverHandler.ClosedOnce.Task.WaitAsync(Timeout);
        await Task.Delay(100); // time for any duplicate notification to surface
        Assert.That(clientHandler.ClosedCount, Is.EqualTo(1));
        Assert.That(serverHandler.ClosedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Packets_sent_before_close_are_drained_to_peer()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        // send several then close immediately, to verify ordering even with a slow server pump
        var clientConn = await transport.Connector.ConnectAsync(new RecordingHandler()).AsTask().WaitAsync(Timeout);
        for (byte i = 0; i < 5; i++)
        {
            await clientConn.SendAsync(new[] { i });
        }

        clientConn.Close();

        var error = await serverHandler.Closed.Task.WaitAsync(Timeout);
        Assert.That(error, Is.Null);
        Assert.That(serverHandler.Packets.Count, Is.EqualTo(5)); // queued packets all delivered before OnClosed
        for (byte i = 0; i < 5; i++)
        {
            Assert.That(serverHandler.Packets.TryDequeue(out var p), Is.True);
            Assert.That(p, Is.EqualTo(new[] { i }));
        }
    }

    [Test]
    public async Task Send_with_precanceled_token_throws_and_keeps_connection_open()
    {
        var (transport, serverHandler) = await StartTransportAsync();
        var clientConn = await transport.Connector.ConnectAsync(new RecordingHandler()).AsTask().WaitAsync(Timeout);

        var canceled = new CancellationToken(canceled: true);
        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientConn.SendAsync(new byte[] { 1 }, canceled));
        Assert.That(clientConn.IsOpen, Is.True); // cancellation does not close the connection

        await clientConn.SendAsync(new byte[] { 2 }); // subsequent sends work normally
        await serverHandler.PacketSignal.WaitAsync(Timeout);
        Assert.That(serverHandler.Packets.TryDequeue(out var received), Is.True);
        Assert.That(received, Is.EqualTo(new byte[] { 2 }));
        clientConn.Close();
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
        Assert.That(allIds, Is.Unique); // all 4 client/server endpoints have distinct ids
        c1.Close();
        c2.Close();
    }

    [Test]
    public async Task Throwing_client_OnConnected_propagates_and_closes_the_pair()
    {
        var (transport, serverHandler) = await StartTransportAsync();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.Connector.ConnectAsync(new ThrowingConnectHandler()).AsTask().WaitAsync(Timeout));

        // server-side close notification proves the pair was cleaned up (same as TcpConnector contract)
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

        // same as TCP observation of a remote reject: client connect itself succeeds, then closes soon
        var clientConn = await transport.Connector.ConnectAsync(clientHandler).AsTask().WaitAsync(Timeout);

        Assert.That(clientHandler.Connected.Task.IsCompletedSuccessfully, Is.True);
        Assert.That(await clientHandler.Closed.Task.WaitAsync(Timeout), Is.Null);
        Assert.That(clientConn.IsOpen, Is.False);

        // accepting continues even if server OnConnected throws (same as Tcp accept-loop survival contract)
        var secondClient = new RecordingHandler();
        await transport.Connector.ConnectAsync(secondClient).AsTask().WaitAsync(Timeout);
        Assert.That(secondClient.Connected.Task.IsCompletedSuccessfully, Is.True);
        Assert.That(await secondClient.Closed.Task.WaitAsync(Timeout), Is.Null); // rejected again, but accepting survives
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
        Assert.That(error, Is.InstanceOf<InvalidOperationException>()); // same as Tcp receive loop
        Assert.That(await clientHandler.Closed.Task.WaitAsync(Timeout), Is.Null);
    }

    [Test]
    public async Task Full_inbox_applies_backpressure_and_close_releases_blocked_sender()
    {
        var serverHandler = new BlockingPacketHandler();
        var transport = new InProcessTransport(maxQueuedPacketsPerConnection: 2);
        await transport.Listener.StartAsync(serverHandler);
        var clientConn = await transport.Connector.ConnectAsync(new RecordingHandler()).AsTask().WaitAsync(Timeout);

        // pump dequeues 1 and blocks + inbox holds 2 = 3 go through immediately, 4th waits for a slot
        await clientConn.SendAsync(new byte[] { 1 }).AsTask().WaitAsync(Timeout);
        await serverHandler.Entered.Task.WaitAsync(Timeout); // confirm pump is blocked in OnPacket
        await clientConn.SendAsync(new byte[] { 2 }).AsTask().WaitAsync(Timeout);
        await clientConn.SendAsync(new byte[] { 3 }).AsTask().WaitAsync(Timeout);
        var blocked = clientConn.SendAsync(new byte[] { 4 }).AsTask();

        await Task.Delay(200);
        Assert.That(blocked.IsCompleted, Is.False); // waiting due to backpressure

        // when the receiving side closes, the waiting sender is released without an exception
        var serverConn = serverHandler.Connection!;
        serverConn.Close();
        serverHandler.Unblock.Release();
        Assert.DoesNotThrowAsync(async () => await blocked.WaitAsync(Timeout));
    }

    [Test]
    public async Task Local_close_releases_sender_blocked_on_peer_backpressure()
    {
        var serverHandler = new BlockingPacketHandler();
        var transport = new InProcessTransport(maxQueuedPacketsPerConnection: 2);
        await transport.Listener.StartAsync(serverHandler);
        var clientConn = await transport.Connector.ConnectAsync(new RecordingHandler()).AsTask().WaitAsync(Timeout);

        await clientConn.SendAsync(new byte[] { 1 }).AsTask().WaitAsync(Timeout);
        await serverHandler.Entered.Task.WaitAsync(Timeout);
        await clientConn.SendAsync(new byte[] { 2 }).AsTask().WaitAsync(Timeout);
        await clientConn.SendAsync(new byte[] { 3 }).AsTask().WaitAsync(Timeout);
        var blocked = clientConn.SendAsync(new byte[] { 4 }).AsTask();
        await Task.Delay(100);
        Assert.That(blocked.IsCompleted, Is.False);

        // close the sender side — even with the peer (server) pump stuck in OnPacket, the blocked
        // send must resolve as a no-op without an exception (like TCP local Close waking a blocked write)
        clientConn.Close();
        Assert.DoesNotThrowAsync(async () => await blocked.WaitAsync(Timeout));

        // resuming the pump drains only the pre-close queued packets (2, 3); the 4th is dropped
        serverHandler.Unblock.Release(3);
        var error = await serverHandler.Closed.Task.WaitAsync(Timeout);
        Assert.That(error, Is.Null);
        Assert.That(serverHandler.PacketCount, Is.EqualTo(3));
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
        public readonly TaskCompletionSource<Exception?> Closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly SemaphoreSlim Unblock = new(0);
        public IConnection? Connection;
        private int _packetCount;

        public int PacketCount => Volatile.Read(ref _packetCount);

        public void OnConnected(IConnection connection) => Connection = connection;

        public void OnPacket(IConnection connection, byte[] packet)
        {
            Interlocked.Increment(ref _packetCount);
            Entered.TrySetResult();
            Unblock.Wait(TimeSpan.FromSeconds(5));
        }

        public void OnClosed(IConnection connection, Exception? error) => Closed.TrySetResult(error);
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
