#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Bun3.Gameplay.Tags;
using UnityEditor;
using UnityEditor.Build;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagPublishedCatalogValidator
    {
        internal static GameplayTagPublishedCatalogContext ResolvePublishedCatalog()
        {
            var providerTypes = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IGameplayTagBuildContextProvider>())
            {
                providerTypes.Add(type);
            }

            return Execute(() => Resolve(providerTypes));
        }

        internal static string ResolveAndValidate(IReadOnlyList<Type> providerTypes) =>
            Execute(() => Validate(Resolve(providerTypes)));

        internal static string Validate(GameplayTagPublishedCatalogContext context) =>
            Execute(() => ValidateCore(context));

        internal static BuildFailedException CreateBuildFailure(Exception exception)
        {
            if (exception is BuildFailedException buildFailedException)
            {
                return buildFailedException;
            }

            return new BuildFailedException(new InvalidOperationException(
                "GameplayTag Published Catalog preflight failed: " + exception.Message,
                exception));
        }

        private static GameplayTagPublishedCatalogContext Resolve(IReadOnlyList<Type> providerTypes)
        {
            if (providerTypes is null) throw new ArgumentNullException(nameof(providerTypes));
            var candidates = new List<Type>();
            for (var index = 0; index < providerTypes.Count; index++)
            {
                var type = providerTypes[index]
                    ?? throw new ArgumentNullException(nameof(providerTypes));
                if (type.IsAbstract || type.ContainsGenericParameters
                    || !typeof(IGameplayTagBuildContextProvider).IsAssignableFrom(type)
                    || FindParameterlessConstructor(type) is null)
                {
                    continue;
                }

                candidates.Add(type);
            }

            if (candidates.Count != 1)
            {
                throw new InvalidOperationException(
                    "Exactly one gameplay tag build context provider is required for a Published build; found "
                    + candidates.Count + ".");
            }

            var provider = (IGameplayTagBuildContextProvider)Activator.CreateInstance(
                candidates[0], nonPublic: true)!;
            var configuredCatalogId = provider.CatalogId;
            if (string.IsNullOrWhiteSpace(configuredCatalogId))
            {
                throw new InvalidOperationException("The gameplay tag build context provider Catalog ID is empty.");
            }

            var context = provider.GetPublishedCatalog()
                ?? throw new InvalidOperationException("The gameplay tag Published Catalog context is null.");
            if (!string.Equals(configuredCatalogId, context.CatalogId, StringComparison.Ordinal))
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

        private static ConstructorInfo? FindParameterlessConstructor(Type type) =>
            type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

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
