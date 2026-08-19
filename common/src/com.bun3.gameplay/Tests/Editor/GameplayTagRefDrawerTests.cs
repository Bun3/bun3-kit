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
    /// <summary>Verifies the GameplayTagRef inspector's SerializedProperty and workspace behavior.</summary>
    [TestFixture]
    public sealed class GameplayTagRefDrawerTests
    {
        /// <summary>Isolates the Unity Undo history per test.</summary>
        [SetUp]
        public void SetUp() => Undo.ClearAll();

        /// <summary>Clears the Unity Undo history after the test.</summary>
        [TearDown]
        public void TearDown() => Undo.ClearAll();

        /// <summary>Verifies one selection applies to all selected targets and one Undo restores them.</summary>
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

        /// <summary>Verifies clearing to None empties the serialized path on all selected targets.</summary>
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

        /// <summary>Verifies differing multi-selection values project as mixed state with an empty picker initial value.</summary>
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

        /// <summary>Verifies malformed or missing raw paths are described as warnings without being modified.</summary>
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
            Assert.That(malformed.Tooltip, Does.Contain("syntax"));
            Assert.That(missing.DisplayText, Is.EqualTo("Legacy.Missing"));
            Assert.That(missing.HasWarning, Is.True);
            Assert.That(missing.Tooltip, Does.Contain("missing"));
            Assert.That(valid.DisplayText, Is.EqualTo("ABILITY.ATTACK"));
            Assert.That(valid.HasWarning, Is.False);
        }

        /// <summary>Verifies an invalid workspace keeps the raw value and blocks only selectability.</summary>
        [Test]
        public void Invalid_workspace_keeps_raw_text_and_reports_a_warning()
        {
            var invalid = CreateInvalidWorkspace();

            var state = GameplayTagRefFieldState.Describe("Legacy.Raw.Value", isMixed: false, invalid);

            Assert.That(state.DisplayText, Is.EqualTo("Legacy.Raw.Value"));
            Assert.That(state.HasWarning, Is.True);
            Assert.That(state.CanSelect, Is.False);
            Assert.That(state.Tooltip, Does.Contain("B3TAG3003"));
        }

        /// <summary>Verifies an invalid workspace neither creates a None warning nor hides the raw syntax warning.</summary>
        [Test]
        public void Invalid_workspace_preserves_none_and_malformed_raw_states()
        {
            var invalid = CreateInvalidWorkspace();

            var none = GameplayTagRefFieldState.Describe(string.Empty, isMixed: false, invalid);
            var malformed = GameplayTagRefFieldState.Describe("Legacy..Broken", isMixed: false, invalid);

            Assert.That(none.DisplayText, Is.EqualTo("None"));
            Assert.That(none.HasWarning, Is.False);
            Assert.That(none.CanSelect, Is.False);
            Assert.That(none.Tooltip, Does.Contain("referenced"));
            Assert.That(malformed.DisplayText, Is.EqualTo("Legacy..Broken"));
            Assert.That(malformed.HasWarning, Is.True);
            Assert.That(malformed.CanSelect, Is.False);
            Assert.That(malformed.Tooltip, Does.Contain("syntax"));
        }

        /// <summary>Verifies the currently invalid workspace replaces the previous good snapshot after cache expiry.</summary>
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

        private static GameplayTagEditorWorkspace CreateInvalidWorkspace()
        {
            var valid = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "tag-ref-invalid-workspace", "ability.attack");
            var game = valid.Snapshot!.Sources.Single(source => source.Descriptor.SourceId == "game");
            return GameplayTagEditorWorkspace.Open(
                new GameplayTagBuildContextResolution(
                    null,
                    new[] { "B3TAG3003: package Source is malformed" },
                    permitsGameOnlyValidation: false),
                game);
        }

        private sealed class TagRefHost : ScriptableObject
        {
            [SerializeField]
            private GameplayTagRef _tag = default;

            internal GameplayTagRef Tag => _tag;
        }
    }
}
