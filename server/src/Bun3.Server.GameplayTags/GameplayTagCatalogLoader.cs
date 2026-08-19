using System;
using System.IO;
using Bun3.Gameplay.Tags;
using Microsoft.Extensions.Logging;

namespace Bun3.Server.GameplayTags
{
    internal sealed class GameplayTagCatalogLoader
    {
        private readonly GameplayTagCatalogOptions _options;
        private readonly ILogger<GameplayTagCatalogLoader> _logger;

        internal GameplayTagCatalogLoader(
            GameplayTagCatalogOptions options,
            ILogger<GameplayTagCatalogLoader> logger)
        {
            _options = options;
            _logger = logger;
        }

        internal TagCatalog Load()
        {
            var expectations = _options.Mode == GameplayTagCatalogMode.LocalDevelopment
                ? TagCatalogExpectations.ForDevelopment(_options.CatalogId)
                : TagCatalogExpectations.ForPublished(
                    _options.CatalogId,
                    _options.CatalogVersion,
                    Convert.FromHexString(_options.ExpectedFingerprint));
            var path = GameplayTagCatalogPathResolver.Resolve(_options);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("GameplayTag catalog file not found.", path);
            }

            using var input = File.OpenRead(path);
            var catalog = TagCatalogBinary.Load(input, expectations);
            _logger.LogInformation(
                "GameplayTag catalog {CatalogId} {CatalogVersion} {Fingerprint} loaded",
                catalog.CatalogId,
                catalog.CatalogVersion,
                Convert.ToHexString(catalog.Fingerprint).ToLowerInvariant());
            return catalog;
        }
    }
}
