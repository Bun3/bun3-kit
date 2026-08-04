using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

public static class Bun3ServerServiceCollectionExtensions
{
    /// <summary>
    /// TCP 전송 기반 Bun3 서버를 Generic Host에 등록한다.
    /// TSession은 IConnection을 받는 public 생성자가 필요하며, 나머지 인자는 DI로 주입된다.
    /// </summary>
    public static IServiceCollection AddBun3Server<TSession>(
        this IServiceCollection services,
        Action<Bun3ServerOptions>? configure = null)
        where TSession : Session
    {
        var optionsBuilder = services.AddOptions<Bun3ServerOptions>()
            .BindConfiguration(Bun3ServerOptions.SectionName);
        if (configure != null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddSingleton<IBun3Logger>(sp =>
        {
            // 최소 구성 호스트(DisableDefaults 등)에서 로깅이 없어도 동작하도록 방어
            var factory = sp.GetService<ILoggerFactory>();
            return factory != null
                ? new Bun3LoggerBridge(factory.CreateLogger("Bun3.Server"))
                : NullBun3Logger.Instance;
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<Bun3ServerOptions>>().Value;
            return new TcpTransportListener(
                new TcpTransportOptions { Port = options.Port, MaxFrameSize = options.MaxFrameSize },
                sp.GetRequiredService<IBun3Logger>());
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<Bun3ServerOptions>>().Value;
            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);
            return new HostedServer<TSession>(
                sp.GetRequiredService<TcpTransportListener>(),
                Factory,
                sp.GetRequiredService<IBun3Logger>(),
                new SessionOptions { MaxQueuedFrames = options.MaxQueuedFramesPerSession });
        });

        services.AddHostedService<Bun3ServerHostedService<TSession>>(sp =>
            new Bun3ServerHostedService<TSession>(sp.GetRequiredService<HostedServer<TSession>>()));

        return services;
    }
}
