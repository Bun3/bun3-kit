using Bun3.Server.Abstractions;
using Bun3.Server.Core;

namespace Bun3.Server.Hosting;

/// <summary>DI가 제공하는 세션 팩토리로 CreateSession을 구현하는 호스팅용 서버.</summary>
internal sealed class HostedServer<TSession> : ServerBase<TSession> where TSession : Session
{
    private readonly Func<IConnection, TSession> _sessionFactory;

    public HostedServer(
        ITransportListener transport,
        Func<IConnection, TSession> sessionFactory,
        IServerLogger logger,
        int maxQueuedPackets)
        : base(transport, logger, maxQueuedPackets)
    {
        _sessionFactory = sessionFactory;
    }

    protected override TSession CreateSession(IConnection connection) => _sessionFactory(connection);
}
