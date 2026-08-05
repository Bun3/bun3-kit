using Bun3.Server.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

internal sealed class ServerHostedService<TSession> : IHostedService where TSession : Session
{
    private readonly HostedServer<TSession> _server;
    private readonly IOptions<ServerOptions> _options;

    public ServerHostedService(HostedServer<TSession> server, IOptions<ServerOptions> options)
    {
        _server = server;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        _server.StopAsync(_options.Value.DrainTimeout, cancellationToken);
}
