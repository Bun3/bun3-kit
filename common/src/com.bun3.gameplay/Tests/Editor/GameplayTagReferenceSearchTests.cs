#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    [TestFixture]
    public sealed class GameplayTagReferenceSearchTests
    {
        private string _temporaryDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(), "bun3-tag-reference-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, true);
        }

        private string WriteText(string relativePath, string contents)
        {
            var path = Path.Combine(_temporaryDirectory, relativePath);
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, contents, new UTF8Encoding(false, true));
            return path;
        }

        [Test]
        public void Scanner_finds_exact_old_tag_tokens_case_insensitively_in_one_file_pass()
        {
            var path = WriteText("References.cs",
                "var a = \"STATE.KILLED\"; var b = \"Ability.Old\";\n" +
                "var c = \"State.Killed.Child\";");
            var opens = 0;
            var scanner = new GameplayTagTextReferenceScanner(file =>
            {
                opens++;
                return File.OpenText(file);
            });

            var result = scanner.Search(
                new[] { new GameplayTagReferenceFile(path, "Assets/References.cs") },
                new[] { "State.Killed", "Ability.Old" },
                excludedCatalogPath: string.Empty,
                isCancelled: null);

            Assert.That(result.IsComplete, Is.True);
            Assert.That(result.Matches.Select(match => match.RedirectSource),
                Is.EquivalentTo(new[] { "State.Killed", "Ability.Old" }));
            Assert.That(result.Matches.Any(match => match.Preview.Contains("State.Killed.Child")), Is.False);
            Assert.That(opens, Is.EqualTo(1));
        }

        [Test]
        public void Scanner_excludes_the_catalog_and_blocks_cleanup_after_read_error()
        {
            var catalog = WriteText("GameplayTags.json", "State.Killed");
            var locked = WriteText("Locked.asset", "State.Killed");
            using var lockStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var scanner = new GameplayTagTextReferenceScanner(File.OpenText);

            var result = scanner.Search(
                new[]
                {
                    new GameplayTagReferenceFile(catalog, "Assets/GameplayTags.json"),
                    new GameplayTagReferenceFile(locked, "Assets/Locked.asset")
                },
                new[] { "State.Killed" },
                catalog,
                isCancelled: null);

            Assert.That(result.IsComplete, Is.False);
            Assert.That(result.Errors, Is.Not.Empty);
            Assert.That(result.Matches, Is.Empty);
        }

        [Test]
        public void Scanner_skips_binary_content_even_when_the_extension_is_text_capable()
        {
            var path = Path.Combine(_temporaryDirectory, "Binary.asset");
            File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3, 4 });
            var scanner = new GameplayTagTextReferenceScanner(File.OpenText);

            var result = scanner.Search(
                new[] { new GameplayTagReferenceFile(path, "Assets/Binary.asset") },
                new[] { "State.Killed" },
                string.Empty,
                isCancelled: null);

            Assert.That(result.IsComplete, Is.True);
            Assert.That(result.Matches, Is.Empty);
        }

        [Test]
        public void Enumerator_includes_owned_text_roots_and_excludes_cache_meta_and_binary_files()
        {
            var assets = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "Assets")).FullName;
            var settings = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "ProjectSettings")).FullName;
            var library = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "Library")).FullName;
            var localPackage = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "LocalPackage")).FullName;
            File.WriteAllText(Path.Combine(assets, "Scene.unity"), "State.Killed");
            File.WriteAllText(Path.Combine(settings, "Tags.json"), "State.Killed");
            File.WriteAllText(Path.Combine(localPackage, "TagCode.cs"), "State.Killed");
            File.WriteAllText(Path.Combine(assets, "Scene.unity.meta"), "State.Killed");
            File.WriteAllBytes(Path.Combine(assets, "Texture.png"), new byte[] { 0, 1, 2 });
            File.WriteAllText(Path.Combine(library, "Generated.cs"), "State.Killed");

            var files = GameplayTagProjectReferenceFiles.EnumerateOwnedTextFiles(
                _temporaryDirectory,
                new[] { localPackage, localPackage });

            Assert.That(files.Select(file => file.AbsolutePath), Is.EquivalentTo(new[]
            {
                Path.Combine(assets, "Scene.unity"),
                Path.Combine(settings, "Tags.json"),
                Path.Combine(localPackage, "TagCode.cs")
            }));
        }

        [Test]
        public void Cancellation_marks_the_scan_incomplete_without_opening_more_files()
        {
            var first = WriteText("First.cs", "State.Killed");
            var second = WriteText("Second.cs", "State.Killed");
            var opens = 0;
            var scanner = new GameplayTagTextReferenceScanner(path =>
            {
                opens++;
                return File.OpenText(path);
            });

            var result = scanner.Search(
                new[]
                {
                    new GameplayTagReferenceFile(first, "Assets/First.cs"),
                    new GameplayTagReferenceFile(second, "Assets/Second.cs")
                },
                new[] { "State.Killed" },
                string.Empty,
                progress => progress.Fraction >= 0.5f);

            Assert.That(result.IsComplete, Is.False);
            Assert.That(result.IsCancelled, Is.True);
            Assert.That(opens, Is.EqualTo(1));
        }
    }
}
