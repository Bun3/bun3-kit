using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Transport.Tcp;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>메시징 서버를 Generic Host DI 컨테이너에 등록하는 확장 메서드 모음.</summary>
public static class RpcServiceCollectionExtensions
{
    /// <summary>
    /// 메시징 서버(TCP)를 Generic Host에 등록한다. 핸들러 등록표는 여기서 1회 구성되며,
    /// 구성 오류(미등록 핸들러 등)는 호스트 StartAsync에서 전체 목록과 함께 실패한다.
    /// TSession은 IConnection을 받는 public 생성자가 필요하며 나머지 인자는 DI로 주입된다.
    /// </summary>
    /// <remarks>제약(v0/v1 동일): 세션 생성자 의존성은 루트 컨테이너에서 해석되고(스코프 금지),
    /// 호스트당 1회만 호출한다(AddServer와 리스너 싱글턴을 공유하지 않도록 함께 쓰지 말 것).</remarks>
    public static IServiceCollection AddRpcServer<TSession, TRequest, TResponse, TUpdate>(
        this IServiceCollection services,
        Action<RpcConfig<TSession>> configure,
        Action<ServerOptions>? serverOptions = null,
        Action<RpcServerOptions>? rpcOptions = null)
        where TSession : RpcSession
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddServerTransport(serverOptions);

        services.AddSingleton(sp =>
        {
            var config = new RpcConfig<TSession>();
            configure(config);
            var rpcServerOptions = new RpcServerOptions();
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            rpcServerOptions.MaxQueuedPackets = options.MaxQueuedPacketsPerSession;
            rpcOptions?.Invoke(rpcServerOptions);

            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);

            // RpcServer ctor가 스키마 구축 + 전수 검증을 수행 — 여기서 throw되면
            // 호스트 StartAsync가 RpcValidationException으로 실패한다(fail-fast).
            return new RpcServer<TSession, TRequest, TResponse, TUpdate>(
                sp.GetRequiredService<TcpTransportListener>(),
                Factory,
                config,
                rpcServerOptions,
                ServerServiceCollectionExtensions.ResolveLogger(sp));
        });

        services.AddHostedService(sp =>
            new ServerLifetimeService<RpcServer<TSession, TRequest, TResponse, TUpdate>, TSession>(
                sp.GetRequiredService<RpcServer<TSession, TRequest, TResponse, TUpdate>>(),
                sp.GetRequiredService<IOptions<ServerOptions>>()));

        return services;
    }
}
