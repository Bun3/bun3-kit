#nullable enable
using System;
using UnityEditor.Build;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagBuildPlayerProcessor : BuildPlayerProcessor
    {
        internal const string PlayerCatalogPath = "Bun3/GameplayTags/GameplayTags.catalog";

        /// <inheritdoc />
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            if (buildPlayerContext is null)
            {
                throw new BuildFailedException("Unity did not provide a BuildPlayerContext.");
            }

            PrepareForBuild(
                GameplayTagPublishedCatalogValidator.ResolvePublishedCatalog,
                buildPlayerContext.AddAdditionalPathToStreamingAssets);
        }

        internal static void PrepareForBuild(
            Func<GameplayTagPublishedCatalogContext> resolvePublishedCatalog,
            Action<string, string> addAdditionalPathToStreamingAssets)
        {
            if (resolvePublishedCatalog is null)
            {
                throw new ArgumentNullException(nameof(resolvePublishedCatalog));
            }

            if (addAdditionalPathToStreamingAssets is null)
            {
                throw new ArgumentNullException(nameof(addAdditionalPathToStreamingAssets));
            }

            try
            {
                var context = resolvePublishedCatalog()
                    ?? throw new InvalidOperationException(
                        "The gameplay tag Published Catalog context is null.");
                var artifactPath = GameplayTagPublishedCatalogValidator.Validate(context);
                addAdditionalPathToStreamingAssets(artifactPath, PlayerCatalogPath);
            }
            catch (Exception exception)
            {
                throw GameplayTagPublishedCatalogValidator.CreateBuildFailure(exception);
            }
        }
    }
}
