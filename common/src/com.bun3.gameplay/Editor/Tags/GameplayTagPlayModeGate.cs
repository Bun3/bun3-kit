#nullable enable
using System;
using Bun3.Gameplay.Tags;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Round-trips fresh sources through binary before entering play mode and blocks failed entries.</summary>
    public static class GameplayTagPlayModeGate
    {
        private static bool _isRegistered;

        internal static bool IsRegistered => _isRegistered;

        /// <summary>Verifies the workspace via the development binary and pins it as the active play-transition catalog.</summary>
        /// <param name="workspace">Workspace opened from fresh sources.</param>
        /// <param name="catalog">Catalog reloaded from binary on success.</param>
        /// <param name="diagnostic">Failure cause and source diagnostics.</param>
        /// <returns>True if entering play mode may continue.</returns>
        public static bool TryPrepare(
            GameplayTagEditorWorkspace workspace,
            out TagCatalog? catalog,
            out string diagnostic) =>
            TryPrepare(workspace, out catalog, out diagnostic, GameplayTagDevelopmentCatalogBuilder.Build);

        internal static bool TryPrepare(
            GameplayTagEditorWorkspace workspace,
            out TagCatalog? catalog,
            out string diagnostic,
            Func<GameplayTagEditorWorkspace, TagCatalog> build)
        {
            if (workspace is null) throw new ArgumentNullException(nameof(workspace));
            if (build is null) throw new ArgumentNullException(nameof(build));
            if (GameplayTagPlaySessionCatalog.Current is not null)
            {
                catalog = null;
                diagnostic = "The active Play session GameplayTag Catalog is already frozen.";
                return false;
            }

            if (!workspace.CanBuildCatalog)
            {
                catalog = null;
                diagnostic = workspace.Diagnostics.Count == 0
                    ? "The current GameplayTag Workspace cannot build a Catalog."
                    : string.Join(Environment.NewLine, workspace.Diagnostics);
                return false;
            }

            try
            {
                catalog = build(workspace);
                GameplayTagPlaySessionCatalog.Freeze(catalog);
                diagnostic = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                GameplayTagPlaySessionCatalog.Clear();
                catalog = null;
                diagnostic = exception.Message;
                return false;
            }
        }

        [InitializeOnLoadMethod]
        internal static void Initialize()
        {
            if (_isRegistered) return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            _isRegistered = true;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                GameplayTagPlaySessionCatalog.Clear();
            }
        }

        internal static void HandlePlayModeStateChanged(
            PlayModeStateChange state,
            Func<GameplayTagEditorWorkspace> openFreshWorkspace,
            Action<string> showWarning,
            Action cancelPlay,
            Func<GameplayTagEditorWorkspace, TagCatalog>? build = null)
        {
            if (openFreshWorkspace is null) throw new ArgumentNullException(nameof(openFreshWorkspace));
            if (showWarning is null) throw new ArgumentNullException(nameof(showWarning));
            if (cancelPlay is null) throw new ArgumentNullException(nameof(cancelPlay));
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                GameplayTagPlaySessionCatalog.Clear();
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                string restoreDiagnostic;
                try
                {
                    if (GameplayTagPlaySessionCatalog.Current is not null
                        || GameplayTagPlaySessionCatalog.TryRestorePrepared(out restoreDiagnostic))
                    {
                        return;
                    }
                }
                catch (Exception exception)
                {
                    GameplayTagPlaySessionCatalog.Clear();
                    restoreDiagnostic =
                        "Prepared GameplayTag Catalog could not be restored: " + exception.Message;
                }

                cancelPlay();
                showWarning(restoreDiagnostic);
                return;
            }

            if (state != PlayModeStateChange.ExitingEditMode) return;

            try
            {
                var workspace = openFreshWorkspace();
                TagCatalog? catalog;
                var prepared = build is null
                    ? TryPrepare(workspace, out catalog, out var diagnostic)
                    : TryPrepare(workspace, out catalog, out diagnostic, build);
                if (prepared)
                {
                    GameplayTagPlaySessionCatalog.RememberPrepared(
                        catalog!,
                        TagCatalogDevelopmentPath.Get(catalog!.CatalogId));
                    return;
                }
                cancelPlay();
                showWarning(diagnostic);
            }
            catch (Exception exception)
            {
                GameplayTagPlaySessionCatalog.Clear();
                cancelPlay();
                showWarning(exception.Message);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state) =>
            HandlePlayModeStateChanged(
                state,
                OpenFreshWorkspace,
                diagnostic => GameplayTagDiagnosticsPanel.ShowWarning(
                    "GameplayTag Play Mode Blocked", diagnostic),
                () => EditorApplication.isPlaying = false);

        private static GameplayTagEditorWorkspace OpenFreshWorkspace()
        {
            var sourcePath = GameplayTagGameSourcePath.Get(Application.dataPath);
            return GameplayTagEditorWorkspace.Open(
                GameplayTagBuildContextResolver.ResolveDevelopment(sourcePath),
                sourcePath);
        }
    }
}
