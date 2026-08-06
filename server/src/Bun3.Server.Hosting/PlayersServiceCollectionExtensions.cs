using Bun3.Server.Abstractions;
using Bun3.Server.Players;
using Bun3.Server.Rpc;
using Bun3.Server.Transport.Tcp;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>Player 수명주기가 붙은 Rpc 서버를 Generic Host에 등록하는 확장.</summary>
public static class PlayersServiceCollectionExtensions
{
    /// <summary>
    /// Players + Rpc 서버(TCP)를 등록한다. 정지 시 서버 drain 후 전 Player를
    /// 은퇴(RetireAllAsync — 저장 플러시)시킨다. TSession은 IConnection을 받는
    /// public 생성자가 필요하며 나머지 인자는 DI로 주입된다. 호스트당 1회만 호출.
    /// </summary>
    public static IServiceCollection AddPlayerServer<TSession, TPlayer, TRequest, TResponse, TUpdate>(
        this IServiceCollection services,
        Func<IServiceProvider, string, ValueTask<TPlayer>> loader,
        Action<PlayersConfig<TSession>> configure,
        Action<ServerOptions>? serverOptions = null,
        Action<PlayersOptions>? playersOptions = null)
        where TSession : PlayerSession<TPlayer>
        where TPlayer : Player
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddServerTransport(serverOptions);

        services.AddSingleton(sp =>
        {
            var effectivePlayersOptions = new PlayersOptions();
            playersOptions?.Invoke(effectivePlayersOptions);
            return new PlayerRegistry<TPlayer>(
                key => loader(sp, key),
                effectivePlayersOptions,
                ServerServiceCollectionExtensions.ResolveLogger(sp));
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
                new RpcServerOptions { MaxQueuedPackets = options.MaxQueuedPacketsPerSession },
                ServerServiceCollectionExtensions.ResolveLogger(sp));
        });

        services.AddHostedService(sp => new PlayersLifetimeService<TSession, TPlayer, TRequest, TResponse, TUpdate>(
            sp.GetRequiredService<RpcServer<TSession, TRequest, TResponse, TUpdate>>(),
            sp.GetRequiredService<PlayerRegistry<TPlayer>>(),
            sp.GetRequiredService<IOptions<ServerOptions>>()));

        return services;
    }
}

/// <summary>서버 수명 + 정지 시 Player 전원 은퇴. 닫힌 제네릭이 서버마다 달라
/// 중복 등록이 TryAddEnumerable에 조용히 떨어지지 않는다.</summary>
internal sealed class PlayersLifetimeService<TSession, TPlayer, TRequest, TResponse, TUpdate> : IHostedService
    where TSession : PlayerSession<TPlayer>
    where TPlayer : Player
    where TRequest : class, IMessage<TRequest>, new()
    where TResponse : class, IMessage<TResponse>, new()
    where TUpdate : class, IMessage<TUpdate>, new()
{
    private readonly RpcServer<TSession, TRequest, TResponse, TUpdate> _server;
    private readonly PlayerRegistry<TPlayer> _registry;
    private readonly IOptions<ServerOptions> _options;

    public PlayersLifetimeService(
        RpcServer<TSession, TRequest, TResponse, TUpdate> server,
        PlayerRegistry<TPlayer> registry,
        IOptions<ServerOptions> options)
    {
        _server = server;
        _registry = registry;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _server.StopAsync(_options.Value.DrainTimeout, cancellationToken).ConfigureAwait(false);
        await _registry.RetireAllAsync(cancellationToken).ConfigureAwait(false);   // 세션 정리 후 저장 플러시
    }
}
