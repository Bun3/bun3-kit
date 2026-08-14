#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

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
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\",\"comment\":\"\"}]}");

            GameplayTagCatalogFileAdapter.Save(path, session);
            var bytes = File.ReadAllBytes(path);
            Assert.That(bytes.Take(3).ToArray(), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(GameplayTagCatalogFileAdapter.Load(path).Serialize(), Does.Contain("state.dead"));

            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Alive\",\"comment\":\"\"}],\"redirects\":[]}",
                new UTF8Encoding(false, true));
            Assert.That(GameplayTagCatalogFileAdapter.Load(path).Serialize(), Does.Contain("state.alive"));
        }

        [Test]
        public void Invalid_json_never_overwrites_existing_file()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            const string original = "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\",\"comment\":\"\"}],\"redirects\":[]}";
            File.WriteAllText(path, original, new UTF8Encoding(false, true));

            Assert.Throws<TagCatalogException>(
                () => GameplayTagCatalogFileAdapter.SaveJson(
                    path, "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State_Bad\",\"comment\":\"\"}],\"redirects\":[]}"));

            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(_temporaryDirectory, "*.tmp"), Is.Empty);
        }

        [Test]
        public void Save_replaces_an_existing_destination_and_leaves_no_temporary_file()
        {
            var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            File.WriteAllText(path,
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\",\"comment\":\"\"}],\"redirects\":[]}",
                new UTF8Encoding(false, true));
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Alive\",\"comment\":\"\"}]}");

            GameplayTagCatalogFileAdapter.Save(path, session);

            Assert.That(File.ReadAllText(path), Does.Contain("state.alive"));
            Assert.That(Directory.GetFiles(_temporaryDirectory, "*.tmp"), Is.Empty);
        }

        [Test]
        public void Create_game_source_writes_the_exact_empty_source_document()
        {
            var path = Path.Combine(_temporaryDirectory, "ProjectSettings", "GameplayTags.json");

            GameplayTagCatalogFileAdapter.CreateGameSource(path);

            Assert.That(File.ReadAllText(path), Is.EqualTo(
                "{\n"
                + "  \"schemaVersion\": 1,\n"
                + "  \"tags\": [],\n"
                + "  \"redirects\": []\n"
                + "}\n"));
        }

        [Test]
        public void Import_existing_normalizes_into_fixed_destination_without_changing_source()
        {
            var source = Path.Combine(_temporaryDirectory, "OldGameplayTags.json");
            var destination = Path.Combine(
                _temporaryDirectory, "ProjectSettings", "GameplayTags.json");
            const string original =
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"Ability.Jump\",\"comment\":\"Jump\"}],\"redirects\":[{\"from\":\"Ability.Old\",\"to\":\"Ability.Jump\"}]}";
            File.WriteAllText(source, original, new UTF8Encoding(false));

            GameplayTagCatalogFileAdapter.ImportExisting(source, destination);

            Assert.That(File.ReadAllText(source), Is.EqualTo(original));
            Assert.That(File.ReadAllText(destination), Does.Contain("ability.jump"));
            Assert.That(File.ReadAllText(destination), Does.Contain("ability.old"));
            Assert.That(File.ReadAllText(destination), Does.Not.Contain("Ability"));
        }

        [Test]
        public void Invalid_import_leaves_source_and_existing_destination_byte_for_byte()
        {
            var source = Path.Combine(_temporaryDirectory, "Invalid.json");
            var destination = Path.Combine(_temporaryDirectory, "GameplayTags.json");
            var sourceBytes = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"Bad_Name\",\"comment\":\"\"}],\"redirects\":[]}");
            var destinationBytes = Encoding.UTF8.GetBytes(
                "{\"schemaVersion\":1,\"tags\":[],\"redirects\":[]}");
            File.WriteAllBytes(source, sourceBytes);
            File.WriteAllBytes(destination, destinationBytes);

            Assert.Throws<TagCatalogException>(() =>
                GameplayTagCatalogFileAdapter.ImportExisting(source, destination));

            Assert.That(File.ReadAllBytes(source), Is.EqualTo(sourceBytes));
            Assert.That(File.ReadAllBytes(destination), Is.EqualTo(destinationBytes));
            Assert.That(Directory.GetFiles(_temporaryDirectory, "*.tmp"), Is.Empty);
        }
    }
}
