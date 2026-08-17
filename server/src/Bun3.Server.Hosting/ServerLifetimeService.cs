using Bun3.Server.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>ServerBase 계열 서버를 호스트 수명에 연결한다. TServer로 닫힌 타입이 갈리므로
/// 서버 등록이 여러 개여도 TryAddEnumerable에 조용히 떨어지지 않는다
/// (중복 등록 자체는 AddServerTransport가 등록 시점에 차단한다).</summary>
internal sealed class ServerLifetimeService<TServer, TSession> : IHostedService
    where TServer : ServerBase<TSession>
    where TSession : Session
{
    private readonly TServer _server;
    private readonly IOptions<ServerOptions> _options;

    public ServerLifetimeService(TServer server, IOptions<ServerOptions> options)
    {
        _server = server;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        _server.StopAsync(_options.Value.DrainTimeout, cancellationToken);
}
