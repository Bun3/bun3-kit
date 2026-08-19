using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Bun3.Server.Transport.Tcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bun3.Server.Hosting;

/// <summary>Extension methods that register a Bun3 server into the Generic Host DI container.</summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers a TCP-transport Bun3 server with the Generic Host.
    /// TSession needs a public constructor taking IConnection; remaining arguments are DI-injected.
    /// </summary>
    /// <remarks>
    /// Constraints (v0):
    /// <list type="bullet">
    /// <item>Additional constructor dependencies of a session are always resolved from the root
    /// container — injecting a scoped service into a session yields either an exception (with
    /// ValidateScopes) or a de facto singleton pinned to the root, not a per-session instance.
    /// Use only singleton/transient session dependencies.</item>
    /// <item>Call at most once per host. Duplicating or mixing the server registration extensions
    /// (AddServer/AddRpcServer/AddPlayerServer) cannot share the TCP listener singleton and fails
    /// at registration time with <see cref="InvalidOperationException"/>. Multiple session
    /// types/ports are out of scope for now.</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddServer<TSession>(
        this IServiceCollection services,
        Action<ServerOptions>? configure = null)
        where TSession : Session
    {
        services.AddServerTransport(configure);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServerOptions>>().Value;
            TSession Factory(IConnection connection) =>
                ActivatorUtilities.CreateInstance<TSession>(sp, connection);
            return new HostedServer<TSession>(
                sp.GetRequiredService<TcpTransportListener>(),
                Factory,
                ResolveLogger(sp),
                options.MaxQueuedPacketsPerSession,
                options.SlowWorkWarning);
        });

        services.AddHostedService(sp =>
            new ServerLifetimeService<HostedServer<TSession>, TSession>(
                sp.GetRequiredService<HostedServer<TSession>>(),
                sp.GetRequiredService<IOptions<ServerOptions>>()));

        return services;
    }

    // Defensive: keeps working on minimal hosts (DisableDefaults etc.) that register no logging.
    internal static ILogger ResolveLogger(IServiceProvider sp) =>
        sp.GetService<ILoggerFactory>()?.CreateLogger("Bun3.Server")
        ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <summary>Registers the ServerOptions pipeline ("Bun3:Server" binding + lambda) and the TCP
    /// listener singleton — the shared front half of the three server extensions.</summary>
    /// <exception cref="InvalidOperationException">A server registration extension was already called (duplicate/mixed).</exception>
    internal static void AddServerTransport(this IServiceCollection services, Action<ServerOptions>? serverOptions)
    {
        // The listener singleton serves exactly one server — fail clearly at registration time
        // instead of deferring duplicates to a "Listener is already started." crash at startup.
        if (services.Any(d => d.ServiceType == typeof(TcpTransportListener)))
        {
            throw new InvalidOperationException(
                "A Bun3 server can be registered only once per host — AddServer/AddRpcServer/AddPlayerServer "
                + "was already called. Multiple servers (session types/ports) are not supported yet.");
        }

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
                    BindAddress = string.IsNullOrEmpty(options.BindAddress)
                        ? null
                        : System.Net.IPAddress.Parse(options.BindAddress),   // Invalid values fail at startup.
                    MaxConnections = options.MaxConnections,
                    MaxPacketSize = options.MaxPacketSize,
                    Backlog = options.Backlog,
                },
                ResolveLogger(sp));
        });
    }
}
