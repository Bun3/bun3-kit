using System;
using System.IO;
using Bun3.Gameplay.Tags;

namespace Bun3.Server.GameplayTags
{
    internal static class GameplayTagCatalogPathResolver
    {
        private const string OverrideEnvironmentVariable = "BUN3_GAMEPLAY_TAG_CATALOG_PATH";

        internal static string Resolve(GameplayTagCatalogOptions options)
        {
            if (options.Mode == GameplayTagCatalogMode.LocalDevelopment)
            {
                var explicitPath = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
                return string.IsNullOrWhiteSpace(explicitPath)
                    ? TagCatalogDevelopmentPath.Get(options.CatalogId, options.LocalApplicationDataOverride)
                    : Path.GetFullPath(explicitPath);
            }

            return Path.IsPathFullyQualified(options.PackagedPath)
                ? Path.GetFullPath(options.PackagedPath)
                : Path.GetFullPath(options.PackagedPath, AppContext.BaseDirectory);
        }
    }
}
