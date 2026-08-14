#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    [TestFixture]
    public sealed class GameplayTagCatalogEditSessionTests
    {
        [Test]
        public void Add_comment_and_imported_rows_serialize_only_canonical_lowercase()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"ABILITY\",\"comment\":\"\"}]}");

            session.Add("Ability.Jump", "점프");
            session.SetComment("ABILITY", "능력");

            var json = session.Serialize();
            Assert.That(json, Does.Contain("\"name\": \"ability\""));
            Assert.That(json, Does.Contain("\"name\": \"ability.jump\""));
            Assert.That(json, Does.Not.Contain("Ability"));
            Assert.That(json, Does.Not.Contain("ABILITY"));
            Assert.That(json, Does.Contain("\"comment\": \"능력\""));
            Assert.That(json.EndsWith("\n", StringComparison.Ordinal), Is.True);
        }

        [Test]
        public void Commenting_an_implicit_game_parent_promotes_only_that_parent()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"Ability.Jump\",\"comment\":\"jump\"}]}");

            session.SetComment("ABILITY", "ability root");

            Assert.That(session.Tags.Select(row => row.Name),
                Is.EqualTo(new[] { "ability", "ability.jump" }));
            Assert.That(session.Tags.Single(row => row.Name == "ability").Comment,
                Is.EqualTo("ability root"));
            Assert.That(session.Tags.Single(row => row.Name == "ability.jump").Comment,
                Is.EqualTo("jump"));
        }

        [Test]
        public void Rename_subtree_canonicalizes_the_segment_and_redirects_every_game_active_path()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Movement.Run.Fast\",\"comment\":\"\"}],"
                + "\"redirects\":[{\"from\":\"Legacy.Run\",\"to\":\"State.Movement.Run\"}]}");

            var result = session.RenameSubtree("STATE.MOVEMENT.RUN", "Sprint");

            Assert.That(result.NewPath, Is.EqualTo("state.movement.sprint"));
            Assert.That(session.Serialize(), Does.Contain("state.movement.sprint.fast"));
            Assert.That(session.Serialize(), Does.Not.Contain("Sprint"));
            Assert.That(GetRedirectTarget(session, "state.movement.run"),
                Is.EqualTo("state.movement.sprint"));
            Assert.That(GetRedirectTarget(session, "state.movement.run.fast"),
                Is.EqualTo("state.movement.sprint.fast"));
            Assert.That(GetRedirectTarget(session, "legacy.run"),
                Is.EqualTo("state.movement.sprint"));
        }

        [Test]
        public void Game_rename_leaves_package_old_path_active_and_reports_shadowed_redirect()
        {
            var package = Package("bun3.gameplay", "ability.jump");
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"ability.jump\",\"comment\":\"game\"}]}",
                package);

            var result = session.RenameSubtree("ability.jump", "leap");

            Assert.That(result.NewPath, Is.EqualTo("ability.leap"));
            Assert.That(result.ShadowedOldPaths, Is.EqualTo(new[] { "ability.jump" }));
            Assert.That(session.Tags.Single().Name, Is.EqualTo("ability.leap"));
            Assert.That(package.Tags.Single().Name, Is.EqualTo("ability.jump"));
            Assert.That(session.LastCompilation!.Catalog!.TryGet("ability.jump", out _), Is.True);
            Assert.That(session.LastCompilation.Catalog.TryGet("ability.leap", out _), Is.True);
            Assert.That(GetRedirectTarget(session, "ability.jump"), Is.EqualTo("ability.leap"));
        }

        [Test]
        public void Rename_into_a_game_active_path_rejects_byte_for_byte()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"ability.jump\",\"comment\":\"\"},"
                + "{\"name\":\"ability.leap.child\",\"comment\":\"\"}]}");
            var before = session.Serialize();

            Assert.Throws<InvalidOperationException>(
                () => session.RenameSubtree("ability.jump", "leap"));

            Assert.That(session.Serialize(), Is.EqualTo(before));
        }

        [Test]
        public void Rename_into_a_path_active_only_in_another_source_succeeds_and_merges_at_runtime()
        {
            var package = Package("bun3.gameplay", "ability.leap");
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"ability.jump\",\"comment\":\"\"}]}",
                package);

            var result = session.RenameSubtree("ability.jump", "Leap");

            Assert.That(result.NewPath, Is.EqualTo("ability.leap"));
            Assert.That(result.ShadowedOldPaths, Is.Empty);
            Assert.That(session.Tags.Single().Name, Is.EqualTo("ability.leap"));
            Assert.That(session.LastCompilation!.Catalog!.Count, Is.EqualTo(2));
            Assert.That(session.LastCompilation.Catalog.GetRequired("ability.leap").Index,
                Is.EqualTo(session.LastCompilation.Catalog.GetRequired("ABILITY.LEAP").Index));
        }

        [Test]
        public void Renaming_an_implicit_game_parent_moves_descendants_and_redirects_both_active_paths()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"Ability.Jump\",\"comment\":\"jump\"}]}");

            var result = session.RenameSubtree("ABILITY", "Skill");

            Assert.That(result.NewPath, Is.EqualTo("skill"));
            Assert.That(session.Tags.Single().Name, Is.EqualTo("skill.jump"));
            Assert.That(GetRedirectTarget(session, "ability"), Is.EqualTo("skill"));
            Assert.That(GetRedirectTarget(session, "ability.jump"), Is.EqualTo("skill.jump"));
        }

        [TestCase("Other.Parent")]
        [TestCase("Bad_Name")]
        [TestCase("")]
        public void Rename_rejects_a_non_segment_and_preserves_the_document(string newSegment)
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"state.dead\",\"comment\":\"\"}]}");
            var before = session.Serialize();

            Assert.Throws<ArgumentException>(() => session.RenameSubtree("state.dead", newSegment));
            Assert.That(session.Serialize(), Is.EqualTo(before));
        }

        [Test]
        public void Case_only_rename_is_a_semantic_no_op_without_a_redirect()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\",\"comment\":\"\"}]}");
            var before = session.Serialize();

            var result = session.RenameSubtree("STATE.DEAD", "DEAD");

            Assert.That(result.NewPath, Is.EqualTo("state.dead"));
            Assert.That(result.ShadowedOldPaths, Is.Empty);
            Assert.That(session.Serialize(), Is.EqualTo(before));
            Assert.That(session.Redirects, Is.Empty);
        }

        [Test]
        public void Delete_exact_removes_only_one_game_explicit_row_and_preserves_descendants_and_package()
        {
            var package = Package("bun3.gameplay", "ability.package");
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"ability\",\"comment\":\"root\"},"
                + "{\"name\":\"ability.jump\",\"comment\":\"jump\"}]}",
                package);

            session.DeleteExact("ABILITY");

            Assert.That(session.Tags.Select(row => row.Name), Is.EqualTo(new[] { "ability.jump" }));
            Assert.That(package.Tags.Single().Name, Is.EqualTo("ability.package"));
            Assert.That(session.LastCompilation!.Catalog!.TryGet("ability", out _), Is.True);
            Assert.That(session.LastCompilation.Catalog.TryGet("ability.jump", out _), Is.True);
            Assert.That(session.LastCompilation.Catalog.TryGet("ability.package", out _), Is.True);
        }

        [Test]
        public void Delete_exact_rejects_an_implicit_only_game_node_byte_for_byte()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"ability.jump\",\"comment\":\"\"}]}");
            var before = session.Serialize();

            Assert.Throws<InvalidOperationException>(() => session.DeleteExact("ability"));

            Assert.That(session.Serialize(), Is.EqualTo(before));
        }

        [Test]
        public void Removing_redirects_matches_sources_case_insensitively_in_one_transaction()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\",\"comment\":\"\"}],"
                + "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"},"
                + "{\"from\":\"State.Gone\",\"to\":\"State.Dead\"}]}");

            var removed = session.RemoveRedirects(new[] { "state.killed", "STATE.GONE" });

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(session.Redirects, Is.Empty);
        }

        [Test]
        public void Removing_an_unknown_redirect_preserves_the_document()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"state.dead\",\"comment\":\"\"}],"
                + "\"redirects\":[{\"from\":\"state.killed\",\"to\":\"state.dead\"}]}");
            var before = session.Serialize();

            Assert.Throws<InvalidOperationException>(
                () => session.RemoveRedirects(new[] { "state.killed", "missing.old" }));
            Assert.That(session.Serialize(), Is.EqualTo(before));
        }

        [Test]
        public void Remove_redirects_tolerates_duplicates_and_rejects_null_sources()
        {
            var session = OpenGame(
                "{\"schemaVersion\":1,\"tags\":[{\"name\":\"state.dead\",\"comment\":\"\"}],"
                + "\"redirects\":[{\"from\":\"state.killed\",\"to\":\"state.dead\"}]}");
            var before = session.Serialize();

            Assert.That(session.RemoveRedirects(Array.Empty<string>()), Is.Zero);
            Assert.That(session.Serialize(), Is.EqualTo(before));
            Assert.Throws<ArgumentNullException>(() => session.RemoveRedirects(null!));
            Assert.Throws<ArgumentException>(() => session.RemoveRedirects(new string[] { null! }));
            Assert.That(session.Serialize(), Is.EqualTo(before));

            Assert.That(session.RemoveRedirects(new[] { "State.Killed", "state.killed" }), Is.EqualTo(1));
            Assert.That(session.Redirects, Is.Empty);
        }

        [Test]
        [Timeout(600_000)]
        public void Rename_subtree_at_maximum_catalog_count_includes_the_last_active_path()
        {
            var session = OpenGame(CreateMaximumCatalogJson());

            Assert.DoesNotThrow(() => session.RenameSubtree("STATE", "Condition"));

            Assert.That(session.Redirects.Count, Is.EqualTo(65_535));
            Assert.That(GetRedirectTarget(session, "state"), Is.EqualTo("condition"));
            Assert.That(GetRedirectTarget(session, "state.x65533"),
                Is.EqualTo("condition.x65533"));
        }

        private static GameplayTagCatalogEditSession OpenGame(
            string json,
            params TagSourceDocument[] readOnlySources)
        {
            var gameSource = LoadGame(json);
            return GameplayTagCatalogEditSession.Open(
                gameSource,
                candidate => Compile(candidate, readOnlySources));
        }

        private static TagSourceDocument LoadGame(string json)
        {
            using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(json));
            return TagSourceJson.LoadGame(stream, "ProjectSettings/GameplayTags.json");
        }

        private static TagSourceDocument Package(string sourceId, params string[] paths)
        {
            var tags = new TagSourceTag[paths.Length];
            for (var index = 0; index < tags.Length; index++)
            {
                tags[index] = new TagSourceTag(paths[index], "package");
            }

            return new TagSourceDocument(
                new TagSourceDescriptor(sourceId, "Package", TagSourceKind.PackageJson, true),
                sourceId + "/TagSource.json",
                tags,
                Array.Empty<TagSourceRedirect>());
        }

        private static TagCatalogCompilation Compile(
            TagSourceDocument candidate,
            IReadOnlyList<TagSourceDocument> readOnlySources)
        {
            var sources = new TagSourceDocument[readOnlySources.Count + 1];
            sources[0] = candidate;
            for (var index = 0; index < readOnlySources.Count; index++)
            {
                sources[index + 1] = readOnlySources[index];
            }

            return TagCatalogCompiler.Compile(
                sources,
                new TagCatalogIdentity("edit-session-test", "0.0.0-dev"));
        }

        private static string GetRedirectTarget(GameplayTagCatalogEditSession session, string from)
        {
            for (var index = 0; index < session.Redirects.Count; index++)
            {
                var redirect = session.Redirects[index];
                if (redirect.From == from) return redirect.To;
            }

            Assert.Fail($"Expected redirect source was not found: {from}");
            return string.Empty;
        }

        private static string CreateMaximumCatalogJson()
        {
            const int descendantCount = 65_534;
            var json = new StringBuilder(descendantCount * 24);
            json.Append("{\"schemaVersion\":1,\"tags\":[");
            for (var index = 0; index < descendantCount; index++)
            {
                if (index > 0) json.Append(',');
                json.Append("{\"name\":\"State.X");
                json.Append(index.ToString("D5", CultureInfo.InvariantCulture));
                json.Append("\",\"comment\":\"\"}");
            }

            json.Append("]}");
            return json.ToString();
        }
    }
}
