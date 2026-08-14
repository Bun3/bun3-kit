#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Bun3.Gameplay.Tags;
using UnityEditor.Build;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagPublishedCatalogValidator
    {
        internal static GameplayTagPublishedCatalogContext ResolvePublishedCatalog()
            => Execute(() => Resolve(
                GameplayTagBuildContextProviderDiscovery.Discover(),
                GameplayTagProjectSettings.ReadConfiguredCatalogId()));

        internal static string ResolveAndValidate(IReadOnlyList<Type> providerTypes) =>
            ResolveAndValidate(providerTypes, null);

        internal static string ResolveAndValidate(
            IReadOnlyList<Type> providerTypes,
            string? configuredCatalogId) =>
            Execute(() => Validate(Resolve(providerTypes, configuredCatalogId)));

        internal static string Validate(GameplayTagPublishedCatalogContext context) =>
            Execute(() => ValidateCore(context));

        internal static BuildFailedException CreateBuildFailure(Exception exception)
        {
            if (exception is BuildFailedException buildFailedException)
            {
                return buildFailedException;
            }

            return new BuildFailedException(
                "GameplayTag Published Catalog preflight failed: " + exception.Message);
        }

        private static GameplayTagPublishedCatalogContext Resolve(
            IReadOnlyList<Type> providerTypes,
            string? configuredCatalogId)
        {
            var candidates = GameplayTagBuildContextProviderDiscovery.SelectCandidates(providerTypes);

            if (candidates.Count != 1)
            {
                if (candidates.Count == 0 && configuredCatalogId is not null)
                {
                    throw new InvalidOperationException(
                        "Project Settings configures development only; exactly one gameplay tag build "
                        + "context provider is required for a Published build.");
                }

                throw new InvalidOperationException(
                    "Exactly one gameplay tag build context provider is required for a Published build; found "
                    + GameplayTagBuildContextProviderDiscovery.FormatCandidateCount(candidates));
            }

            var provider = (IGameplayTagBuildContextProvider)Activator.CreateInstance(
                candidates[0], nonPublic: true)!;
            var providerCatalogId = provider.CatalogId;
            if (!string.Equals(
                    providerCatalogId,
                    GameplayTagCatalogId.Require(providerCatalogId, nameof(provider.CatalogId)),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Catalog ID must use its canonical lowercase ASCII-hyphen form.");
            }

            if (configuredCatalogId is not null
                && !string.Equals(
                    providerCatalogId,
                    configuredCatalogId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The gameplay tag build context provider Catalog ID does not match Project Settings.");
            }

            var context = provider.GetPublishedCatalog()
                ?? throw new InvalidOperationException("The gameplay tag Published Catalog context is null.");
            if (!string.Equals(providerCatalogId, context.CatalogId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The gameplay tag provider Catalog ID does not match its Published Catalog context.");
            }

            return context;
        }

        private static string ValidateCore(GameplayTagPublishedCatalogContext context)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));
            var artifactPath = Path.GetFullPath(context.ArtifactPath);
            using (var input = File.OpenRead(artifactPath))
            {
                _ = TagCatalogBinary.Load(
                    input,
                    TagCatalogExpectations.ForPublished(
                        context.CatalogId,
                        context.CatalogVersion,
                        context.ExpectedFingerprint));
            }

            return artifactPath;
        }

        private static T Execute<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (Exception exception)
            {
                throw CreateBuildFailure(exception);
            }
        }
    }
}
