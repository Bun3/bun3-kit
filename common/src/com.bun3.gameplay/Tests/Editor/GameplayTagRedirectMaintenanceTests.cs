#nullable enable
using System;
using System.Linq;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>Verifies the redirect cleanup policy trusts only complete search results.</summary>
    [TestFixture]
    internal sealed class GameplayTagRedirectMaintenanceTests
    {
        /// <summary>Verifies only redirects without project matches become bulk candidates.</summary>
        [Test]
        public void Bulk_cleanup_returns_only_sources_without_project_matches()
        {
            var redirects = new[]
            {
                new GameplayTagRedirectRowModel(
                    "game", "Game", "State.Killed", "State.Dead", false, false),
                new GameplayTagRedirectRowModel(
                    "game", "Game", "Ability.Old", "Ability.New", false, false),
                new GameplayTagRedirectRowModel(
                    "bun3.gameplay", "Bun3.Gameplay", "Framework.Old", "Ability.New", true, false)
            };
            var result = GameplayTagReferenceSearchResult.Complete(new[]
            {
                new GameplayTagReferenceMatch(
                    "State.Killed", "C:/Project/Assets/A.prefab", "Assets/A.prefab", 7, "State.Killed")
            });

            Assert.That(GameplayTagRedirectMaintenance.GetUnreferencedSources(redirects, result),
                Is.EqualTo(new[] { "Ability.Old" }));
        }

        /// <summary>Verifies the per-source redirect projection orders by source ID and old path and preserves shadow state.</summary>
        [Test]
        public void Redirect_rows_are_grouped_by_source_and_mark_active_old_names_as_shadowed()
        {
            var sources = new[]
            {
                new TagSourceDocument(
                    new TagSourceDescriptor("game", "Game", TagSourceKind.GameJson, false),
                    "GameplayTags.json",
                    Array.Empty<TagSourceTag>(),
                    new[] { new TagSourceRedirect("z.old", "active.tag") }),
                new TagSourceDocument(
                    new TagSourceDescriptor(
                        "bun3.gameplay", "Bun3.Gameplay", TagSourceKind.PackageJson, true),
                    "Bun3.Gameplay.tags.json",
                    new[]
                    {
                        new TagSourceTag("active.tag", ""),
                        new TagSourceTag("active.tag.child", "")
                    },
                    new[]
                    {
                        new TagSourceRedirect("active.tag", "active.tag.child"),
                        new TagSourceRedirect("a.old", "active.tag")
                    })
            };
            var compilation = TagCatalogCompiler.Compile(
                sources, new TagCatalogIdentity("redirect-tests", "0.0.0-dev"));
            Assert.That(compilation.Succeeded, Is.True);
            var snapshot = new GameplayTagWorkspaceSnapshot(
                compilation.Catalog!, compilation.Provenance!, sources);

            var rows = GameplayTagRedirectMaintenance.CreateRows(snapshot);

            Assert.That(rows.Select(row => (row.SourceId, row.From)), Is.EqualTo(new[]
            {
                ("bun3.gameplay", "a.old"),
                ("bun3.gameplay", "active.tag"),
                ("game", "z.old")
            }));
            Assert.That(rows[1].IsReadOnly, Is.True);
            Assert.That(rows[1].IsShadowed, Is.True);
            Assert.That(rows[0].IsShadowed, Is.False);
        }

        /// <summary>Verifies case-only-different matches are also excluded from candidates while redirect order is kept.</summary>
        [Test]
        public void Bulk_cleanup_ignores_case_and_preserves_redirect_order()
        {
            var redirects = new[]
            {
                new EditableRedirectRow("State.Killed", "State.Dead"),
                new EditableRedirectRow("Ability.Old", "Ability.New"),
                new EditableRedirectRow("Item.Old", "Item.New")
            };
            var result = GameplayTagReferenceSearchResult.Complete(new[]
            {
                new GameplayTagReferenceMatch(
                    "ability.old", "C:/Project/Assets/A.prefab", "Assets/A.prefab", 3, "ability.old")
            });

            Assert.That(GameplayTagRedirectMaintenance.GetUnreferencedSources(redirects, result),
                Is.EqualTo(new[] { "State.Killed", "Item.Old" }));
        }

        /// <summary>Verifies an incomplete search produces no cleanup candidates.</summary>
        [Test]
        public void Incomplete_scan_cannot_produce_cleanup_candidates()
        {
            var redirects = new[] { new EditableRedirectRow("State.Killed", "State.Dead") };
            var incomplete = GameplayTagReferenceSearchResult.Incomplete(
                cancelled: true, errors: Array.Empty<string>());

            Assert.Throws<InvalidOperationException>(
                () => GameplayTagRedirectMaintenance.GetUnreferencedSources(redirects, incomplete));
        }

        /// <summary>Verifies referenced-redirect dialog results convert to explicit decisions.</summary>
        /// <param name="result">Button index returned by DisplayDialogComplex.</param>
        /// <param name="expected">Expected decision.</param>
        [TestCase(0, ReferencedRedirectDecision.OpenReferences)]
        [TestCase(1, ReferencedRedirectDecision.Cancel)]
        [TestCase(2, ReferencedRedirectDecision.RemoveAnyway)]
        public void Referenced_redirect_dialog_result_maps_to_an_explicit_decision(
            int result, ReferencedRedirectDecision expected)
        {
            Assert.That(GameplayTagRedirectMaintenance.MapReferencedDialogResult(result), Is.EqualTo(expected));
        }

        /// <summary>Verifies an undefined dialog result is rejected.</summary>
        [Test]
        public void Unknown_dialog_result_is_rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => GameplayTagRedirectMaintenance.MapReferencedDialogResult(3));
        }

        /// <summary>Verifies the cleanup warning always mentions uninspected external data.</summary>
        [Test]
        public void Cleanup_warning_always_names_the_unscanned_external_data()
        {
            Assert.That(GameplayTagRedirectMaintenance.NoProjectReferencesWarning,
                Does.StartWith("No project references were found."));
            Assert.That(GameplayTagRedirectMaintenance.ExternalDataScopeWarning,
                Does.Contain("Save data").And.Contain("server configuration")
                    .And.Contain("external files").And.Contain("deployed builds"));
        }
    }
}
