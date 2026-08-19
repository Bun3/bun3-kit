using Bun3.Server.Abstractions;

namespace Bun3.Server.Tests.Helpers;

/// <summary>In-memory connector joining the client and server ends synchronously. For RpcClient unit tests.</summary>
public sealed class InMemoryConnector : IConnector
{
    private readonly IConnectionHandler _serverHandler;

    public InMemoryConnector(IConnectionHandler serverHandler) => _serverHandler = serverHandler;

    public DuplexConnection? ServerConnection { get; private set; }

    public ValueTask<IConnection> ConnectAsync(IConnectionHandler clientHandler, CancellationToken ct = default)
    {
        var link = new DuplexLink();
        var client = new DuplexConnection(1, link, clientHandler);
        var server = new DuplexConnection(2, link, _serverHandler);
        client.Peer = server;
        server.Peer = client;
        ServerConnection = server;

        clientHandler.OnConnected(client);
        _serverHandler.OnConnected(server);
        return new ValueTask<IConnection>(client);
    }
}

internal sealed class DuplexLink
{
    private int _closed;

    public bool TryClose() => Interlocked.Exchange(ref _closed, 1) == 0;

    public bool IsClosed => Volatile.Read(ref _closed) != 0;
}

public sealed class DuplexConnection : IConnection
{
    private readonly DuplexLink _link;
    private readonly IConnectionHandler _handler;

    internal DuplexConnection(long id, DuplexLink link, IConnectionHandler handler)
    {
        Id = id;
        _link = link;
        _handler = handler;
    }

    internal DuplexConnection? Peer { get; set; }

    public long Id { get; }
    public string? RemoteAddress => "in-memory";
    public bool IsOpen => !_link.IsClosed;

    public ValueTask SendAsync(ReadOnlyMemory<byte> packet, CancellationToken ct = default)
    {
        if (IsOpen && Peer != null)
        {
            // ownership-transfer contract — the sender's buffer cannot be handed over, so transfer a copy
            Peer._handler.OnPacket(Peer, packet.ToArray());
        }
        return default;
    }

    public void Close()
    {
        if (!_link.TryClose())
        {
            return;
        }

        _handler.OnClosed(this, null);
        Peer?._handler.OnClosed(Peer!, null);
    }
}
