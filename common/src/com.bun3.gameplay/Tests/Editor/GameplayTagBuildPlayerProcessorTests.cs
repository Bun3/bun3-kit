#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;
using UnityEditor.Build;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>Unity 게시 빌드가 고정된 외부 Catalog만 검증하고 포함하는지 확인합니다.</summary>
    [TestFixture]
    public sealed class GameplayTagBuildPlayerProcessorTests
    {
        private string _temporaryDirectory = null!;

        /// <summary>각 테스트의 게시 artifact와 가짜 프로젝트를 격리합니다.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "bun3-tag-build-player-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
        }

        /// <summary>테스트가 만든 게시 artifact와 가짜 프로젝트를 제거합니다.</summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, true);
            }
        }

        /// <summary>유효한 게시 artifact를 정해진 player 경로에 정확히 한 번 포함하는지 검증합니다.</summary>
        [Test]
        public void Valid_pinned_catalog_is_binary_round_tripped_and_included_exactly_once()
        {
            var context = WritePublishedCatalog("release-game", "4.2.0", "ability.jump");
            var originalBytes = File.ReadAllBytes(context.ArtifactPath);
            var additions = new List<(string Source, string Destination)>();

            GameplayTagBuildPlayerProcessor.PrepareForBuild(
                () => context,
                (source, destination) => additions.Add((source, destination)));

            Assert.That(additions, Has.Count.EqualTo(1));
            Assert.That(additions[0].Source, Is.EqualTo(Path.GetFullPath(context.ArtifactPath)));
            Assert.That(additions[0].Destination,
                Is.EqualTo("Bun3/GameplayTags/GameplayTags.catalog"));
            Assert.That(File.ReadAllBytes(context.ArtifactPath), Is.EqualTo(originalBytes));
            Assert.That(Directory.GetFiles(_temporaryDirectory),
                Is.EquivalentTo(new[] { context.ArtifactPath }));
        }

        /// <summary>누락되거나 손상된 게시 artifact가 player build 시작 전에 실패하는지 검증합니다.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Missing_or_corrupt_catalog_fails_before_any_player_inclusion(bool corrupt)
        {
            GameplayTagPublishedCatalogContext context;
            if (corrupt)
            {
                var path = Path.Combine(_temporaryDirectory, "GameplayTags.catalog");
                File.WriteAllBytes(path, new byte[] { (byte)'B', (byte)'3', (byte)'D', (byte)'K' });
                context = new GameplayTagPublishedCatalogContext(
                    path, "release-game", "4.2.0", new byte[32]);
            }
            else
            {
                context = new GameplayTagPublishedCatalogContext(
                    Path.Combine(_temporaryDirectory, "missing.catalog"),
                    "release-game",
                    "4.2.0",
                    new byte[32]);
            }

            var inclusionCount = 0;

            Assert.Throws<BuildFailedException>(() =>
                GameplayTagBuildPlayerProcessor.PrepareForBuild(
                    () => context,
                    (_, _) => inclusionCount++));
            Assert.That(inclusionCount, Is.Zero);
        }

        /// <summary>게시 build metadata의 ID, Version 또는 fingerprint 불일치를 모두 거부하는지 검증합니다.</summary>
        [TestCase("id")]
        [TestCase("version")]
        [TestCase("fingerprint")]
        public void Published_identity_or_fingerprint_mismatch_fails_before_inclusion(string mismatch)
        {
            var actual = WritePublishedCatalog("release-game", "4.2.0", "ability.jump");
            var fingerprint = actual.ExpectedFingerprint.ToArray();
            var expectedId = actual.CatalogId;
            var expectedVersion = actual.CatalogVersion;
            if (mismatch == "id") expectedId = "other-game";
            if (mismatch == "version") expectedVersion = "4.2.1";
            if (mismatch == "fingerprint") fingerprint[0] ^= 0xff;
            var mismatched = new GameplayTagPublishedCatalogContext(
                actual.ArtifactPath, expectedId, expectedVersion, fingerprint);
            var inclusionCount = 0;

            Assert.Throws<BuildFailedException>(() =>
                GameplayTagBuildPlayerProcessor.PrepareForBuild(
                    () => mismatched,
                    (_, _) => inclusionCount++));
            Assert.That(inclusionCount, Is.Zero);
        }

        /// <summary>게시 provider가 없거나 여러 개이면 artifact를 열기 전에 설정 오류로 실패하는지 검증합니다.</summary>
        [Test]
        public void Published_provider_resolution_requires_exactly_one_concrete_provider()
        {
            Assert.Throws<BuildFailedException>(() =>
                GameplayTagPublishedCatalogValidator.ResolveAndValidate(Array.Empty<Type>()));
            Assert.Throws<BuildFailedException>(() =>
                GameplayTagPublishedCatalogValidator.ResolveAndValidate(
                    new[] { typeof(ValidProvider), typeof(SecondProvider) }));
        }

        /// <summary>provider의 제품 ID와 게시 context의 ID가 다르면 중복 설정을 stale 상태로 거부하는지 검증합니다.</summary>
        [Test]
        public void Provider_catalog_id_must_match_the_published_context_catalog_id()
        {
            MismatchedProvider.Context = WritePublishedCatalog(
                "artifact-game", "4.2.0", "ability.jump");

            Assert.Throws<BuildFailedException>(() =>
                GameplayTagPublishedCatalogValidator.ResolveAndValidate(
                    new[] { typeof(MismatchedProvider) }));
        }

        /// <summary>preprocess 반복 호출이 이전 context를 cache하지 않고 새 고정값을 다시 검증하는지 확인합니다.</summary>
        [Test]
        public void Repeated_preprocess_resolves_fresh_context_and_rejects_a_stale_second_pin()
        {
            var valid = WritePublishedCatalog("release-game", "4.2.0", "ability.jump");
            var staleFingerprint = valid.ExpectedFingerprint.ToArray();
            staleFingerprint[0] ^= 0xff;
            var stale = new GameplayTagPublishedCatalogContext(
                valid.ArtifactPath, valid.CatalogId, valid.CatalogVersion, staleFingerprint);
            var resolveCount = 0;
            var inclusionCount = 0;

            GameplayTagBuildPlayerProcessor.PrepareForBuild(
                Resolve,
                (_, _) => inclusionCount++);
            Assert.Throws<BuildFailedException>(() =>
                GameplayTagBuildPlayerProcessor.PrepareForBuild(
                    Resolve,
                    (_, _) => inclusionCount++));

            Assert.That(resolveCount, Is.EqualTo(2));
            Assert.That(inclusionCount, Is.EqualTo(1));

            GameplayTagPublishedCatalogContext Resolve()
            {
                resolveCount++;
                return resolveCount == 1 ? valid! : stale!;
            }
        }

        /// <summary>반복 build가 같은 artifact를 결정적으로 포함하고 작성용 JSON이나 프로젝트를 수정하지 않는지 검증합니다.</summary>
        [Test]
        public void Repeat_build_is_deterministic_and_does_not_stage_or_compile_authoring_files()
        {
            var projectRoot = Path.Combine(_temporaryDirectory, "Project");
            var projectSettings = Path.Combine(projectRoot, "ProjectSettings");
            var assets = Path.Combine(projectRoot, "Assets");
            Directory.CreateDirectory(projectSettings);
            Directory.CreateDirectory(assets);
            var gameSourcePath = Path.Combine(projectSettings, "GameplayTags.json");
            var malformedAuthoringBytes = new byte[] { 1, 3, 3, 7 };
            File.WriteAllBytes(gameSourcePath, malformedAuthoringBytes);
            var context = WritePublishedCatalog("release-game", "4.2.0", "ability.jump");
            var publishedBytes = File.ReadAllBytes(context.ArtifactPath);
            var additions = new List<(string Source, string Destination)>();
            var originalCurrentDirectory = Environment.CurrentDirectory;

            try
            {
                Environment.CurrentDirectory = projectRoot;
                GameplayTagBuildPlayerProcessor.PrepareForBuild(
                    () => context,
                    (source, destination) => additions.Add((source, destination)));
                GameplayTagBuildPlayerProcessor.PrepareForBuild(
                    () => context,
                    (source, destination) => additions.Add((source, destination)));
            }
            finally
            {
                Environment.CurrentDirectory = originalCurrentDirectory;
            }

            Assert.That(additions, Has.Count.EqualTo(2));
            Assert.That(additions[0], Is.EqualTo(additions[1]));
            Assert.That(File.ReadAllBytes(context.ArtifactPath), Is.EqualTo(publishedBytes));
            Assert.That(File.ReadAllBytes(gameSourcePath), Is.EqualTo(malformedAuthoringBytes));
            Assert.That(Directory.Exists(Path.Combine(assets, "StreamingAssets")), Is.False);
            Assert.That(Directory.GetFiles(projectRoot, "*.tmp", SearchOption.AllDirectories), Is.Empty);
        }

        private GameplayTagPublishedCatalogContext WritePublishedCatalog(
            string catalogId,
            string catalogVersion,
            string tag)
        {
            var source = new TagSourceDocument(
                new TagSourceDescriptor("game", "Game", TagSourceKind.GameJson, false),
                "ProjectSettings/GameplayTags.json",
                new[] { new TagSourceTag(tag, "test") },
                Array.Empty<TagSourceRedirect>());
            var compilation = TagCatalogCompiler.Compile(
                new[] { source },
                new TagCatalogIdentity(catalogId, catalogVersion));
            Assert.That(compilation.Succeeded, Is.True);
            var path = Path.Combine(
                _temporaryDirectory,
                Guid.NewGuid().ToString("N") + "-GameplayTags.catalog");
            using (var output = File.Create(path))
            {
                TagCatalogBinaryWriter.Write(output, compilation.Catalog!);
            }

            return new GameplayTagPublishedCatalogContext(
                path,
                catalogId,
                catalogVersion,
                compilation.Catalog!.Fingerprint);
        }

        private sealed class ValidProvider : IGameplayTagBuildContextProvider
        {
            /// <inheritdoc />
            public string CatalogId => "release-game";

            /// <inheritdoc />
            public IReadOnlyList<string> ExternalSourceMetadataPaths => Array.Empty<string>();

            /// <inheritdoc />
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() =>
                throw new InvalidOperationException("This provider must not be opened in the count test.");
        }

        private sealed class SecondProvider : IGameplayTagBuildContextProvider
        {
            /// <inheritdoc />
            public string CatalogId => "second-game";

            /// <inheritdoc />
            public IReadOnlyList<string> ExternalSourceMetadataPaths => Array.Empty<string>();

            /// <inheritdoc />
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() =>
                throw new InvalidOperationException("This provider must not be opened in the count test.");
        }

        private sealed class MismatchedProvider : IGameplayTagBuildContextProvider
        {
            internal static GameplayTagPublishedCatalogContext Context { get; set; } = null!;

            /// <inheritdoc />
            public string CatalogId => "configured-game";

            /// <inheritdoc />
            public IReadOnlyList<string> ExternalSourceMetadataPaths => Array.Empty<string>();

            /// <inheritdoc />
            public GameplayTagPublishedCatalogContext GetPublishedCatalog() => Context;
        }
    }
}
