#nullable enable
using System;
using Bun3.Gameplay.Tags;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    /// <summary>Play 진입 전에 fresh Source를 binary round-trip하고 실패한 진입을 차단합니다.</summary>
    public static class GameplayTagPlayModeGate
    {
        private static bool _isRegistered;

        internal static bool IsRegistered => _isRegistered;

        /// <summary>Workspace를 개발 binary로 검증하고 활성 Play 전환 Catalog로 고정합니다.</summary>
        /// <param name="workspace">fresh Source에서 연 Workspace입니다.</param>
        /// <param name="catalog">성공하면 binary에서 재로드한 Catalog입니다.</param>
        /// <param name="diagnostic">실패 원인과 Source 진단입니다.</param>
        /// <returns>Play 진입을 계속할 수 있으면 true입니다.</returns>
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
                if (GameplayTagPlaySessionCatalog.Current is not null
                    || GameplayTagPlaySessionCatalog.TryRestorePrepared(out var restoreDiagnostic))
                {
                    return;
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
