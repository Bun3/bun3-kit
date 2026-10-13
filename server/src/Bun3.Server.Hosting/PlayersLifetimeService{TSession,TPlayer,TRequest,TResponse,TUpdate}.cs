using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Ticking;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

internal sealed class PlayersLifetimeService<TSession, TPlayer, TRequest, TResponse, TUpdate> : IHostedService
    where TSession : PlayerSession<TPlayer>
    where TPlayer : Player
    where TRequest : class, IMessage<TRequest>, new()
    where TResponse : class, IMessage<TResponse>, new()
    where TUpdate : class, IMessage<TUpdate>, new()
{
    private readonly RpcServer<TSession, TRequest, TResponse, TUpdate> _server;
    private readonly PlayerRegistry<TPlayer> _registry;
    private readonly TickLoop _tickLoop;
    private readonly IOptions<ServerOptions> _options;
    private readonly PlayerCommandWorker<TPlayer>? _commands;
    private readonly PlayerServerLifecycle<TPlayer>? _lifecycle;
    public PlayersLifetimeService(RpcServer<TSession, TRequest, TResponse, TUpdate> server,
        PlayerRegistry<TPlayer> registry, TickLoop tickLoop, IOptions<ServerOptions> options, PlayerCommandWorker<TPlayer>? commands = null)
    {
        _server = server; _registry = registry; _tickLoop = tickLoop; _options = options; _commands = commands;
        if (registry.WriteBinding?.Role == PlayerWriteRole.Server) _lifecycle = new(registry);
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_lifecycle != null)
        {
            var prepared = await _lifecycle.PrepareAsync(cancellationToken).ConfigureAwait(false);
            if (prepared.Outcome != PlayerWriteTransitionOutcome.Committed) throw new InvalidOperationException("Server write ownership could not be confirmed.");
        }
        try
        {
            if (_lifecycle != null)
            {
                var activated = await _lifecycle.ActivateAsync(cancellationToken).ConfigureAwait(false);
                if (activated.Outcome != PlayerWriteTransitionOutcome.Committed) throw new InvalidOperationException("Server activation could not be confirmed.");
            }
            await _server.StartAsync(cancellationToken).ConfigureAwait(false);
            _tickLoop.Start();
            if (_commands != null) await _commands.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            throw;
        }
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _commands?.StopIntake(); _registry.StopAcceptingOperations();
        if (_lifecycle == null)
        {
            try
            {
                await _tickLoop.StopAsync(cancellationToken).ConfigureAwait(false);
                await _server.StopAsync(_options.Value.DrainTimeout, cancellationToken).ConfigureAwait(false);
            }
            finally { await _registry.RetireAllAsync(CancellationToken.None).ConfigureAwait(false); }
            return;
        }
        var errors = new List<Exception>();
        try
        {
            var draining = await _lifecycle.BeginDrainAsync(cancellationToken).ConfigureAwait(false);
            if (draining.Outcome != PlayerWriteTransitionOutcome.Committed) errors.Add(new InvalidOperationException("Server drain transition is unconfirmed."));
        }
        catch (Exception ex) { errors.Add(ex); }
        try { await _tickLoop.StopAsync(cancellationToken).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        try { await _server.StopAsync(_options.Value.DrainTimeout, cancellationToken).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); }
        if (_commands != null)
        { try { await _commands.StopAsync(cancellationToken).ConfigureAwait(false); } catch (Exception ex) { errors.Add(ex); } }
        if (errors.Count == 0)
        {
            var stopped = await _lifecycle.CompleteDrainAsync(cancellationToken).ConfigureAwait(false);
            if (!stopped.OfflineConfirmed) throw new InvalidOperationException("Server shutdown did not confirm Offline write authority.");
        }
        else
        {
            await _registry.DrainAsync(cancellationToken).ConfigureAwait(false);
            throw new AggregateException("Server shutdown requires recovery; Offline was not published.", errors);
        }
    }
}
