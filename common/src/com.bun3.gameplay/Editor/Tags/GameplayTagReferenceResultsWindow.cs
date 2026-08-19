#nullable enable
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagReferenceResultsWindow : EditorWindow
    {
        private GameplayTagReferenceSearchResult _result;
        private bool _hasResult;
        private Vector2 _scroll;

        /// <summary>Shows a reference search result as a window listing text matches.</summary>
        /// <param name="result">Reference search result to show.</param>
        internal static void Show(GameplayTagReferenceSearchResult result)
        {
            var window = GetWindow<GameplayTagReferenceResultsWindow>(utility: true, title: "Text Matches");
            window._result = result;
            window._hasResult = true;
            window.Show();
        }

        /// <summary>Builds the display text and tooltip for a single match.</summary>
        /// <param name="match">Reference match to display.</param>
        internal static GUIContent CreateMatchContent(GameplayTagReferenceMatch match)
        {
            var text = match.RedirectSource + "  —  " + match.DisplayPath + ":" + match.LineNumber;
            return new GUIContent(text, text + "\n" + match.Preview);
        }

        private void OnGUI()
        {
            if (!_hasResult)
            {
                EditorGUILayout.LabelField("No reference search has been run.");
                return;
            }

            DrawBanner();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var matches = _result.Matches;
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                if (GUILayout.Button(CreateMatchContent(match), EditorStyles.label))
                {
                    OpenMatch(match);
                }

                EditorGUILayout.LabelField(match.Preview, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawBanner()
        {
            if (_result.IsComplete)
            {
                EditorGUILayout.HelpBox(
                    _result.Matches.Count + " text match(es) found. "
                        + GameplayTagRedirectMaintenance.ExternalDataScopeWarning,
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                _result.IsCancelled
                    ? "The search was cancelled, so no redirect can be classified as obsolete."
                    : "Some files could not be read, so no redirect can be classified as obsolete.",
                MessageType.Warning);

            var errors = _result.Errors;
            for (var index = 0; index < errors.Count; index++)
            {
                EditorGUILayout.LabelField(errors[index], EditorStyles.miniLabel);
            }
        }

        private static void OpenMatch(GameplayTagReferenceMatch match)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(match.DisplayPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                AssetDatabase.OpenAsset(asset, match.LineNumber);
                return;
            }

            InternalEditorUtility.OpenFileAtLineExternal(match.AbsolutePath, match.LineNumber);
        }
    }
}
