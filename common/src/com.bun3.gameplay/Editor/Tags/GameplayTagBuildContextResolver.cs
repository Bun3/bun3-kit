#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using UnityEditor;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Unity Editor provider와 설치된 package에서 Catalog build context를 resolve합니다.</summary>
    public static class GameplayTagBuildContextResolver
    {
        private const string PackageMetadataRelativePath = "Bun3/GameplayTags/TagSource.json";
        private const string ProviderCountCode = "B3TAG3001";
        private const string ProviderConfigurationCode = "B3TAG3002";
        private const string SourceLoadCode = "B3TAG3003";

        /// <summary>Unity의 타입 cache와 설치 package를 사용해 개발 build context를 resolve합니다.</summary>
        /// <param name="gameSourcePath">고정 Game Source 절대 경로입니다.</param>
        /// <returns>완전한 context 또는 작업을 막는 진단입니다.</returns>
        public static GameplayTagBuildContextResolution ResolveDevelopment(string gameSourcePath)
        {
            var providerTypes = new List<Type>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IGameplayTagBuildContextProvider>())
            {
                providerTypes.Add(type);
            }

            return ResolveDevelopment(
                gameSourcePath,
                providerTypes,
                DiscoverInstalledPackageMetadataPaths());
        }

        internal static GameplayTagBuildContextResolution ResolveDevelopment(
            string gameSourcePath,
            IReadOnlyList<Type> providerTypes,
            IReadOnlyList<string> installedPackageMetadataPaths)
        {
            if (gameSourcePath is null) throw new ArgumentNullException(nameof(gameSourcePath));
            if (providerTypes is null) throw new ArgumentNullException(nameof(providerTypes));
            if (installedPackageMetadataPaths is null)
            {
                throw new ArgumentNullException(nameof(installedPackageMetadataPaths));
            }

            var candidates = new List<Type>();
            for (var index = 0; index < providerTypes.Count; index++)
            {
                var type = providerTypes[index] ?? throw new ArgumentNullException(nameof(providerTypes));
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
                return Failure(
                    ProviderCountCode + ": Exactly one gameplay tag build context provider is required; found "
                    + candidates.Count + ".",
                    permitsGameOnlyValidation: true);
            }

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

            IReadOnlyList<string> externalPaths;
            string catalogId;
            try
            {
                catalogId = provider.CatalogId;
                externalPaths = provider.ExternalSourceMetadataPaths
                    ?? throw new InvalidOperationException("External Source Metadata path list is null.");
                _ = new TagCatalogIdentity(catalogId, TagCatalogVersions.Development);
            }
            catch (Exception exception)
            {
                return Failure(
                    ProviderConfigurationCode + ": Invalid gameplay tag build context provider: "
                    + exception.Message,
                    permitsGameOnlyValidation: true);
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
                    permitsGameOnlyValidation: false);
            }
            catch (Exception exception) when (exception is ArgumentException)
            {
                return Failure(
                    ProviderConfigurationCode + ": Invalid gameplay tag build context: "
                    + exception.Message,
                    permitsGameOnlyValidation: true);
            }
        }

        private static ConstructorInfo? FindParameterlessConstructor(Type type) =>
            type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);

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
                    throw new ArgumentException("Source Metadata path는 비어 있을 수 없습니다.", nameof(values));
                }

                destination.Add(Path.GetFullPath(path));
            }
        }

        private static GameplayTagBuildContextResolution Failure(
            string diagnostic,
            bool permitsGameOnlyValidation,
            string? localSourcePath = null) =>
            new GameplayTagBuildContextResolution(
                null,
                new[] { new GameplayTagWorkspaceDiagnostic(diagnostic, localSourcePath) },
                permitsGameOnlyValidation);
    }
}
