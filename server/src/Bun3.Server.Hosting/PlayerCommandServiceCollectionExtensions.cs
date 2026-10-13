using Bun3.Server.Players;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bun3.Server.Hosting;

/// <summary>Registers durable command workers for an active game server or an offline administrator host.</summary>
public static class PlayerCommandServiceCollectionExtensions
{
    /// <summary>Registers a worker whose lifetime is controlled by the matching player server.</summary>
    public static IServiceCollection AddPlayerCommands<TPlayer>(this IServiceCollection services,
        Func<IServiceProvider, PlayerCommandStore> store, Action<PlayerCommandCatalog<TPlayer>> commands,
        Action<PlayerCommandWorkerOptions>? options = null) where TPlayer : Player => Register(services, store, commands, options, false);
    /// <summary>Registers a standalone worker with fresh player state and no sessions, listener or tick loop.</summary>
    public static IServiceCollection AddOfflinePlayerCommands<TPlayer>(this IServiceCollection services,
        Func<IServiceProvider, OwnedPlayerStore<TPlayer>> players, Func<IServiceProvider, PlayerCommandStore> commands,
        Action<PlayerCommandCatalog<TPlayer>> configure, Action<PlayerCommandWorkerOptions>? options = null) where TPlayer : Player
    {
        ArgumentNullException.ThrowIfNull(players);
        services.AddSingleton(sp =>
        {
            var store = players(sp) ?? throw new InvalidOperationException("Player store factory returned null.");
            if (store.Binding.Role != PlayerWriteRole.OfflineAdministrator) throw new InvalidOperationException("Offline commands require an offline administrator binding.");
            return new PlayerRegistry<TPlayer>(store, logger: ServerServiceCollectionExtensions.ResolveLogger(sp));
        });
        Register(services, commands, configure, options, true);
        services.AddHostedService(sp => sp.GetRequiredService<PlayerCommandWorker<TPlayer>>());
        return services;
    }
    private static IServiceCollection Register<TPlayer>(IServiceCollection services, Func<IServiceProvider, PlayerCommandStore> store,
        Action<PlayerCommandCatalog<TPlayer>> configure, Action<PlayerCommandWorkerOptions>? options, bool standalone) where TPlayer : Player
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(configure);
        services.AddSingleton(sp => new PlayerCommandQueue(store(sp), ServerServiceCollectionExtensions.ResolveLogger(sp)));
        services.AddSingleton(_ => { var catalog = new PlayerCommandCatalog<TPlayer>(); configure(catalog); return catalog; });
        services.AddSingleton(sp =>
        {
            var settings = new PlayerCommandWorkerOptions(); options?.Invoke(settings);
            return new PlayerCommandWorker<TPlayer>(sp.GetRequiredService<PlayerCommandQueue>(), sp.GetRequiredService<PlayerCommandCatalog<TPlayer>>(),
                sp.GetRequiredService<PlayerRegistry<TPlayer>>(), settings, standalone, ServerServiceCollectionExtensions.ResolveLogger(sp));
        });
        return services;
    }
}
