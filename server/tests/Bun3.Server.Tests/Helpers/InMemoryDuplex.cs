using Bun3.Server.Abstractions;

namespace Bun3.Server.Tests.Helpers;

/// <summary>클라↔서버 양끝을 동기로 잇는 인메모리 커넥터. RpcClient 단위 검증용.</summary>
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
            // 상대편 핸들러에 동기 전달 — 버퍼는 호출 동안만 유효 계약 그대로
            Peer._handler.OnPacket(Peer, packet);
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
