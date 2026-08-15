using System;
using Bun3.Gameplay.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bun3.Server.GameplayTags
{
    /// <summary>GameplayTag Catalog를 Native .NET Generic Host에 등록하는 확장 메서드 모음입니다.</summary>
    public static class GameplayTagServiceCollectionExtensions
    {
        /// <summary>
        /// GameplayTag Catalog를 한 번 읽는 singleton과 후속 gameplay hosted service보다 먼저 시작되는 경계를 등록합니다.
        /// </summary>
        /// <param name="services">등록 대상 서비스 모음입니다.</param>
        /// <param name="configure">구성 섹션 바인딩 뒤에 적용할 선택적 옵션 설정입니다.</param>
        /// <returns>연속 등록을 위한 입력 서비스 모음입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/>가 null인 경우입니다.</exception>
        public static IServiceCollection AddGameplayTagCatalog(
            this IServiceCollection services,
            Action<GameplayTagCatalogOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            var optionsBuilder = services.AddOptions<GameplayTagCatalogOptions>()
                .BindConfiguration(GameplayTagCatalogOptions.SectionName)
                .Validate(
                    options => Enum.IsDefined(options.Mode),
                    "GameplayTag Catalog Mode가 정의된 값이어야 합니다.")
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.CatalogId),
                    "GameplayTag Catalog ID가 필요합니다.")
                .Validate(
                    options => options.Mode != GameplayTagCatalogMode.Packaged
                        || !string.IsNullOrWhiteSpace(options.CatalogVersion),
                    "Packaged GameplayTag Catalog Version이 필요합니다.")
                .Validate(
                    options => options.Mode != GameplayTagCatalogMode.Packaged
                        || !TagCatalogVersions.IsDevelopment(options.CatalogVersion),
                    "Packaged GameplayTag Catalog Version은 예약된 개발 Version일 수 없습니다.")
                .Validate(
                    options => options.Mode != GameplayTagCatalogMode.Packaged
                        || GameplayTagCatalogOptions.IsFingerprintHex(options.ExpectedFingerprint),
                    "Packaged GameplayTag Catalog fingerprint는 정확히 64자리 hex여야 합니다.")
                .Validate(
                    options => options.Mode != GameplayTagCatalogMode.Packaged
                        || !string.IsNullOrWhiteSpace(options.PackagedPath),
                    "Packaged GameplayTag Catalog 경로가 필요합니다.")
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
