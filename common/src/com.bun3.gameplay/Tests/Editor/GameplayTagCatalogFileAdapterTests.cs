#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Gameplay.Unity.Tests
{
    [TestFixture]
    public sealed class GameplayTagCatalogFileAdapterTests
    {
        private string _temporaryDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "bun3-tag-file-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
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
        public void Save_writes_utf8_without_bom_and_reload_reads_external_change()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");

            GameplayTagCatalogFileAdapter.Save(path, session);
            var bytes = File.ReadAllBytes(path);
            Assert.That(bytes.Take(3).ToArray(), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(GameplayTagCatalogFileAdapter.Load(path).Serialize(), Does.Contain("State.Dead"));

            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Alive\"}]}",
                new UTF8Encoding(false, true));
            Assert.That(GameplayTagCatalogFileAdapter.Load(path).Serialize(), Does.Contain("State.Alive"));
        }

        [Test]
        public void Invalid_json_never_overwrites_existing_file()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            const string original = "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}";
            File.WriteAllText(path, original, new UTF8Encoding(false, true));

            Assert.Throws<TagCatalogException>(
                () => GameplayTagCatalogFileAdapter.SaveJson(
                    path, "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State_Bad\"}]}"));

            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(_temporaryDirectory, "*.tmp"), Is.Empty);
        }

        [Test]
        public void Save_replaces_an_existing_destination_and_leaves_no_temporary_file()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}",
                new UTF8Encoding(false, true));
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Alive\"}]}");

            GameplayTagCatalogFileAdapter.Save(path, session);

            Assert.That(File.ReadAllText(path), Does.Contain("State.Alive"));
            Assert.That(Directory.GetFiles(_temporaryDirectory, "*.tmp"), Is.Empty);
        }

        [Test]
        public void TryToAssetPath_accepts_only_files_below_the_project_assets_directory()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var insideAssets = Path.Combine(Application.dataPath, "Tags", "GameplayTags.json");
            var assetsItself = Path.Combine(projectRoot, "Assets");
            var sibling = Path.Combine(projectRoot, "AssetsSibling", "GameplayTags.json");
            var outside = Path.Combine(projectRoot, "ProjectSettings", "GameplayTags.json");

            Assert.That(GameplayTagCatalogFileAdapter.TryToAssetPath(insideAssets, out var assetPath), Is.True);
            Assert.That(assetPath, Is.EqualTo("Assets/Tags/GameplayTags.json"));
            Assert.That(GameplayTagCatalogFileAdapter.TryToAssetPath(assetsItself, out _), Is.False);
            Assert.That(GameplayTagCatalogFileAdapter.TryToAssetPath(sibling, out _), Is.False);
            Assert.That(GameplayTagCatalogFileAdapter.TryToAssetPath(outside, out _), Is.False);
        }

        [Test]
        public void TryToAssetPath_applies_the_requested_platform_case_comparison()
        {
            var projectDirectory = Path.Combine(_temporaryDirectory, "Project");
            var assetsDirectory = Path.Combine(projectDirectory, "Assets");
            var caseChangedPath = Path.Combine(
                projectDirectory,
                "assets",
                "Tags",
                "GameplayTags.json");

            Assert.That(
                GameplayTagCatalogFileAdapter.TryToAssetPath(
                    caseChangedPath,
                    assetsDirectory,
                    StringComparison.Ordinal,
                    out _),
                Is.False);
            Assert.That(
                GameplayTagCatalogFileAdapter.TryToAssetPath(
                    caseChangedPath,
                    assetsDirectory,
                    StringComparison.OrdinalIgnoreCase,
                    out var assetPath),
                Is.True);
            Assert.That(assetPath, Is.EqualTo("Assets/Tags/GameplayTags.json"));
        }
    }
}
