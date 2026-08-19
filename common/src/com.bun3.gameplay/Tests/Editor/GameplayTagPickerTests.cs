#nullable enable
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Linq;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>Verifies the merged GameplayTag picker projection and selection boundaries.</summary>
    [TestFixture]
    public sealed class GameplayTagPickerTests
    {
        /// <summary>Verifies identical declarations across sources become one runtime row with source details.</summary>
        [Test]
        public void Duplicate_source_declarations_produce_one_runtime_row_with_ordered_source_details()
        {
            var model = new GameplayTagPickerModel(CreateSnapshot(
                CreateSource(
                    "game", "Game", TagSourceKind.GameJson, false,
                    new TagSourceTag("ability.jump", "game jump")),
                CreateSource(
                    "bun3.gameplay", "Bun3.Gameplay", TagSourceKind.PackageJson, true,
                    new TagSourceTag("ability.jump", "framework jump"))));

            var row = model.Rows.Single(candidate => candidate.CanonicalPath == "ability.jump");
            var content = GameplayTagPickerTreeView.CreateNameContent(row, isCurrent: false, checkImage: null);

            Assert.That(model.Rows.Count(candidate => candidate.CanonicalPath == "ability.jump"), Is.EqualTo(1));
            Assert.That(row.SourceCount, Is.EqualTo(2));
            Assert.That(row.SourceDetails, Does.Contain("bun3.gameplay").And.Contain("framework jump"));
            Assert.That(row.SourceDetails, Does.Contain("game").And.Contain("game jump"));
            Assert.That(row.SourceDetails.IndexOf("bun3.gameplay", StringComparison.Ordinal),
                Is.LessThan(row.SourceDetails.IndexOf("\ngame (Game)", StringComparison.Ordinal)));
            Assert.That(content.tooltip,
                Does.StartWith("ability.jump\n").And.Contain(row.SourceDetails));
        }

        /// <summary>Verifies an explicit declaration with an empty comment and a truly implicit parent are distinguished by the provenance flag.</summary>
        [Test]
        public void Empty_comment_explicit_contribution_is_not_reported_as_implicit()
        {
            var model = new GameplayTagPickerModel(CreateSnapshot(
                CreateSource(
                    "game", "Game", TagSourceKind.GameJson, false,
                    new TagSourceTag("ability.jump", string.Empty)),
                CreateSource(
                    "bun3.gameplay", "Bun3.Gameplay", TagSourceKind.PackageJson, true,
                    new TagSourceTag("ability.jump", "framework jump"))));

            var explicitLeaf = model.Rows.Single(row => row.CanonicalPath == "ability.jump");
            var implicitParent = model.Rows.Single(row => row.CanonicalPath == "ability");

            Assert.That(explicitLeaf.SourceDetails,
                Does.Contain("game (Game): explicit (no comment)")
                    .And.Not.Contain("game (Game): implicit"));
            Assert.That(implicitParent.SourceDetails,
                Does.Contain("bun3.gameplay (Bun3.Gameplay): implicit")
                    .And.Contain("game (Game): implicit"));
        }

        /// <summary>Verifies search matches only canonical full paths case-insensitively and keeps ancestors.</summary>
        [Test]
        public void Filter_matches_the_canonical_path_case_insensitively_and_keeps_only_ancestors()
        {
            var model = new GameplayTagPickerModel(CreateSnapshot(CreateSource(
                "game", "Game", TagSourceKind.GameJson, false,
                new TagSourceTag("ability.movement.jump", "jump"),
                new TagSourceTag("state.dead", "JUMP only in a comment"))));

            var rows = model.Filter("JUMP");

            Assert.That(rows.Select(row => row.CanonicalPath), Is.EqualTo(new[]
            {
                "ability",
                "ability.movement",
                "ability.movement.jump"
            }));
            Assert.That(rows.Select(row => row.IsDirectMatch),
                Is.EqualTo(new[] { false, false, true }));
        }

        /// <summary>Verifies search expansion is temporary and both scroll axes and normal expansion are restored.</summary>
        [Test]
        public void Picker_filter_temporarily_expands_results_and_preserves_both_scroll_axes()
        {
            var state = new TreeViewState { scrollPos = new Vector2(31f, 59f) };
            var tree = new GameplayTagPickerTreeView(state);
            var model = new GameplayTagPickerModel(CreateSnapshot(CreateSource(
                "game", "Game", TagSourceKind.GameJson, false,
                new TagSourceTag("ability.jump", "jump"),
                new TagSourceTag("state.dead.ghost", "ghost"))));
            var ability = model.Rows.Single(row => row.CanonicalPath == "ability");
            var stateRow = model.Rows.Single(row => row.CanonicalPath == "state");
            var dead = model.Rows.Single(row => row.CanonicalPath == "state.dead");
            tree.SetRows(model.Rows, isFiltering: false);
            tree.SetExpanded(ability.Id, true);
            tree.SetExpanded(stateRow.Id, false);

            tree.SetRows(model.Filter("GHOST"), isFiltering: true);

            Assert.That(tree.IsExpanded(stateRow.Id), Is.True);
            Assert.That(tree.IsExpanded(dead.Id), Is.True);

            tree.SetRows(model.Rows, isFiltering: false);

            Assert.That(tree.IsExpanded(ability.Id), Is.True);
            Assert.That(tree.IsExpanded(stateRow.Id), Is.False);
            Assert.That(state.scrollPos, Is.EqualTo(new Vector2(31f, 59f)));
            Assert.That(tree.UsesScrollView, Is.True);
        }

        /// <summary>Verifies the shared renderer draws after the disclosure and puts the full canonical path in the tooltip.</summary>
        [Test]
        public void Picker_label_uses_the_shared_disclosure_geometry_and_keeps_the_full_path_in_tooltip()
        {
            var row = new GameplayTagPickerRow(
                id: 3,
                parentId: 2,
                canonicalPath: "ability.movement.jump",
                displaySegment: "jump",
                sourceCount: 1,
                sourceDetails: "game (Game): player jump",
                isDirectMatch: false);
            var rowRect = new Rect(12f, 8f, 240f, 18f);
            const float childBearingRowContentIndent = 46f;

            var selectedIcon = new Texture2D(1, 1);
            GUIContent selected;
            GUIContent ordinary;
            GUIContent fallback;
            try
            {
                selected = GameplayTagPickerTreeView.CreateNameContent(row, isCurrent: true, selectedIcon);
                ordinary = GameplayTagPickerTreeView.CreateNameContent(row, isCurrent: false, selectedIcon);
                fallback = GameplayTagPickerTreeView.CreateNameContent(row, isCurrent: true, checkImage: null);
                Assert.That(selected.text, Is.EqualTo("jump"));
                Assert.That(selected.image, Is.SameAs(selectedIcon));
                Assert.That(ordinary.image, Is.Null);
                Assert.That(fallback.text, Is.EqualTo("\u2713 jump"));
                Assert.That(GameplayTagPickerTreeView.CreateSourceContent(row).text, Is.EqualTo("1 source"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(selectedIcon);
            }
            var labelRect = GameplayTagTreeRowGeometry.CalculateLabelRect(
                rowRect,
                childBearingRowContentIndent);

            Assert.That(GameplayTagPickerTreeView.CreateNameContent(row, false, null).tooltip,
                Does.Contain("ability.movement.jump").And.Contain("game (Game): player jump"));
            Assert.That(labelRect.xMin,
                Is.EqualTo(rowRect.xMin + childBearingRowContentIndent));
            Assert.That(labelRect.xMax, Is.EqualTo(rowRect.xMax));
        }

        [Test]
        public void Picker_row_geometry_reserves_a_non_overlapping_source_column()
        {
            var rects = GameplayTagPickerRowGeometry.Calculate(
                new Rect(40f, 8f, 240f, 18f), sourceWidth: 56f, spacing: 8f);

            Assert.That(rects.SourceRect.xMax, Is.EqualTo(280f));
            Assert.That(rects.SourceRect.width, Is.EqualTo(56f));
            Assert.That(rects.NameRect.xMax + 8f, Is.EqualTo(rects.SourceRect.xMin));
            Assert.That(rects.NameRect.Overlaps(rects.SourceRect), Is.False);
        }

        [Test]
        public void Picker_row_geometry_clamps_source_column_when_row_is_narrower_than_source_and_spacing()
        {
            var rects = GameplayTagPickerRowGeometry.Calculate(
                new Rect(40f, 8f, 40f, 18f), sourceWidth: 56f, spacing: 8f);

            Assert.That(rects.SourceRect.xMax, Is.EqualTo(80f));
            Assert.That(rects.SourceRect.width, Is.EqualTo(40f));
            Assert.That(rects.NameRect.width, Is.EqualTo(0f));
            Assert.That(rects.NameRect.Overlaps(rects.SourceRect), Is.False);
        }

        [Test]
        public void Picker_current_path_state_survives_projection_replacement_and_invalid_paths_clear_current()
        {
            var model = new GameplayTagPickerModel(CreateSnapshot(CreateSource(
                "game", "Game", TagSourceKind.GameJson, false,
                new TagSourceTag("ability.jump", "jump"),
                new TagSourceTag("state.dead", "dead"))));
            var tree = new GameplayTagPickerTreeView(new TreeViewState());
            tree.SetRows(model.Rows, isFiltering: false);
            tree.SynchronizeSelection("ABILITY.JUMP");
            var filteredRows = model.Filter("JUMP");
            tree.SetRows(filteredRows, isFiltering: true);
            Assert.That(filteredRows.Count(row => tree.IsCurrent(row)), Is.EqualTo(1));
            Assert.That(filteredRows.Single(tree.IsCurrent).CanonicalPath,
                Is.EqualTo("ability.jump"));

            foreach (var path in new[] { string.Empty, "Legacy..Broken", "ability.missing" })
            {
                tree.SetCurrentPath(path);
                Assert.That(model.Rows.Any(tree.IsCurrent), Is.False);
            }
        }

        /// <summary>Verifies the selection callback returns only the canonical runtime path without source info.</summary>
        [Test]
        public void Selection_returns_only_the_canonical_runtime_path()
        {
            var snapshot = CreateSnapshot(
                CreateSource(
                    "game", "Game", TagSourceKind.GameJson, false,
                    new TagSourceTag("ability.jump", "game jump")),
                CreateSource(
                    "bun3.gameplay", "Bun3.Gameplay", TagSourceKind.PackageJson, true,
                    new TagSourceTag("ability.jump", "framework jump")));
            var selected = string.Empty;
            var window = ScriptableObject.CreateInstance<GameplayTagPickerWindow>();
            try
            {
                window.Initialize(snapshot, "state.raw-invalid", path => selected = path);
                var row = window.Model!.Rows.Single(candidate => candidate.CanonicalPath == "ability.jump");

                Assert.That(window.CurrentRawValue, Is.EqualTo("state.raw-invalid"));
                Assert.That(selected, Is.Empty);

                var applied = window.TrySelect(row.Id);

                Assert.That(applied, Is.True);
                Assert.That(selected, Is.EqualTo("ability.jump"));
                Assert.That(selected, Does.Not.Contain("game"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>Verifies an invalid workspace preserves the existing raw value and blocks new selection.</summary>
        [Test]
        public void Invalid_workspace_keeps_the_raw_current_value_visible_and_disables_new_selection()
        {
            var source = CreateSource(
                "game", "Game", TagSourceKind.GameJson, false,
                new TagSourceTag("ability.jump", "game jump"));
            var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
                "GameplayTags.json", Array.Empty<Type>(), Array.Empty<string>());
            var workspace = GameplayTagEditorWorkspace.Open(resolution, source);
            var callbackCount = 0;
            var window = ScriptableObject.CreateInstance<GameplayTagPickerWindow>();
            try
            {
                window.Initialize(workspace, "Legacy.MixedCase.Value", _ => callbackCount++);

                Assert.That(window.CanSelect, Is.False);
                Assert.That(window.CurrentRawValue, Is.EqualTo("Legacy.MixedCase.Value"));
                Assert.That(window.PersistentDiagnostics, Is.Not.Empty);
                Assert.That(window.TrySelect(1), Is.False);
                Assert.That(callbackCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        /// <summary>Verifies a live workspace turning invalid keeps the open picker's raw value and blocks only selection.</summary>
        [Test]
        public void Live_workspace_refresh_invalidates_an_open_picker_without_replacing_the_raw_value()
        {
            var valid = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "picker-live-game", "ability.jump");
            var invalid = GameplayTagEditorWorkspace.Open(
                new GameplayTagBuildContextResolution(
                    null,
                    new[] { "B3TAG3003: package Source became malformed" },
                    permitsGameOnlyValidation: false),
                valid.Snapshot!.Sources.Single(source => source.Descriptor.SourceId == "game"));
            var callbackCount = 0;
            var window = ScriptableObject.CreateInstance<GameplayTagPickerWindow>();
            try
            {
                window.Initialize(valid, "Legacy.Raw.Value", _ => callbackCount++);
                Assert.That(window.CanSelect, Is.True);

                window.RefreshWorkspace(invalid);

                Assert.That(window.CanSelect, Is.False);
                Assert.That(window.CurrentRawValue, Is.EqualTo("Legacy.Raw.Value"));
                Assert.That(window.PersistentDiagnostics.Single(), Does.Contain("B3TAG3003"));
                Assert.That(window.TrySelect(1), Is.False);
                Assert.That(callbackCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static GameplayTagWorkspaceSnapshot CreateSnapshot(params TagSourceDocument[] sources)
        {
            var compilation = TagCatalogCompiler.Compile(
                sources,
                new TagCatalogIdentity("picker-tests", "0.0.0-dev"));
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
#pragma warning restore CS0618
