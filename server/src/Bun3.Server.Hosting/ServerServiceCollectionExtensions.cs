using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>Bun3 서버를 Generic Host DI 컨테이너에 등록하는 확장 메서드 모음.</summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>
    /// TCP 전송 기반 Bun3 서버를 Generic Host에 등록한다.
    /// TSession은 IConnection을 받는 public 생성자가 필요하며, 나머지 인자는 DI로 주입된다.
    /// </summary>
    /// <remarks>
    /// 제약(v0):
    /// <list type="bullet">
    /// <item>세션 생성 시 추가 생성자 의존성은 항상 루트 컨테이너에서 해석된다 —
    /// scoped 서비스를 세션에 주입하면 세션별 인스턴스가 아니라 예외(ValidateScopes 시)
    /// 또는 루트에 고정된 사실상의 싱글턴이 된다. 세션 의존성은 싱글턴/트랜지언트만 사용할 것.</item>
    /// <item>호스트당 1회만 호출할 것. 두 번 등록하면(예: 세션 타입 2개) 마지막
    /// TcpTransportListener 싱글턴을 공유하게 되어 두 번째 서버의 StartAsync가
    /// "Listener is already started."로 기동 시점에 실패한다. 다중 세션 타입/포트는 v1 범위.</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddServer<TSession>(
        this IServiceCollection services,
        Action<ServerOptions>? configure = null)
        where TSession : Session
    {
        var optionsBuilder = services.AddOptions<ServerOptions>()
            .BindConfiguration(ServerOptions.SectionName);
        if (configure != null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            return new TcpTransportListener(
                new TcpTransportOptions { Port = options.Port, MaxPacketSize = options.MaxPacketSize, Backlog = options.Backlog },
                ResolveLogger(sp));
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);
            return new HostedServer<TSession>(
                sp.GetRequiredService<TcpTransportListener>(),
                Factory,
                ResolveLogger(sp),
                options.MaxQueuedPacketsPerSession);
        });

        services.AddHostedService(sp =>
            new ServerLifetimeService<HostedServer<TSession>, TSession>(
                sp.GetRequiredService<HostedServer<TSession>>(),
                sp.GetRequiredService<IOptions<ServerOptions>>()));

        return services;
    }

    // 최소 구성 호스트(DisableDefaults 등)에서 로깅이 없어도 동작하도록 방어
    internal static ILogger ResolveLogger(IServiceProvider sp) =>
        sp.GetService<ILoggerFactory>()?.CreateLogger("Bun3.Server")
        ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
