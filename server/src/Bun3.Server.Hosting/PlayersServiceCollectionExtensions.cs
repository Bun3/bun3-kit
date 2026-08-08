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

/// <summary>Player 수명주기가 붙은 Rpc 서버를 Generic Host에 등록하는 확장.</summary>
public static class PlayersServiceCollectionExtensions
{
    /// <summary>
    /// Players + Rpc 서버(TCP)를 등록한다. 시작 시 서버 수신 → 틱 루프 순으로 뜨고,
    /// 정지 시 틱 루프 → 서버 drain → 전 Player 은퇴(RetireAllAsync — 저장 플러시)
    /// 순으로 정리된다. TSession은 IConnection을 받는 public 생성자가 필요하며
    /// 나머지 인자는 DI로 주입된다. ticking은 TickLoop 옵션(틱 간격 등)을, jobs는
    /// TickLoop.Start 전에 게임 전역 잡을 등록할 콜백을 받는다(Player 틱 잡은 자동 등록됨).
    /// 호스트당 1회만 호출.
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

        var effectivePlayersOptions = new PlayersOptions();
        playersOptions?.Invoke(effectivePlayersOptions);

        services.AddServerTransport(serverOptions);

        services.AddSingleton(sp => new PlayerRegistry<TPlayer>(
            key => loader(sp, key),
            effectivePlayersOptions,
            ServerServiceCollectionExtensions.ResolveLogger(sp)));

        services.AddSingleton(sp =>
        {
            var tickingOptions = new TickingOptions();
            ticking?.Invoke(tickingOptions);
            var loop = new TickLoop(tickingOptions, ServerServiceCollectionExtensions.ResolveLogger(sp));
            new PlayerTicker<TPlayer>(
                    sp.GetRequiredService<PlayerRegistry<TPlayer>>(),
                    effectivePlayersOptions,
                    ServerServiceCollectionExtensions.ResolveLogger(sp))
                .Register(loop);
            jobs?.Invoke(loop);   // 게임 전역 잡 — Start 전 등록 규약 충족
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

/// <summary>서버·틱 루프 수명 + 정지 시 Player 전원 은퇴. 닫힌 제네릭이 서버마다 달라
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
        _tickLoop.Start();   // 서버가 받은 뒤 틱 시작
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _tickLoop.StopAsync(cancellationToken).ConfigureAwait(false);   // 틱 먼저 정지 — 정지 중 새 틱 작업 유입 차단
        await _server.StopAsync(_options.Value.DrainTimeout, cancellationToken).ConfigureAwait(false);
        await _registry.RetireAllAsync(cancellationToken).ConfigureAwait(false);   // 최종 저장
    }
}
