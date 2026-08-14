#nullable enable
using System;
using System.IO;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>개발 Catalog의 원자적 binary round-trip 계약을 검증합니다.</summary>
    [TestFixture]
    public sealed class GameplayTagDevelopmentCatalogTests
    {
        private string _temporaryDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "bun3-tag-development-catalog-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, true);
        }

        [Test]
        public void Build_writes_the_exact_development_path_and_returns_the_binary_reloaded_catalog()
        {
            var workspace = CreateValidWorkspace("round-trip-game", "ability.jump");
            var expectedPath = TagCatalogDevelopmentPath.Get(
                "round-trip-game", _temporaryDirectory);

            var catalog = GameplayTagDevelopmentCatalogBuilder.Build(
                workspace, _temporaryDirectory);

            Assert.That(File.Exists(expectedPath), Is.True);
            Assert.That(catalog, Is.Not.SameAs(workspace.Snapshot!.Catalog));
            Assert.That(catalog.CatalogId, Is.EqualTo("round-trip-game"));
            Assert.That(catalog.CatalogVersion, Is.EqualTo("0.0.0-dev"));
            Assert.That(catalog.TryGet("ability.jump", out _), Is.True);
            using var input = File.OpenRead(expectedPath);
            var independentlyReloaded = TagCatalogBinary.Load(
                input, TagCatalogExpectations.ForDevelopment("round-trip-game"));
            Assert.That(independentlyReloaded.Fingerprint.ToArray(),
                Is.EqualTo(catalog.Fingerprint.ToArray()));
        }

        [Test]
        public void Invalid_workspace_preserves_the_last_good_cache_bytes()
        {
            var destination = TagCatalogDevelopmentPath.Get("invalid-game", _temporaryDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var previous = new byte[] { 11, 22, 33, 44 };
            File.WriteAllBytes(destination, previous);
            var source = CreateGameSource("ability.jump");
            var invalidResolution = new GameplayTagBuildContextResolution(
                null,
                new[] { "B3TAG3001: provider missing" },
                permitsGameOnlyValidation: true);
            var workspace = GameplayTagEditorWorkspace.Open(invalidResolution, source);

            var error = Assert.Throws<InvalidOperationException>(() =>
                GameplayTagDevelopmentCatalogBuilder.Build(workspace, _temporaryDirectory));

            Assert.That(error!.Message, Does.Contain("B3TAG3001"));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(previous));
        }

        [Test]
        public void Binary_readback_failure_preserves_the_last_good_cache_and_removes_the_temporary_file()
        {
            var workspace = CreateValidWorkspace("readback-game", "ability.jump");
            var destination = TagCatalogDevelopmentPath.Get("readback-game", _temporaryDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var previous = new byte[] { 5, 4, 3, 2, 1 };
            File.WriteAllBytes(destination, previous);

            Assert.Throws<TagCatalogFormatException>(() =>
                GameplayTagDevelopmentCatalogBuilder.Build(
                    workspace,
                    _temporaryDirectory,
                    (_, _) => throw new TagCatalogFormatException("forced readback failure")));

            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(previous));
            Assert.That(
                Directory.GetFiles(
                    Path.GetDirectoryName(destination)!,
                    ".GameplayTags.catalog.*.tmp"),
                Is.Empty);
        }

        internal static GameplayTagEditorWorkspace CreateValidWorkspace(
            string catalogId,
            string tag)
        {
            var source = CreateGameSource(tag);
            var context = new GameCatalogBuildContext(
                new TagCatalogIdentity(catalogId, "0.0.0-dev"),
                CatalogBuildMode.Development,
                new[] { source });
            return GameplayTagEditorWorkspace.Open(
                new GameplayTagBuildContextResolution(
                    context, Array.Empty<string>(), permitsGameOnlyValidation: false),
                source);
        }

        private static TagSourceDocument CreateGameSource(string tag) =>
            new TagSourceDocument(
                new TagSourceDescriptor("game", "Game", TagSourceKind.GameJson, false),
                "ProjectSettings/GameplayTags.json",
                new[] { new TagSourceTag(tag, "test") },
                Array.Empty<TagSourceRedirect>());
    }
}
