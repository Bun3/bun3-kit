using System;
using Bun3.Gameplay.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bun3.Server.GameplayTags
{
    /// <summary>Extension methods registering the GameplayTag catalog on the native .NET Generic Host.</summary>
    public static class GameplayTagServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a singleton that reads the GameplayTag catalog once, plus a boundary that starts before subsequent gameplay hosted services.
        /// </summary>
        /// <param name="services">Service collection to register into.</param>
        /// <param name="configure">Optional option setup applied after configuration-section binding.</param>
        /// <returns>The input service collection for chaining.</returns>
        /// <exception cref="ArgumentNullException">When <paramref name="services"/> is null.</exception>
        public static IServiceCollection AddGameplayTagCatalog(
            this IServiceCollection services,
            Action<GameplayTagCatalogOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            var optionsBuilder = services.AddOptions<GameplayTagCatalogOptions>()
                .BindConfiguration(GameplayTagCatalogOptions.SectionName)
                .Validate(
                    options => Enum.IsDefined(options.Mode),
                    "GameplayTag catalog Mode must be a defined value.")
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.CatalogId),
                    "GameplayTag catalog ID is required.")
                .Validate(
                    options => options.Mode != GameplayTagCatalogMode.Packaged
                        || !string.IsNullOrWhiteSpace(options.CatalogVersion),
                    "Packaged GameplayTag catalog version is required.")
                .Validate(
                    options => options.Mode != GameplayTagCatalogMode.Packaged
                        || !TagCatalogVersions.IsDevelopment(options.CatalogVersion),
                    "Packaged GameplayTag catalog version must not be the reserved development version.")
                .Validate(
                    options => options.Mode != GameplayTagCatalogMode.Packaged
                        || GameplayTagCatalogOptions.IsFingerprintHex(options.ExpectedFingerprint),
                    "Packaged GameplayTag catalog fingerprint must be exactly 64 hex digits.")
                .Validate(
                    options => options.Mode != GameplayTagCatalogMode.Packaged
                        || !string.IsNullOrWhiteSpace(options.PackagedPath),
                    "Packaged GameplayTag catalog path is required.")
                .ValidateOnStart();
            if (configure != null)
            {
                optionsBuilder.Configure(configure);
            }

            services.AddSingleton(sp =>
            {
                var logger = sp.GetService<ILogger<GameplayTagCatalogLoader>>()
                    ?? NullLogger<GameplayTagCatalogLoader>.Instance;
                return new GameplayTagCatalogLoader(
                    sp.GetRequiredService<IOptions<GameplayTagCatalogOptions>>().Value,
                    logger);
            });
            services.AddSingleton<TagCatalog>(sp =>
                sp.GetRequiredService<GameplayTagCatalogLoader>().Load());
            services.AddHostedService<GameplayTagCatalogStartupService>();
            return services;
        }
    }
}
