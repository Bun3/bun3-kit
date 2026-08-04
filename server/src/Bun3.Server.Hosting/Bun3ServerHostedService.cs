using Bun3.Server.Core;
using Microsoft.Extensions.Hosting;

namespace Bun3.Server.Hosting;

internal sealed class Bun3ServerHostedService<TSession> : IHostedService where TSession : Session
{
    private readonly HostedServer<TSession> _server;

    public Bun3ServerHostedService(HostedServer<TSession> server) => _server = server;

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => _server.StopAsync(ct: cancellationToken);
}
