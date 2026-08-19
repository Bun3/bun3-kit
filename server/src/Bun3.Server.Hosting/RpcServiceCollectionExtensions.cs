using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Transport.Tcp;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>Extension methods that register a messaging server into the Generic Host DI container.</summary>
public static class RpcServiceCollectionExtensions
{
    /// <summary>
    /// Registers a messaging server (TCP) with the Generic Host. The handler table is built once
    /// here; configuration errors (unregistered handlers, etc.) fail in the host's StartAsync with
    /// the full list. TSession needs a public constructor taking IConnection; remaining arguments
    /// are DI-injected.
    /// </summary>
    /// <remarks>Constraints (same in v0/v1): session constructor dependencies resolve from the
    /// root container (no scoped services), and call at most once per host — duplicating or
    /// mixing the server registration extensions fails at registration time with
    /// <see cref="InvalidOperationException"/>.</remarks>
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
            rpcServerOptions.SlowWorkWarning = options.SlowWorkWarning;
            rpcOptions?.Invoke(rpcServerOptions);

            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);

            // The RpcServer ctor builds the schema and validates it exhaustively — a throw here
            // fails the host's StartAsync with RpcValidationException (fail-fast).
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
