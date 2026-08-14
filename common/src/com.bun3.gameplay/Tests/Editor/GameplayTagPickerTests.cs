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
    /// <summary>병합 GameplayTag Picker projection과 선택 경계를 검증합니다.</summary>
    [TestFixture]
    public sealed class GameplayTagPickerTests
    {
        /// <summary>여러 Source의 같은 선언이 Source 상세를 가진 단일 런타임 행이 되는지 검증합니다.</summary>
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
            var content = GameplayTagPickerTreeView.CreateLabelContent(row);

            Assert.That(model.Rows.Count(candidate => candidate.CanonicalPath == "ability.jump"), Is.EqualTo(1));
            Assert.That(row.SourceCount, Is.EqualTo(2));
            Assert.That(row.SourceDetails, Does.Contain("bun3.gameplay").And.Contain("framework jump"));
            Assert.That(row.SourceDetails, Does.Contain("game").And.Contain("game jump"));
            Assert.That(row.SourceDetails.IndexOf("bun3.gameplay", StringComparison.Ordinal),
                Is.LessThan(row.SourceDetails.IndexOf("\ngame (Game)", StringComparison.Ordinal)));
            Assert.That(content.tooltip,
                Does.StartWith("ability.jump\n").And.Contain(row.SourceDetails));
        }

        /// <summary>빈 comment의 명시 선언과 실제 implicit 부모를 provenance flag로 구분하는지 검증합니다.</summary>
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

        /// <summary>검색이 canonical 전체 경로만 대소문자와 무관하게 찾고 조상을 유지하는지 검증합니다.</summary>
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

        /// <summary>검색 확장이 임시이며 양축 scroll 위치와 일반 확장이 복원되는지 검증합니다.</summary>
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

        /// <summary>공유 renderer가 disclosure 뒤에서 그리고 전체 canonical 경로를 tooltip에 두는지 검증합니다.</summary>
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

            var content = GameplayTagPickerTreeView.CreateLabelContent(row);
            var labelRect = GameplayTagTreeRowGeometry.CalculateLabelRect(
                rowRect,
                childBearingRowContentIndent);

            Assert.That(content.text, Is.EqualTo("jump  1 source"));
            Assert.That(content.tooltip,
                Does.Contain("ability.movement.jump").And.Contain("game (Game): player jump"));
            Assert.That(labelRect.xMin,
                Is.EqualTo(rowRect.xMin + childBearingRowContentIndent));
            Assert.That(labelRect.xMax, Is.EqualTo(rowRect.xMax));
        }

        /// <summary>선택 callback이 Source 정보 없이 canonical 런타임 경로만 반환하는지 검증합니다.</summary>
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

        /// <summary>잘못된 Workspace에서 기존 raw 값을 보존하고 신규 선택을 막는지 검증합니다.</summary>
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

        /// <summary>live Workspace가 무효해지면 열린 Picker의 raw 값은 유지하고 선택만 차단함을 검증합니다.</summary>
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
