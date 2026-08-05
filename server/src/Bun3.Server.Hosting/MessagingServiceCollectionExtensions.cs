using Bun3.Server.Abstractions;
using Bun3.Server.Messaging;
using Bun3.Server.Transport.Tcp;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>메시징 서버를 Generic Host DI 컨테이너에 등록하는 확장 메서드 모음.</summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// 메시징 서버(TCP)를 Generic Host에 등록한다. 핸들러 등록표는 여기서 1회 구성되며,
    /// 구성 오류(미등록 핸들러 등)는 호스트 StartAsync에서 전체 목록과 함께 실패한다.
    /// TSession은 IConnection을 받는 public 생성자가 필요하며 나머지 인자는 DI로 주입된다.
    /// </summary>
    /// <remarks>제약(v0/v1 동일): 세션 생성자 의존성은 루트 컨테이너에서 해석되고(스코프 금지),
    /// 호스트당 1회만 호출한다(AddServer와 리스너 싱글턴을 공유하지 않도록 함께 쓰지 말 것).</remarks>
    public static IServiceCollection AddMessagingServer<TSession, TRequest, TResponse, TUpdate>(
        this IServiceCollection services,
        Action<MessagingConfig<TSession>> configure,
        Action<ServerOptions>? serverOptions = null,
        Action<MessagingServerOptions>? messagingOptions = null)
        where TSession : MessagingSession
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = services.AddOptions<ServerOptions>()
            .BindConfiguration(ServerOptions.SectionName);
        if (serverOptions != null)
        {
            optionsBuilder.Configure(serverOptions);
        }

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            return new TcpTransportListener(
                new TcpTransportOptions
                {
                    Port = options.Port,
                    MaxPacketSize = options.MaxPacketSize,
                    Backlog = options.Backlog,
                },
                ResolveLogger(sp));
        });

        services.AddSingleton(sp =>
        {
            var config = new MessagingConfig<TSession>();
            configure(config);
            var messagingServerOptions = new MessagingServerOptions();
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            messagingServerOptions.MaxQueuedPackets = options.MaxQueuedPacketsPerSession;
            messagingOptions?.Invoke(messagingServerOptions);

            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);

            // MessagingServer ctor가 스키마 구축 + 전수 검증을 수행 — 여기서 throw되면
            // 호스트 StartAsync가 MessagingValidationException으로 실패한다(fail-fast).
            return new MessagingServer<TSession, TRequest, TResponse, TUpdate>(
                sp.GetRequiredService<TcpTransportListener>(),
                Factory,
                config,
                messagingServerOptions,
                ResolveLogger(sp));
        });

        services.AddHostedService(sp => new MessagingHostedService<TSession, TRequest, TResponse, TUpdate>(
            sp.GetRequiredService<MessagingServer<TSession, TRequest, TResponse, TUpdate>>(),
            sp.GetRequiredService<IOptions<ServerOptions>>()));

        return services;
    }

    private static ILogger ResolveLogger(IServiceProvider sp) =>
        sp.GetService<ILoggerFactory>()?.CreateLogger("Bun3.Server")
        ?? (ILogger)NullLogger.Instance;
}

internal sealed class MessagingHostedService<TSession, TRequest, TResponse, TUpdate> : IHostedService
    where TSession : MessagingSession
    where TRequest : class, IMessage<TRequest>, new()
    where TResponse : class, IMessage<TResponse>, new()
    where TUpdate : class, IMessage<TUpdate>, new()
{
    private readonly MessagingServer<TSession, TRequest, TResponse, TUpdate> _server;
    private readonly IOptions<ServerOptions> _options;

    public MessagingHostedService(
        MessagingServer<TSession, TRequest, TResponse, TUpdate> server,
        IOptions<ServerOptions> options)
    {
        _server = server;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _server.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) =>
        _server.StopAsync(_options.Value.DrainTimeout, cancellationToken);
}
