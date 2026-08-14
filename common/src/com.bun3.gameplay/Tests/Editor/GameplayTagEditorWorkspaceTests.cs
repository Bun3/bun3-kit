#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    [TestFixture]
    public sealed class GameplayTagEditorWorkspaceTests
    {
        private string _temporaryDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "bun3-tag-workspace-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
            FakeProvider.CatalogIdValue = "test-game";
            FakeProvider.ExternalSourceMetadataPathsValue = Array.Empty<string>();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, true);
            }
        }

        [Test]
        public void Game_source_path_maps_the_project_assets_directory_only_to_project_settings()
        {
            var project = Path.Combine(_temporaryDirectory, "Project");
            var dataPath = Path.Combine(project, "Assets");

            var result = GameplayTagGameSourcePath.Get(dataPath);

            Assert.That(result, Is.EqualTo(Path.GetFullPath(
                Path.Combine(project, "ProjectSettings", "GameplayTags.json"))));
            Assert.That(result, Does.Not.StartWith(Path.GetFullPath(dataPath) + Path.DirectorySeparatorChar));
        }

        /// <summary>Provider 개수 오류가 실제 후보 타입을 안정적인 순서로 보여 주는지 검증합니다.</summary>
        [Test]
        public void Resolver_requires_exactly_one_valid_provider_with_stable_diagnostics()
        {
            var gameSourcePath = WriteGameSource("game.json", "ability.jump");

            var missing = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                Array.Empty<Type>(),
                Array.Empty<string>());
            var multiple = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                new[] { typeof(SecondProvider), typeof(FakeProvider) },
                Array.Empty<string>());

            Assert.That(missing.HasCompleteContext, Is.False);
            Assert.That(missing.Context, Is.Null);
            Assert.That(missing.Diagnostics, Is.EqualTo(new[]
            {
                "B3TAG3001: Exactly one gameplay tag build context provider is required; found 0."
            }));
            Assert.That(multiple.HasCompleteContext, Is.False);
            Assert.That(multiple.Context, Is.Null);
            Assert.That(multiple.Diagnostics, Is.EqualTo(new[]
            {
                "B3TAG3001: Exactly one gameplay tag build context provider is required; found 2. "
                + "Candidates: Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests+FakeProvider, "
                + "Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests+SecondProvider."
            }));
        }

        /// <summary>전역 Provider 탐색이 현재 Unity 테스트 어셈블리의 더블을 반환하지 않는지 검증합니다.</summary>
        [Test]
        public void Global_discovery_excludes_providers_from_test_assemblies()
        {
            var providers = GameplayTagBuildContextProviderDiscovery.Discover();
            var testAssembly = typeof(GameplayTagEditorWorkspaceTests).Assembly;

            Assert.That(providers.All(provider => provider.Assembly != testAssembly), Is.True);
        }

        [Test]
        public void Resolver_combines_provider_and_installed_package_metadata_into_development_context()
        {
            var gameSourcePath = WriteGameSource("game.json", "ability.jump");
            var externalPath = WriteMetadataSource(
                "external.json", "framework.external", "ability.jump");
            var installedPath = WriteMetadataSource(
                "installed.json", "framework.installed", "state.dead");
            FakeProvider.ExternalSourceMetadataPathsValue = new[] { externalPath };

            var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                new[] { typeof(AbstractProvider), typeof(GenericProvider<>), typeof(FakeProvider) },
                new[] { installedPath });

            Assert.That(resolution.HasCompleteContext, Is.True);
            Assert.That(resolution.Diagnostics, Is.Empty);
            Assert.That(resolution.Context, Is.Not.Null);
            Assert.That(resolution.Context!.Identity.CatalogId, Is.EqualTo("test-game"));
            Assert.That(resolution.Context.Identity.CatalogVersion, Is.EqualTo("0.0.0-dev"));
            Assert.That(
                resolution.Context.Sources.Select(source => source.Descriptor.SourceId),
                Is.EquivalentTo(new[] { "game", "framework.external", "framework.installed" }));
            Assert.That(
                resolution.Context.Sources.Where(source => source.Descriptor.SourceId != "game")
                    .All(source => source.Descriptor.IsReadOnly),
                Is.True);
        }

        [Test]
        public void Missing_provider_keeps_valid_game_source_editable_without_exposing_partial_snapshot()
        {
            var gameSourcePath = WriteGameSource("game.json", "Ability.Jump");
            var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                Array.Empty<Type>(),
                Array.Empty<string>());

            var workspace = GameplayTagEditorWorkspace.Open(resolution, gameSourcePath);

            Assert.That(workspace.CanCreateGameSource, Is.False);
            Assert.That(workspace.CanEditGameSource, Is.True);
            Assert.That(workspace.CanBuildCatalog, Is.False);
            Assert.That(workspace.GameSession, Is.Not.Null);
            Assert.That(workspace.GameSession!.Serialize(), Does.Contain("ability.jump"));
            Assert.That(workspace.Snapshot, Is.Null);
            Assert.That(workspace.Diagnostics, Is.EqualTo(resolution.Diagnostics));
        }

        [Test]
        public void Malformed_resolved_external_metadata_disables_editing_and_building()
        {
            var gameSourcePath = WriteGameSource("game.json", "ability.jump");
            var externalPath = Path.Combine(_temporaryDirectory, "broken.json");
            File.WriteAllText(externalPath, "{not json", new UTF8Encoding(false));
            FakeProvider.ExternalSourceMetadataPathsValue = new[] { externalPath };
            var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                new[] { typeof(FakeProvider) },
                Array.Empty<string>());

            var workspace = GameplayTagEditorWorkspace.Open(resolution, gameSourcePath);

            Assert.That(resolution.HasCompleteContext, Is.False);
            Assert.That(resolution.Diagnostics.Single(), Does.StartWith("B3TAG3003:"));
            Assert.That(workspace.CanEditGameSource, Is.False);
            Assert.That(workspace.CanBuildCatalog, Is.False);
            Assert.That(workspace.Snapshot, Is.Null);
        }

        [Test]
        public void Missing_game_source_is_explicitly_creatable_but_not_editable_or_buildable()
        {
            var gameSourcePath = Path.Combine(_temporaryDirectory, "missing.json");
            var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                Array.Empty<Type>(),
                Array.Empty<string>());

            var workspace = GameplayTagEditorWorkspace.Open(resolution, gameSourcePath);

            Assert.That(workspace.CanCreateGameSource, Is.True);
            Assert.That(workspace.CanEditGameSource, Is.False);
            Assert.That(workspace.CanBuildCatalog, Is.False);
            Assert.That(workspace.GameSession, Is.Null);
            Assert.That(workspace.Snapshot, Is.Null);
        }

        [Test]
        public void Complete_context_exposes_merged_catalog_provenance_and_source_documents()
        {
            var gameSourcePath = WriteGameSource("game.json", "ability.jump");
            var externalPath = WriteMetadataSource(
                "external.json", "framework.external", "ability.jump");
            FakeProvider.ExternalSourceMetadataPathsValue = new[] { externalPath };
            var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                new[] { typeof(FakeProvider) },
                Array.Empty<string>());

            var workspace = GameplayTagEditorWorkspace.Open(resolution, gameSourcePath);

            Assert.That(workspace.CanEditGameSource, Is.True);
            Assert.That(workspace.CanBuildCatalog, Is.True);
            Assert.That(workspace.Snapshot, Is.Not.Null);
            Assert.That(workspace.Snapshot!.Catalog.TryGet("ability.jump", out _), Is.True);
            Assert.That(
                workspace.Snapshot.Provenance.GetContributions("ability.jump").Count,
                Is.EqualTo(2));
            Assert.That(workspace.Snapshot.Sources.Count, Is.EqualTo(2));
        }

        [Test]
        public void Published_context_copies_the_exact_fingerprint()
        {
            var fingerprint = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
            var context = new GameplayTagPublishedCatalogContext(
                "catalog.bin", "test-game", "1.2.3", fingerprint);

            fingerprint[0] = 255;

            Assert.That(context.ArtifactPath, Is.EqualTo("catalog.bin"));
            Assert.That(context.CatalogId, Is.EqualTo("test-game"));
            Assert.That(context.CatalogVersion, Is.EqualTo("1.2.3"));
            Assert.That(context.ExpectedFingerprint.ToArray()[0], Is.Zero);
            Assert.Throws<ArgumentException>(() => new GameplayTagPublishedCatalogContext(
                "catalog.bin", "test-game", "1.2.3", new byte[31]));
        }

        private string WriteGameSource(string fileName, string tag)
        {
            var path = Path.Combine(_temporaryDirectory, fileName);
            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"" + tag
                + "\",\"comment\":\"game\"}],\"redirects\":[]}",
                new UTF8Encoding(false));
            return path;
        }

        private string WriteMetadataSource(
            string fileName,
            string sourceId,
            string tag)
        {
            var path = Path.Combine(_temporaryDirectory, fileName);
            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"source\":{\"id\":\"" + sourceId
                + "\",\"displayName\":\"" + sourceId
                + "\",\"kind\":\"packageJson\"},\"tags\":[{\"name\":\""
                + tag + "\",\"comment\":\"framework\"}],\"redirects\":[]}",
                new UTF8Encoding(false));
            return path;
        }

        public sealed class FakeProvider : IGameplayTagBuildContextProvider
        {
            public static string CatalogIdValue { get; set; } = "test-game";
            public static IReadOnlyList<string> ExternalSourceMetadataPathsValue { get; set; } =
                Array.Empty<string>();

            public string CatalogId => CatalogIdValue;
            public IReadOnlyList<string> ExternalSourceMetadataPaths =>
                ExternalSourceMetadataPathsValue;
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() =>
                new GameplayTagPublishedCatalogContext(
                    "published.catalog", CatalogId, "1.0.0", new byte[32]);
        }

        public sealed class SecondProvider : IGameplayTagBuildContextProvider
        {
            public string CatalogId => "second";
            public IReadOnlyList<string> ExternalSourceMetadataPaths => Array.Empty<string>();
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() =>
                new GameplayTagPublishedCatalogContext(
                    "published.catalog", CatalogId, "1.0.0", new byte[32]);
        }

        public abstract class AbstractProvider : IGameplayTagBuildContextProvider
        {
            public abstract string CatalogId { get; }
            public abstract IReadOnlyList<string> ExternalSourceMetadataPaths { get; }
            public abstract GameplayTagPublishedCatalogContext GetPublishedCatalog();
        }

        public sealed class GenericProvider<T> : IGameplayTagBuildContextProvider
        {
            public string CatalogId => "generic";
            public IReadOnlyList<string> ExternalSourceMetadataPaths => Array.Empty<string>();
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() =>
                new GameplayTagPublishedCatalogContext(
                    "published.catalog", CatalogId, "1.0.0", new byte[32]);
        }
    }
}
