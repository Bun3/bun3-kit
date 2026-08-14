#nullable enable
using System;
using System.IO;
using System.Reflection;
using Bun3.Gameplay.Editor.Tags;
using Bun3.Gameplay.Tags;
using NUnit.Framework;
using UnityEditor;

namespace Bun3.Gameplay.Unity.Tests
{
    /// <summary>Play 진입 전 Catalog 준비와 세션 수명주기를 검증합니다.</summary>
    [TestFixture]
    public sealed class GameplayTagPlayModeGateTests
    {
        private string _temporaryDirectory = null!;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "bun3-tag-play-gate-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
            GameplayTagPlaySessionCatalog.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagPlaySessionCatalog.Clear();
            if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, true);
        }

        [Test]
        public void Missing_provider_context_blocks_prepare_and_never_freezes_preview_or_last_good_binary()
        {
            var valid = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-invalid-game", "state.ready");
            _ = GameplayTagDevelopmentCatalogBuilder.Build(valid, _temporaryDirectory);
            var invalid = GameplayTagEditorWorkspace.Open(
                new GameplayTagBuildContextResolution(
                    null,
                    new[] { "B3TAG3001: provider missing" },
                    permitsGameOnlyValidation: true),
                valid.Snapshot!.Sources[0]);

            var prepared = GameplayTagPlayModeGate.TryPrepare(
                invalid,
                out var catalog,
                out var diagnostic,
                workspace => GameplayTagDevelopmentCatalogBuilder.Build(
                    workspace, _temporaryDirectory));

            Assert.That(prepared, Is.False);
            Assert.That(catalog, Is.Null);
            Assert.That(diagnostic, Does.Contain("B3TAG3001"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
        }

        [Test]
        public void Missing_game_source_and_malformed_resolved_source_each_block_prepare_with_their_path_diagnostic()
        {
            var missingPath = Path.Combine(_temporaryDirectory, "ProjectSettings", "GameplayTags.json");
            var missing = GameplayTagEditorWorkspace.Open(
                new GameplayTagBuildContextResolution(
                    null,
                    Array.Empty<string>(),
                    permitsGameOnlyValidation: true),
                missingPath);
            var valid = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-malformed-game", "state.ready");
            var malformed = GameplayTagEditorWorkspace.Open(
                new GameplayTagBuildContextResolution(
                    null,
                    new[] { "B3TAG3003: Failed to load gameplay tag source: package.json" },
                    permitsGameOnlyValidation: false),
                valid.Snapshot!.Sources[0]);

            Assert.That(GameplayTagPlayModeGate.TryPrepare(
                missing, out _, out var missingDiagnostic), Is.False);
            Assert.That(missingDiagnostic,
                Does.Contain("B3TAG3101").And.Contain(missingPath));
            Assert.That(GameplayTagPlayModeGate.TryPrepare(
                malformed, out _, out var malformedDiagnostic), Is.False);
            Assert.That(malformedDiagnostic,
                Does.Contain("B3TAG3003").And.Contain("package.json"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
        }

        [Test]
        public void Successful_prepare_round_trips_binary_and_freezes_the_same_instance_until_edit_mode_returns()
        {
            var workspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-success-game", "state.ready");

            var prepared = GameplayTagPlayModeGate.TryPrepare(
                workspace,
                out var catalog,
                out var diagnostic,
                value => GameplayTagDevelopmentCatalogBuilder.Build(
                    value, _temporaryDirectory));

            Assert.That(prepared, Is.True, diagnostic);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog, Is.Not.SameAs(workspace.Snapshot!.Catalog));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.SameAs(catalog));

            File.WriteAllBytes(
                TagCatalogDevelopmentPath.Get("play-success-game", _temporaryDirectory),
                new byte[] { 0, 1, 2, 3 });
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.SameAs(catalog));

            GameplayTagPlayModeGate.HandlePlayModeStateChanged(
                PlayModeStateChange.EnteredEditMode,
                () => throw new AssertionException("EditMode cleanup must not reload Sources."),
                _ => throw new AssertionException("EditMode cleanup must not show a popup."),
                () => throw new AssertionException("EditMode cleanup must not cancel Play."));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
        }

        [Test]
        public void Exiting_edit_mode_failure_cancels_the_transition_and_shows_exactly_one_diagnostic_popup()
        {
            var valid = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-cancel-game", "state.ready");
            var invalid = GameplayTagEditorWorkspace.Open(
                new GameplayTagBuildContextResolution(
                    null,
                    new[] { "B3TAG3001: provider missing" },
                    permitsGameOnlyValidation: true),
                valid.Snapshot!.Sources[0]);
            var popupCount = 0;
            var cancelCount = 0;
            var popupDiagnostic = string.Empty;

            GameplayTagPlayModeGate.HandlePlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                () => invalid,
                diagnostic =>
                {
                    popupCount++;
                    popupDiagnostic = diagnostic;
                },
                () => cancelCount++,
                workspace => GameplayTagDevelopmentCatalogBuilder.Build(
                    workspace, _temporaryDirectory));

            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(popupCount, Is.EqualTo(1));
            Assert.That(popupDiagnostic, Does.Contain("B3TAG3001"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
        }

        [Test]
        public void Initialization_and_cleanup_are_idempotent()
        {
            GameplayTagPlayModeGate.Initialize();
            GameplayTagPlayModeGate.Initialize();

            Assert.That(GameplayTagPlayModeGate.IsRegistered, Is.True);

            GameplayTagPlayModeGate.HandlePlayModeStateChanged(
                PlayModeStateChange.EnteredEditMode,
                () => throw new AssertionException("cleanup must not resolve"),
                _ => throw new AssertionException("cleanup must not popup"),
                () => throw new AssertionException("cleanup must not cancel"));
            GameplayTagPlayModeGate.HandlePlayModeStateChanged(
                PlayModeStateChange.EnteredEditMode,
                () => throw new AssertionException("cleanup must remain idempotent"),
                _ => throw new AssertionException("cleanup must remain silent"),
                () => throw new AssertionException("cleanup must not cancel"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
        }

        [Test]
        public void Domain_reload_restores_only_the_exact_prepared_binary_once_and_never_hot_reloads_it()
        {
            var workspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-domain-game", "state.ready");
            var catalog = GameplayTagDevelopmentCatalogBuilder.Build(
                workspace, _temporaryDirectory);
            var path = TagCatalogDevelopmentPath.Get("play-domain-game", _temporaryDirectory);
            GameplayTagPlaySessionCatalog.RememberPrepared(catalog, path);
            ClearManagedCurrentOnly();

            var restored = GameplayTagPlaySessionCatalog.TryRestorePrepared(out var diagnostic);

            Assert.That(restored, Is.True, diagnostic);
            var frozen = GameplayTagPlaySessionCatalog.Current;
            Assert.That(frozen, Is.Not.Null);
            Assert.That(frozen, Is.Not.SameAs(catalog));
            File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3 });
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.SameAs(frozen));

            ClearManagedCurrentOnly();
            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out diagnostic), Is.False);
            Assert.That(diagnostic, Is.Not.Empty);
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
        }

        [Test]
        public void Active_play_session_catalog_cannot_be_replaced_by_a_second_prepare()
        {
            var firstWorkspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-frozen-game", "state.first");
            var secondWorkspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-frozen-game", "state.second");
            Assert.That(GameplayTagPlayModeGate.TryPrepare(
                firstWorkspace,
                out var first,
                out var firstDiagnostic,
                workspace => GameplayTagDevelopmentCatalogBuilder.Build(
                    workspace, _temporaryDirectory)), Is.True, firstDiagnostic);

            var preparedAgain = GameplayTagPlayModeGate.TryPrepare(
                secondWorkspace,
                out var second,
                out var secondDiagnostic,
                workspace => GameplayTagDevelopmentCatalogBuilder.Build(
                    workspace, _temporaryDirectory));

            Assert.That(preparedAgain, Is.False);
            Assert.That(second, Is.Null);
            Assert.That(secondDiagnostic, Does.Contain("already frozen"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.SameAs(first));
            Assert.That(GameplayTagPlaySessionCatalog.Current!.TryGet("state.first", out _), Is.True);
            Assert.That(GameplayTagPlaySessionCatalog.Current.TryGet("state.second", out _), Is.False);
        }

        private static void ClearManagedCurrentOnly()
        {
            var property = typeof(GameplayTagPlaySessionCatalog).GetProperty(
                nameof(GameplayTagPlaySessionCatalog.Current),
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Play session Current property is missing.");
            property.SetValue(null, null);
        }
    }
}
