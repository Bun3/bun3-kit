#nullable enable
using System;
using System.Linq;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>Verifies the per-source tag tree projection's row composition and search behavior.</summary>
    [TestFixture]
    public sealed class GameplayTagTreeModelTests
    {
        /// <summary>Verifies source roots and per-source duplicate tags display in deterministic preorder.</summary>
        [Test]
        public void Source_roots_and_duplicate_tags_use_deterministic_preorder_and_unique_editor_ids()
        {
            var model = new GameplayTagTreeModel(CreateSnapshot(
                CreateSource(
                    "game", "Game", TagSourceKind.GameJson, false,
                    new TagSourceTag("ability.jump", "game comment")),
                CreateSource(
                    "bun3.gameplay", "Bun3.Gameplay", TagSourceKind.PackageJson, true,
                    new TagSourceTag("ability.jump", "framework comment"))));

            Assert.That(model.Rows.Select(row => (row.SourceId, row.Path)), Is.EqualTo(new[]
            {
                ("bun3.gameplay", ""),
                ("bun3.gameplay", "ability"),
                ("bun3.gameplay", "ability.jump"),
                ("game", ""),
                ("game", "ability"),
                ("game", "ability.jump")
            }));
            Assert.That(model.Rows.Select(row => row.Id), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
            Assert.That(model.Rows[2].RuntimeIndex, Is.EqualTo(model.Rows[5].RuntimeIndex));
            Assert.That(model.Rows[2].Id, Is.Not.EqualTo(model.Rows[5].Id));
        }

        /// <summary>Verifies source roots and implicit/explicit rows preserve source ownership and permissions.</summary>
        [Test]
        public void Rows_preserve_source_comment_explicit_and_readonly_metadata()
        {
            var model = new GameplayTagTreeModel(CreateSnapshot(
                CreateSource(
                    "game", "Game", TagSourceKind.GameJson, false,
                    new TagSourceTag("ability.jump", "game comment")),
                CreateSource(
                    "bun3.gameplay", "Bun3.Gameplay", TagSourceKind.PackageJson, true,
                    new TagSourceTag("ability.jump", "framework comment"))));

            var packageRoot = model.Rows.Single(row => row.SourceId == "bun3.gameplay" && row.IsSourceRoot);
            Assert.That(packageRoot.Path, Is.Empty);
            Assert.That(packageRoot.DisplayName, Is.EqualTo("Bun3.Gameplay"));
            Assert.That(packageRoot.IsReadOnly, Is.True);

            var packageParent = model.Rows.Single(
                row => row.SourceId == "bun3.gameplay" && row.Path == "ability");
            Assert.That(packageParent.IsExplicit, Is.False);
            Assert.That(packageParent.Comment, Is.Empty);

            var packageLeaf = model.Rows.Single(
                row => row.SourceId == "bun3.gameplay" && row.Path == "ability.jump");
            Assert.That(packageLeaf.IsExplicit, Is.True);
            Assert.That(packageLeaf.Comment, Is.EqualTo("framework comment"));
            Assert.That(packageLeaf.IsReadOnly, Is.True);

            var gameLeaf = model.Rows.Single(row => row.SourceId == "game" && row.Path == "ability.jump");
            Assert.That(gameLeaf.Comment, Is.EqualTo("game comment"));
            Assert.That(gameLeaf.IsReadOnly, Is.False);
        }

        /// <summary>Verifies search matches only canonical full paths and keeps source root and ancestor context.</summary>
        [Test]
        public void Search_keeps_the_matching_source_root_and_ancestors_and_marks_only_direct_matches()
        {
            var model = new GameplayTagTreeModel(CreateSnapshot(
                CreateSource(
                    "game", "Game", TagSourceKind.GameJson, false,
                    new TagSourceTag("state.dead", "jump appears only in this comment")),
                CreateSource(
                    "bun3.gameplay", "Bun3.Gameplay", TagSourceKind.PackageJson, true,
                    new TagSourceTag("ability.movement.jump", "framework"))));

            var rows = model.Filter("JUMP");

            Assert.That(rows.Select(row => (row.SourceId, row.Path)), Is.EqualTo(new[]
            {
                ("bun3.gameplay", ""),
                ("bun3.gameplay", "ability"),
                ("bun3.gameplay", "ability.movement"),
                ("bun3.gameplay", "ability.movement.jump")
            }));
            Assert.That(rows.Select(row => row.IsDirectMatch),
                Is.EqualTo(new[] { false, false, false, true }));
        }

        /// <summary>Verifies a source without tags still remains as an authoring-unit root row.</summary>
        [Test]
        public void Empty_sources_still_render_non_tag_source_roots()
        {
            var model = new GameplayTagTreeModel(CreateSnapshot(
                CreateSource("game", "Game", TagSourceKind.GameJson, false),
                CreateSource("bun3.gameplay", "Bun3.Gameplay", TagSourceKind.Native, true)));

            Assert.That(model.Rows.Select(row => (row.SourceId, row.DisplayName, row.Path)),
                Is.EqualTo(new[]
                {
                    ("bun3.gameplay", "Bun3.Gameplay", ""),
                    ("game", "Game", "")
                }));
            Assert.That(model.Rows.All(row => row.IsSourceRoot && row.RuntimeIndex == 0), Is.True);
        }

        private static GameplayTagWorkspaceSnapshot CreateSnapshot(params TagSourceDocument[] sources)
        {
            var compilation = TagCatalogCompiler.Compile(
                sources,
                new TagCatalogIdentity("tree-tests", "0.0.0-dev"));
            Assert.That(compilation.Succeeded, Is.True,
                string.Join(Environment.NewLine, compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
            return new GameplayTagWorkspaceSnapshot(
                compilation.Catalog!, compilation.Provenance!, sources);
        }

        private static TagSourceDocument CreateSource(
            string sourceId,
            string displayName,
            TagSourceKind kind,
            bool isReadOnly,
            params TagSourceTag[] tags) =>
            new TagSourceDocument(
                new TagSourceDescriptor(sourceId, displayName, kind, isReadOnly),
                sourceId + ".json",
                tags,
                Array.Empty<TagSourceRedirect>());
    }
}
