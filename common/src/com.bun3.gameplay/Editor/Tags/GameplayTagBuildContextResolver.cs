#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Resolves the catalog build context from the Unity editor provider and installed packages.</summary>
    public static class GameplayTagBuildContextResolver
    {
        private const string PackageMetadataRelativePath = "Bun3/GameplayTags/TagSource.json";
        private const string ProviderCountCode = "B3TAG3001";
        private const string ProviderConfigurationCode = "B3TAG3002";
        private const string SourceLoadCode = "B3TAG3003";
        private const string ProjectSettingsConfigurationCode = "B3TAG3004";

        /// <summary>Resolves the development build context using Unity's type cache and installed packages.</summary>
        /// <param name="gameSourcePath">Fixed absolute path of the game source.</param>
        /// <returns>Complete context, or diagnostics that block the operation.</returns>
        public static GameplayTagBuildContextResolution ResolveDevelopment(string gameSourcePath)
            => ResolveDevelopment(
                gameSourcePath,
                GameplayTagBuildContextProviderDiscovery.Discover(),
                DiscoverInstalledPackageMetadataPaths(),
                GameplayTagProjectSettings.ReadConfiguredCatalogId());

        internal static GameplayTagBuildContextResolution ResolveDevelopment(
            string gameSourcePath,
            IReadOnlyList<Type> providerTypes,
            IReadOnlyList<string> installedPackageMetadataPaths)
            => ResolveDevelopment(
                gameSourcePath,
                providerTypes,
                installedPackageMetadataPaths,
                null);

        internal static GameplayTagBuildContextResolution ResolveDevelopment(
            string gameSourcePath,
            IReadOnlyList<Type> providerTypes,
            IReadOnlyList<string> installedPackageMetadataPaths,
            string? configuredCatalogId)
        {
            if (gameSourcePath is null) throw new ArgumentNullException(nameof(gameSourcePath));
            if (providerTypes is null) throw new ArgumentNullException(nameof(providerTypes));
            if (installedPackageMetadataPaths is null)
            {
                throw new ArgumentNullException(nameof(installedPackageMetadataPaths));
            }

            var candidates = GameplayTagBuildContextProviderDiscovery.SelectCandidates(providerTypes);

            if (candidates.Count > 1)
            {
                return Failure(
                    ProviderCountCode + ": Exactly one gameplay tag build context provider is required; found "
                    + GameplayTagBuildContextProviderDiscovery.FormatCandidateCount(candidates),
                    permitsGameOnlyValidation: true);
            }

            if (configuredCatalogId is not null)
            {
                try
                {
                    _ = GameplayTagCatalogId.RequireCanonical(
                        configuredCatalogId,
                        nameof(configuredCatalogId));
                }
                catch (ArgumentException exception)
                {
                    return Failure(
                        ProviderConfigurationCode + ": Invalid GameplayTag Project Settings: "
                        + exception.Message,
                        permitsGameOnlyValidation: true);
                }
            }

            IReadOnlyList<string> externalPaths;
            string catalogId;
            if (candidates.Count == 0)
            {
                if (configuredCatalogId is null)
                {
                    return Failure(
                        ProjectSettingsConfigurationCode
                        + ": GameplayTag Catalog settings are not configured.",
                        permitsGameOnlyValidation: true,
                        requiresCatalogConfiguration: true);
                }

                catalogId = configuredCatalogId;
                externalPaths = Array.Empty<string>();
            }
            else
            {
                IGameplayTagBuildContextProvider provider;
                try
                {
                    provider = (IGameplayTagBuildContextProvider)Activator.CreateInstance(
                        candidates[0], nonPublic: true)!;
                }
                catch (Exception exception)
                {
                    return Failure(
                        ProviderConfigurationCode + ": Failed to create gameplay tag build context provider: "
                        + exception.GetBaseException().Message,
                        permitsGameOnlyValidation: true);
                }

                try
                {
                    catalogId = provider.CatalogId;
                    if (!string.Equals(
                            catalogId,
                            GameplayTagCatalogId.Require(catalogId, nameof(provider.CatalogId)),
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Catalog ID must use its canonical lowercase ASCII-hyphen form.");
                    }

                    externalPaths = provider.ExternalSourceMetadataPaths
                        ?? throw new InvalidOperationException("External Source Metadata path list is null.");
                    if (configuredCatalogId is not null
                        && !string.Equals(
                            catalogId,
                            configuredCatalogId,
                            StringComparison.Ordinal))
                    {
                        return Failure(
                            ProviderConfigurationCode
                            + ": GameplayTag Catalog ID does not match Project Settings.",
                            permitsGameOnlyValidation: true);
                    }

                    _ = new TagCatalogIdentity(catalogId, TagCatalogVersions.Development);
                }
                catch (Exception exception)
                {
                    return Failure(
                        ProviderConfigurationCode + ": Invalid gameplay tag build context provider: "
                        + exception.Message,
                        permitsGameOnlyValidation: true);
                }
            }

            string[] metadataPaths;
            try
            {
                metadataPaths = MergeMetadataPaths(externalPaths, installedPackageMetadataPaths);
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is NotSupportedException)
            {
                return Failure(
                    ProviderConfigurationCode + ": Invalid Source Metadata path: "
                    + exception.Message,
                    permitsGameOnlyValidation: true);
            }

            var sources = new List<TagSourceDocument>(metadataPaths.Length + 1);
            try
            {
                using (var gameStream = File.OpenRead(gameSourcePath))
                {
                    sources.Add(TagSourceJson.LoadGame(gameStream, gameSourcePath));
                }
            }
            catch (Exception exception) when (exception is IOException
                || exception is UnauthorizedAccessException
                || exception is TagCatalogException)
            {
                return Failure(
                    SourceLoadCode + ": Failed to load gameplay tag source: " + exception.Message,
                    permitsGameOnlyValidation: false,
                    localSourcePath: gameSourcePath);
            }

            for (var index = 0; index < metadataPaths.Length; index++)
            {
                var path = metadataPaths[index];
                try
                {
                    using var metadataStream = File.OpenRead(path);
                    sources.Add(TagSourceJson.LoadMetadata(metadataStream, path));
                }
                catch (Exception exception) when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is TagCatalogException)
                {
                    return Failure(
                        SourceLoadCode + ": Failed to load gameplay tag source: " + exception.Message,
                        permitsGameOnlyValidation: false,
                        localSourcePath: path);
                }
            }

            try
            {
                return new GameplayTagBuildContextResolution(
                    new GameCatalogBuildContext(
                        new TagCatalogIdentity(catalogId, TagCatalogVersions.Development),
                        CatalogBuildMode.Development,
                        sources),
                    Array.Empty<string>(),
                    permitsGameOnlyValidation: false,
                    requiresCatalogConfiguration: false);
            }
            catch (Exception exception) when (exception is ArgumentException)
            {
                return Failure(
                    ProviderConfigurationCode + ": Invalid gameplay tag build context: "
                    + exception.Message,
                    permitsGameOnlyValidation: true);
            }
        }

        private static string[] DiscoverInstalledPackageMetadataPaths()
        {
            var paths = new List<string>();
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (string.IsNullOrEmpty(package.resolvedPath)) continue;
                var path = Path.Combine(
                    package.resolvedPath,
                    PackageMetadataRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) paths.Add(path);
            }

            return paths.ToArray();
        }

        private static string[] MergeMetadataPaths(
            IReadOnlyList<string> externalPaths,
            IReadOnlyList<string> installedPaths)
        {
            var comparison = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var unique = new HashSet<string>(comparison);
            AddPaths(externalPaths, unique);
            AddPaths(installedPaths, unique);
            var paths = new string[unique.Count];
            unique.CopyTo(paths);
            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }

        private static void AddPaths(IReadOnlyList<string> values, HashSet<string> destination)
        {
            for (var index = 0; index < values.Count; index++)
            {
                var path = values[index];
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new ArgumentException("Source metadata path cannot be empty.", nameof(values));
                }

                destination.Add(Path.GetFullPath(path));
            }
        }

        private static GameplayTagBuildContextResolution Failure(
            string diagnostic,
            bool permitsGameOnlyValidation,
            bool requiresCatalogConfiguration = false,
            string? localSourcePath = null) =>
            new GameplayTagBuildContextResolution(
                null,
                new[] { new GameplayTagWorkspaceDiagnostic(diagnostic, localSourcePath) },
                permitsGameOnlyValidation,
                requiresCatalogConfiguration);
    }
}
