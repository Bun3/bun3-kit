using System.Collections.Concurrent;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Tests.Helpers;

public sealed class RecordingHandler : IConnectionHandler
{
    public readonly TaskCompletionSource<IConnection> Connected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly TaskCompletionSource<Exception?> Closed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public readonly ConcurrentQueue<byte[]> Packets = new();
    public readonly SemaphoreSlim PacketSignal = new(0);
    public readonly ConcurrentQueue<long> ConnectionIds = new();
    public readonly SemaphoreSlim ConnectedSignal = new(0);

    public void OnConnected(IConnection connection)
    {
        ConnectionIds.Enqueue(connection.Id);
        ConnectedSignal.Release();
        Connected.TrySetResult(connection);
    }

    public void OnPacket(IConnection connection, ReadOnlyMemory<byte> packet)
    {
        Packets.Enqueue(packet.ToArray());
        PacketSignal.Release();
    }

    public void OnClosed(IConnection connection, Exception? error) => Closed.TrySetResult(error);
}
