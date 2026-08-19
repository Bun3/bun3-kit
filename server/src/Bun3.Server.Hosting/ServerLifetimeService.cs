using Bun3.Server.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>Ties a ServerBase-family server to the host lifetime. The closed type varies per
/// TServer, so multiple server registrations do not silently collapse into TryAddEnumerable
/// (duplicate registration itself is blocked at registration time by AddServerTransport).</summary>
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
