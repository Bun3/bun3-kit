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
    /// tick loop starts; on stop, admission closes before tick loop → server drain → retire all players
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
        return AddPlayerServerCore<TSession, TPlayer, TRequest, TResponse, TUpdate>(
            services, sp => new PlayerRegistry<TPlayer>(
                key => loader(sp, key),
                sp.GetRequiredService<IOptions<PlayersOptions>>().Value,
                ServerServiceCollectionExtensions.ResolveLogger(sp)),
            configure, serverOptions, playersOptions, ticking, jobs);
    }

    /// <summary>Registers a Players + Rpc server backed by one DI-created player store.
    /// The registry and ticker share that registry, and shutdown closes admission before draining work.</summary>
    public static IServiceCollection AddPlayerServer<TSession, TPlayer, TRequest, TResponse, TUpdate>(
        this IServiceCollection services,
        Func<IServiceProvider, PlayerStore<TPlayer>> store,
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
        ArgumentNullException.ThrowIfNull(store);
        return AddPlayerServer<TSession, TPlayer, TRequest, TResponse, TUpdate>(services,
            new Func<IServiceProvider, PlayerPersistence<TPlayer>>(sp => store(sp)), configure, serverOptions, playersOptions, ticking, jobs);
    }

    /// <summary>Registers a server using versioned or authority-protected player persistence.</summary>
    public static IServiceCollection AddPlayerServer<TSession, TPlayer, TRequest, TResponse, TUpdate>(
        this IServiceCollection services,
        Func<IServiceProvider, PlayerPersistence<TPlayer>> store,
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
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configure);
        return AddPlayerServerCore<TSession, TPlayer, TRequest, TResponse, TUpdate>(
            services, sp => new PlayerRegistry<TPlayer>(
                store(sp) ?? throw new InvalidOperationException("Player store factory returned null."),
                sp.GetRequiredService<IOptions<PlayersOptions>>().Value,
                ServerServiceCollectionExtensions.ResolveLogger(sp)),
            configure, serverOptions, playersOptions, ticking, jobs);
    }

    private static IServiceCollection AddPlayerServerCore<TSession, TPlayer, TRequest, TResponse, TUpdate>(
        IServiceCollection services,
        Func<IServiceProvider, PlayerRegistry<TPlayer>> registry,
        Action<PlayersConfig<TSession>> configure,
        Action<ServerOptions>? serverOptions,
        Action<PlayersOptions>? playersOptions,
        Action<TickingOptions>? ticking,
        Action<TickLoop>? jobs)
        where TSession : PlayerSession<TPlayer>
        where TPlayer : Player
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {

        services.AddServerTransport(serverOptions);

        // Same options pipeline as ServerOptions — appsettings ("Bun3:Players") binding, then the lambda.
        var playersOptionsBuilder = services.AddOptions<PlayersOptions>()
            .BindConfiguration(PlayersOptions.SectionName);
        if (playersOptions != null)
        {
            playersOptionsBuilder.Configure(playersOptions);
        }

        services.AddSingleton(registry);

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
            sp.GetRequiredService<IOptions<ServerOptions>>(),
            sp.GetService<PlayerCommandWorker<TPlayer>>()));

        return services;
    }
}
