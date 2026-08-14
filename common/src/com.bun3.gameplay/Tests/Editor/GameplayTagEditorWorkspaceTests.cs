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

        /// <summary>설정되지 않은 Catalog와 여러 Provider를 안정적인 진단으로 구분하는지 검증합니다.</summary>
        [Test]
        public void Resolver_reports_unconfigured_settings_and_multiple_providers_with_stable_diagnostics()
        {
            var gameSourcePath = WriteGameSource("game.json", "ability.jump");

            var missing = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                Array.Empty<Type>(),
                Array.Empty<string>(),
                null);
            var multiple = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                new[] { typeof(SecondProvider), typeof(FakeProvider) },
                Array.Empty<string>(),
                "test-game");

            Assert.That(missing.HasCompleteContext, Is.False);
            Assert.That(missing.Context, Is.Null);
            Assert.That(missing.RequiresCatalogConfiguration, Is.True);
            Assert.That(missing.Diagnostics, Is.EqualTo(new[]
            {
                "B3TAG3004: GameplayTag Catalog settings are not configured."
            }));
            Assert.That(multiple.HasCompleteContext, Is.False);
            Assert.That(multiple.Context, Is.Null);
            Assert.That(multiple.RequiresCatalogConfiguration, Is.False);
            Assert.That(multiple.Diagnostics, Is.EqualTo(new[]
            {
                "B3TAG3001: Exactly one gameplay tag build context provider is required; found 2. "
                + "Candidates: Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests+FakeProvider, "
                + "Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests+SecondProvider."
            }));
        }

        /// <summary>코드 Provider가 없어도 Project Settings ID로 완전한 개발 context를 만드는지 검증합니다.</summary>
        [Test]
        public void Project_settings_catalog_id_is_the_development_fallback_without_a_provider()
        {
            var path = WriteGameSource("game.json", "ability.jump");
            var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
                path, Array.Empty<Type>(), Array.Empty<string>(), "jurassic-paradise");

            Assert.That(resolution.HasCompleteContext, Is.True);
            Assert.That(resolution.RequiresCatalogConfiguration, Is.False);
            Assert.That(resolution.Context!.Identity.CatalogId, Is.EqualTo("jurassic-paradise"));
            Assert.That(resolution.Context.Sources, Has.Count.EqualTo(1));
        }

        /// <summary>개발 context가 Provider와 Project Settings의 선택 매트릭스를 따르는지 검증합니다.</summary>
        [Test]
        public void Resolver_selects_development_context_from_the_provider_and_settings_matrix()
        {
            var gameSourcePath = WriteGameSource("game.json", "ability.jump");

            var providerWithoutSettings = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                new[] { typeof(FakeProvider) },
                Array.Empty<string>(),
                null);
            var providerWithMatchingSettings = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                new[] { typeof(FakeProvider) },
                Array.Empty<string>(),
                "test-game");
            var providerWithMismatchedSettings = GameplayTagBuildContextResolver.ResolveDevelopment(
                Path.Combine(_temporaryDirectory, "unopened-game.json"),
                new[] { typeof(FakeProvider) },
                Array.Empty<string>(),
                "other-game");

            Assert.That(providerWithoutSettings.HasCompleteContext, Is.True);
            Assert.That(providerWithoutSettings.Context!.Identity.CatalogId, Is.EqualTo("test-game"));
            Assert.That(providerWithMatchingSettings.HasCompleteContext, Is.True);
            Assert.That(providerWithMatchingSettings.Context!.Identity.CatalogId, Is.EqualTo("test-game"));
            Assert.That(providerWithMismatchedSettings.HasCompleteContext, Is.False);
            Assert.That(providerWithMismatchedSettings.Diagnostics, Is.EqualTo(new[]
            {
                "B3TAG3002: GameplayTag Catalog ID does not match Project Settings."
            }));
        }

        /// <summary>Provider Catalog ID가 canonical 형식이 아니면 같은 설정 ID로 정규화되더라도 거부하는지 검증합니다.</summary>
        [Test]
        public void Resolver_rejects_a_noncanonical_provider_catalog_id_before_matching_project_settings()
        {
            var gameSourcePath = WriteGameSource("game.json", "ability.jump");
            FakeProvider.CatalogIdValue = "TEST GAME";

            var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                new[] { typeof(FakeProvider) },
                Array.Empty<string>(),
                "test-game");

            Assert.That(resolution.HasCompleteContext, Is.False);
            Assert.That(resolution.Diagnostics, Is.EqualTo(new[]
            {
                "B3TAG3002: Invalid gameplay tag build context provider: "
                + "Catalog ID must use its canonical lowercase ASCII-hyphen form."
            }));
        }

        /// <summary>비정규형 Project Settings ID를 Provider와 비교하기 전에 거부하는지 검증합니다.</summary>
        [Test]
        public void Resolver_requires_a_raw_ordinal_catalog_id_match_with_project_settings()
        {
            var gameSourcePath = WriteGameSource("game.json", "ability.jump");

            var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
                gameSourcePath,
                new[] { typeof(FakeProvider) },
                Array.Empty<string>(),
                "test--game");

            Assert.That(resolution.HasCompleteContext, Is.False);
            Assert.That(resolution.Diagnostics, Has.Count.EqualTo(1));
            Assert.That(resolution.Diagnostics[0], Does.StartWith(
                "B3TAG3002: Invalid GameplayTag Project Settings:"));
            Assert.That(resolution.Diagnostics[0], Does.Contain(
                "canonical lowercase ASCII-hyphen form"));
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
            Assert.That(workspace.RequiresCatalogConfiguration, Is.True);
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
