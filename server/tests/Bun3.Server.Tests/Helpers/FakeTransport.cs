using System.Collections.Concurrent;
using Bun3.Server.Abstractions;

namespace Bun3.Server.Tests.Helpers;

public sealed class FakeTransport : ITransportListener
{
    private IConnectionHandler? _handler;

    public bool Started { get; private set; }
    public bool Stopped { get; private set; }

    public Task StartAsync(IConnectionHandler handler, CancellationToken ct = default)
    {
        _handler = handler;
        Started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        Stopped = true;
        return Task.CompletedTask;
    }

    /// <summary>Simulates a client connection.</summary>
    public FakeConnection Connect(long id)
    {
        var connection = new FakeConnection(id, this);
        _handler!.OnConnected(connection);
        return connection;
    }

    internal void RaisePacket(FakeConnection connection, byte[] packet) => _handler!.OnPacket(connection, packet);

    internal void RaiseClosed(FakeConnection connection, Exception? error) => _handler!.OnClosed(connection, error);
}

public sealed class FakeConnection : IConnection
{
    private readonly FakeTransport _transport;
    private int _closed;

    public FakeConnection(long id, FakeTransport transport)
    {
        Id = id;
        _transport = transport;
    }

    public long Id { get; }
    public string? RemoteAddress => "fake";
    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public readonly ConcurrentQueue<byte[]> SentPackets = new();
    public readonly SemaphoreSlim SentSignal = new(0);

    public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default)
    {
        if (IsOpen)
        {
            SentPackets.Enqueue(packet.ToArray());
            SentSignal.Release();
        }
        return default;
    }

    // Note: unlike the real TcpConnection, OnClosed is raised synchronously on the calling thread — do not write tests that depend on this synchrony.
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _transport.RaiseClosed(this, null);
        }
    }

    /// <summary>Simulates a packet arriving from the remote side.</summary>
    public void ReceivePacket(byte[] packet) => _transport.RaisePacket(this, packet);

    /// <summary>Simulates the remote side disconnecting with an error.</summary>
    public void FailWith(Exception error)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _transport.RaiseClosed(this, error);
        }
    }
}
