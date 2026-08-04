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

    /// <summary>클라이언트 접속을 시뮬레이션한다.</summary>
    public FakeConnection Connect(long id)
    {
        var connection = new FakeConnection(id, this);
        _handler!.OnConnected(connection);
        return connection;
    }

    internal void RaiseFrame(FakeConnection connection, byte[] frame) => _handler!.OnFrame(connection, frame);

    internal void RaiseClosed(FakeConnection connection, Exception? error) => _handler!.OnClosed(connection, error);
}

public sealed class FakeConnection : IConnection
{
    private readonly FakeTransport _transport;
    private readonly List<byte[]> _sentFrames = new();
    private int _closed;

    public FakeConnection(long id, FakeTransport transport)
    {
        Id = id;
        _transport = transport;
    }

    public long Id { get; }
    public string? RemoteAddress => "fake";
    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public IReadOnlyList<byte[]> SentFrames
    {
        get { lock (_sentFrames) return _sentFrames.ToArray(); }
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        if (IsOpen)
        {
            lock (_sentFrames) _sentFrames.Add(frame.ToArray());
        }
        return default;
    }

    // 주의: 실제 TcpConnection과 달리 OnClosed를 호출 스레드에서 동기로 올린다 — 이 동기성에 의존하는 테스트를 작성하지 말 것.
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _transport.RaiseClosed(this, null);
        }
    }

    /// <summary>원격에서 프레임이 도착한 것을 시뮬레이션한다.</summary>
    public void ReceiveFrame(byte[] frame) => _transport.RaiseFrame(this, frame);

    /// <summary>원격이 오류로 끊긴 것을 시뮬레이션한다.</summary>
    public void FailWith(Exception error)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            _transport.RaiseClosed(this, error);
        }
    }
}
