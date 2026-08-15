#nullable enable
using System;
using System.Linq;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>GameplayTagRef Inspector의 SerializedProperty와 Workspace 동작을 검증합니다.</summary>
    [TestFixture]
    public sealed class GameplayTagRefDrawerTests
    {
        /// <summary>테스트마다 Unity Undo 기록을 격리합니다.</summary>
        [SetUp]
        public void SetUp() => Undo.ClearAll();

        /// <summary>테스트 후 Unity Undo 기록을 제거합니다.</summary>
        [TearDown]
        public void TearDown() => Undo.ClearAll();

        /// <summary>하나의 선택이 모든 선택 대상에 적용되고 한 번의 Undo로 복원되는지 검증합니다.</summary>
        [Test]
        public void Apply_path_changes_all_targets_and_one_undo_restores_them()
        {
            var first = ScriptableObject.CreateInstance<TagRefHost>();
            var second = ScriptableObject.CreateInstance<TagRefHost>();
            try
            {
                var applied = GameplayTagRefDrawer.ApplyPath(
                    new UnityEngine.Object[] { first, second },
                    "_tag",
                    "Ability.Attack");

                Assert.That(applied, Is.True);
                Assert.That(first.Tag.Path, Is.EqualTo("ability.attack"));
                Assert.That(second.Tag.Path, Is.EqualTo("ability.attack"));

                Undo.PerformUndo();

                Assert.That(first.Tag.IsEmpty, Is.True);
                Assert.That(second.Tag.IsEmpty, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        /// <summary>None clear가 모든 선택 대상의 직렬화 경로를 비우는지 검증합니다.</summary>
        [Test]
        public void Clear_path_writes_none_to_all_targets()
        {
            var first = CreateHost("ability.attack");
            var second = CreateHost("ability.defend");
            try
            {
                var applied = GameplayTagRefDrawer.ApplyPath(
                    new UnityEngine.Object[] { first, second },
                    "_tag",
                    string.Empty);

                Assert.That(applied, Is.True);
                Assert.That(first.Tag.IsEmpty, Is.True);
                Assert.That(second.Tag.IsEmpty, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        /// <summary>서로 다른 다중 선택 값이 mixed 상태와 빈 Picker 초기값으로 투영되는지 검증합니다.</summary>
        [Test]
        public void Mixed_values_are_reported_without_choosing_one_target_as_the_initial_value()
        {
            var first = CreateHost("ability.attack");
            var second = CreateHost("ability.defend");
            try
            {
                var serialized = new SerializedObject(new UnityEngine.Object[] { first, second });
                var property = serialized.FindProperty("_tag");

                var initial = GameplayTagRefDrawer.GetInitialPickerPath(property, out var isMixed);

                Assert.That(isMixed, Is.True);
                Assert.That(initial, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        /// <summary>잘못됐거나 사라진 raw 경로를 변경하지 않고 warning 상태로 설명하는지 검증합니다.</summary>
        [Test]
        public void Invalid_and_missing_raw_paths_remain_visible_with_warnings()
        {
            var workspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "tag-ref-drawer", "ability.attack");

            var malformed = GameplayTagRefFieldState.Describe("Legacy..Broken", isMixed: false, workspace);
            var missing = GameplayTagRefFieldState.Describe("Legacy.Missing", isMixed: false, workspace);
            var valid = GameplayTagRefFieldState.Describe("ABILITY.ATTACK", isMixed: false, workspace);

            Assert.That(malformed.DisplayText, Is.EqualTo("Legacy..Broken"));
            Assert.That(malformed.HasWarning, Is.True);
            Assert.That(malformed.Tooltip, Does.Contain("문법"));
            Assert.That(missing.DisplayText, Is.EqualTo("Legacy.Missing"));
            Assert.That(missing.HasWarning, Is.True);
            Assert.That(missing.Tooltip, Does.Contain("없"));
            Assert.That(valid.DisplayText, Is.EqualTo("ABILITY.ATTACK"));
            Assert.That(valid.HasWarning, Is.False);
        }

        /// <summary>잘못된 Workspace에서 raw 값은 유지하고 선택 가능 상태만 차단하는지 검증합니다.</summary>
        [Test]
        public void Invalid_workspace_keeps_raw_text_and_reports_a_warning()
        {
            var valid = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "tag-ref-invalid-workspace", "ability.attack");
            var game = valid.Snapshot!.Sources.Single(source => source.Descriptor.SourceId == "game");
            var invalid = GameplayTagEditorWorkspace.Open(
                new GameplayTagBuildContextResolution(
                    null,
                    new[] { "B3TAG3003: package Source is malformed" },
                    permitsGameOnlyValidation: false),
                game);

            var state = GameplayTagRefFieldState.Describe("Legacy.Raw.Value", isMixed: false, invalid);

            Assert.That(state.DisplayText, Is.EqualTo("Legacy.Raw.Value"));
            Assert.That(state.HasWarning, Is.True);
            Assert.That(state.CanSelect, Is.False);
            Assert.That(state.Tooltip, Does.Contain("B3TAG3003"));
        }

        /// <summary>cache 만료 후 현재 invalid Workspace가 이전 정상 snapshot을 대체하는지 검증합니다.</summary>
        [Test]
        public void Workspace_cache_never_substitutes_a_last_good_snapshot_after_refresh()
        {
            var valid = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "tag-ref-cache", "ability.attack");
            var game = valid.Snapshot!.Sources.Single(source => source.Descriptor.SourceId == "game");
            var invalid = GameplayTagEditorWorkspace.Open(
                new GameplayTagBuildContextResolution(
                    null,
                    new[] { "B3TAG3003: provider became invalid" },
                    permitsGameOnlyValidation: false),
                game);
            var now = 10d;
            var openCount = 0;
            var current = valid;
            var cache = new GameplayTagRefWorkspaceCache(
                () =>
                {
                    openCount++;
                    return current;
                },
                () => now,
                0.75d);

            Assert.That(cache.Open(), Is.SameAs(valid));
            current = invalid;
            now = 10.5d;
            Assert.That(cache.Open(), Is.SameAs(valid));
            Assert.That(openCount, Is.EqualTo(1));

            now = 10.76d;
            var refreshed = cache.Open();

            Assert.That(refreshed, Is.SameAs(invalid));
            Assert.That(refreshed.CanBuildCatalog, Is.False);
            Assert.That(openCount, Is.EqualTo(2));
        }

        private static TagRefHost CreateHost(string path)
        {
            var host = ScriptableObject.CreateInstance<TagRefHost>();
            var serialized = new SerializedObject(host);
            serialized.FindProperty("_tag").FindPropertyRelative("_path").stringValue = path;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return host;
        }

        private sealed class TagRefHost : ScriptableObject
        {
            [SerializeField]
            private GameplayTagRef _tag = default;

            internal GameplayTagRef Tag => _tag;
        }
    }
}
