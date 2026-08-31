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
    /// <summary>Verifies catalog preparation before play mode and the session lifecycle.</summary>
    [TestFixture]
    public sealed class GameplayTagPlayModeGateTests
    {
        private string _temporaryDirectory = null!;

        /// <summary>Initializes each test's temp catalog path and play session state.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "bun3-tag-play-gate-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryDirectory);
            GameplayTagPlaySessionCatalog.Clear();
        }

        /// <summary>Cleans up the play session state and temp files created by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            GameplayTagPlaySessionCatalog.Clear();
            if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, true);
        }

        /// <summary>Verifies a wrong-length fingerprint marker is cleaned up fail-closed without throwing.</summary>
        [Test]
        public void Wrong_length_fingerprint_marker_fails_closed_and_is_forgotten()
        {
            var workspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-marker-fingerprint", "state.ready");
            var catalog = GameplayTagDevelopmentCatalogBuilder.Build(workspace, _temporaryDirectory);
            var path = TagCatalogDevelopmentPath.Get(catalog.CatalogId, _temporaryDirectory);
            SetRawPreparedMarker(
                path,
                catalog.CatalogId,
                Convert.ToBase64String(new byte[31]));

            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out var diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("could not be restored"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out _), Is.False);
        }

        /// <summary>Verifies a correct-length fingerprint marker differing from the prepared catalog is rejected.</summary>
        [Test]
        public void Wrong_prepared_fingerprint_fails_closed_and_is_forgotten()
        {
            var workspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-marker-mismatch", "state.ready");
            var catalog = GameplayTagDevelopmentCatalogBuilder.Build(workspace, _temporaryDirectory);
            var path = TagCatalogDevelopmentPath.Get(catalog.CatalogId, _temporaryDirectory);
            var wrongFingerprint = catalog.Fingerprint.ToArray();
            wrongFingerprint[0] ^= 0xff;
            SetRawPreparedMarker(
                path,
                catalog.CatalogId,
                Convert.ToBase64String(wrongFingerprint));

            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out var diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("could not be restored"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out _), Is.False);
        }

        /// <summary>Rejects, fail-closed, an empty or identity-rule-violating catalog ID marker.</summary>
        [TestCase(" ")]
        [TestCase("Invalid Catalog")]
        public void Invalid_catalog_id_marker_fails_closed_and_is_forgotten(string catalogId)
        {
            var workspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-marker-id", "state.ready");
            var catalog = GameplayTagDevelopmentCatalogBuilder.Build(workspace, _temporaryDirectory);
            var path = TagCatalogDevelopmentPath.Get(catalog.CatalogId, _temporaryDirectory);
            SetRawPreparedMarker(
                path,
                catalogId,
                Convert.ToBase64String(catalog.Fingerprint.ToArray()));

            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out var diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("could not be restored"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out _), Is.False);
        }

        /// <summary>Rejects, fail-closed, a binary path marker that is empty or not a complete existing file.</summary>
        [TestCase(" ")]
        [TestCase("missing/GameplayTags.catalog")]
        public void Invalid_binary_path_marker_fails_closed_and_is_forgotten(string path)
        {
            var workspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-marker-path", "state.ready");
            var catalog = GameplayTagDevelopmentCatalogBuilder.Build(workspace, _temporaryDirectory);
            SetRawPreparedMarker(
                path,
                catalog.CatalogId,
                Convert.ToBase64String(catalog.Fingerprint.ToArray()));

            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out var diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("could not be restored"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out _), Is.False);
        }

        /// <summary>Rejects and cleans up, fail-closed, a complete binary path marker that does not exist.</summary>
        [Test]
        public void Missing_absolute_binary_path_marker_fails_closed_and_is_forgotten()
        {
            var workspace = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-marker-missing-path", "state.ready");
            var catalog = GameplayTagDevelopmentCatalogBuilder.Build(workspace, _temporaryDirectory);
            var missingPath = Path.Combine(_temporaryDirectory, "missing", "GameplayTags.catalog");
            SetRawPreparedMarker(
                missingPath,
                catalog.CatalogId,
                Convert.ToBase64String(catalog.Fingerprint.ToArray()));

            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out var diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("could not be restored"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
            Assert.That(GameplayTagPlaySessionCatalog.TryRestorePrepared(out _), Is.False);
        }

        /// <summary>Verifies a failed EnteredPlayMode restore from a bad marker cancels the transition and warns exactly once each.</summary>
        [Test]
        public void Entered_play_mode_malformed_marker_cancels_once_and_warns_once()
        {
            SetRawPreparedMarker(" ", " ", "not-base64");
            var cancelCount = 0;
            var warningCount = 0;

            Assert.That(() => GameplayTagPlayModeGate.HandlePlayModeStateChanged(
                PlayModeStateChange.EnteredPlayMode,
                () => throw new AssertionException("EnteredPlayMode restore must not resolve Sources."),
                _ => warningCount++,
                () => cancelCount++,
                build: null,
                isBatchMode: false), Throws.Nothing);

            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(warningCount, Is.EqualTo(1));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
        }

        /// <summary>Blocks play preparation without pinning the preview or last-good binary when there is no provider context.</summary>
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

        /// <summary>Verifies a missing game source and an invalid resolved source block play preparation with path diagnostics.</summary>
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

        /// <summary>Verifies a successful preparation pins the binary round-trip instance until returning to edit mode.</summary>
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

        /// <summary>Verifies an ExitingEditMode preparation failure triggers the play transition cancel and warning exactly once each.</summary>
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
                    workspace, _temporaryDirectory),
                isBatchMode: false);

            Assert.That(cancelCount, Is.EqualTo(1));
            Assert.That(popupCount, Is.EqualTo(1));
            Assert.That(popupDiagnostic, Does.Contain("B3TAG3001"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
        }

        /// <summary>Verifies an ExitingEditMode preparation failure in batch mode warns but lets play proceed.</summary>
        [Test]
        public void Exiting_edit_mode_failure_in_batch_mode_does_not_cancel_but_still_warns()
        {
            var valid = GameplayTagDevelopmentCatalogTests.CreateValidWorkspace(
                "play-batch-game", "state.ready");
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
                    workspace, _temporaryDirectory),
                isBatchMode: true);

            Assert.That(cancelCount, Is.EqualTo(0));
            Assert.That(popupCount, Is.EqualTo(1));
            Assert.That(popupDiagnostic, Does.Contain("B3TAG3001"));
            Assert.That(GameplayTagPlaySessionCatalog.Current, Is.Null);
        }

        /// <summary>Verifies play gate registration and edit mode cleanup are idempotent.</summary>
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

        /// <summary>Verifies the prepared binary is restored exactly once after a domain reload and never hot reloaded.</summary>
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

        /// <summary>Verifies the active play session catalog is not replaced by a second preparation.</summary>
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

        private static void SetRawPreparedMarker(
            string path,
            string catalogId,
            string fingerprint)
        {
            SessionState.SetString("Bun3.Gameplay.Tags.PlaySession.Path", path);
            SessionState.SetString("Bun3.Gameplay.Tags.PlaySession.CatalogId", catalogId);
            SessionState.SetString("Bun3.Gameplay.Tags.PlaySession.Fingerprint", fingerprint);
        }
    }
}
