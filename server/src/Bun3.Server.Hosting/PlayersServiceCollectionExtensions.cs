using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Ticking;
using Bun3.Server.Transport.Tcp;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>Extensions that register an Rpc server with Player lifecycle into the Generic Host.</summary>
public static class PlayersServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Players + Rpc server (TCP). On start, the server begins accepting before the
    /// tick loop starts; on stop, the order is tick loop → server drain → retire all players
    /// (RetireAllAsync — save flush). TSession needs a public constructor taking IConnection;
    /// remaining arguments are DI-injected. ticking takes TickLoop options (tick interval, etc.);
    /// jobs takes a callback to register game-wide jobs before TickLoop.Start (the player tick
    /// job is registered automatically). Call at most once per host.
    /// </summary>
    public static IServiceCollection AddPlayerServer<TSession, TPlayer, TRequest, TResponse, TUpdate>(
        this IServiceCollection services,
        Func<IServiceProvider, string, ValueTask<TPlayer>> loader,
        Action<PlayersConfig<TSession>> configure,
        Action<ServerOptions>? serverOptions = null,
        Action<PlayersOptions>? playersOptions = null,
        Action<TickingOptions>? ticking = null,
        Action<TickLoop>? jobs = null)
        where TSession : PlayerSession<TPlayer>
        where TPlayer : Player
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddServerTransport(serverOptions);

        // Same options pipeline as ServerOptions — appsettings ("Bun3:Players") binding, then the lambda.
        var playersOptionsBuilder = services.AddOptions<PlayersOptions>()
            .BindConfiguration(PlayersOptions.SectionName);
        if (playersOptions != null)
        {
            playersOptionsBuilder.Configure(playersOptions);
        }

        services.AddSingleton(sp => new PlayerRegistry<TPlayer>(
            key => loader(sp, key),
            sp.GetRequiredService<IOptions<PlayersOptions>>().Value,
            ServerServiceCollectionExtensions.ResolveLogger(sp)));

        services.AddSingleton(sp =>
        {
            var tickingOptions = new TickingOptions();
            ticking?.Invoke(tickingOptions);
            var loop = new TickLoop(tickingOptions, ServerServiceCollectionExtensions.ResolveLogger(sp));
            new PlayerTicker<TPlayer>(
                    sp.GetRequiredService<PlayerRegistry<TPlayer>>(),
                    sp.GetRequiredService<IOptions<PlayersOptions>>().Value,
                    ServerServiceCollectionExtensions.ResolveLogger(sp))
                .Register(loop);
            jobs?.Invoke(loop);   // Game-wide jobs — satisfies the register-before-Start rule.
            return loop;
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            var config = new PlayersConfig<TSession>();
            configure(config);

            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);

            return new RpcServer<TSession, TRequest, TResponse, TUpdate>(
                sp.GetRequiredService<TcpTransportListener>(),
                sp.GetRequiredService<PlayerRegistry<TPlayer>>().Wrap(config, Factory),
                config.Rpc,
                new RpcServerOptions
                {
                    MaxQueuedPackets = options.MaxQueuedPacketsPerSession,
                    SlowWorkWarning = options.SlowWorkWarning,
                },
                ServerServiceCollectionExtensions.ResolveLogger(sp));
        });

        services.AddHostedService(sp => new PlayersLifetimeService<TSession, TPlayer, TRequest, TResponse, TUpdate>(
            sp.GetRequiredService<RpcServer<TSession, TRequest, TResponse, TUpdate>>(),
            sp.GetRequiredService<PlayerRegistry<TPlayer>>(),
            sp.GetRequiredService<TickLoop>(),
            sp.GetRequiredService<IOptions<ServerOptions>>()));

        return services;
    }
}

/// <summary>Server + tick-loop lifetime, plus retiring all players on stop. The closed generic
/// differs per server, so duplicate registrations do not silently collapse into TryAddEnumerable.</summary>
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

    public PlayersLifetimeService(
        RpcServer<TSession, TRequest, TResponse, TUpdate> server,
        PlayerRegistry<TPlayer> registry,
        TickLoop tickLoop,
        IOptions<ServerOptions> options)
    {
        _server = server;
        _registry = registry;
        _tickLoop = tickLoop;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _server.StartAsync(cancellationToken).ConfigureAwait(false);
        _tickLoop.Start();   // Start ticking after the server is accepting.
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _tickLoop.StopAsync(cancellationToken).ConfigureAwait(false);   // Stop ticking first — blocks new tick work during shutdown.
            await _server.StopAsync(_options.Value.DrainTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The final save must be attempted even if earlier steps were canceled or failed —
            // skipping it loses connected players' progress. Since this is the last chance to save
            // even when the shutdown deadline (cancellationToken) is exhausted, no token is passed.
            await _registry.RetireAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
