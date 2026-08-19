#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagRedirectCleanupDialog : EditorWindow
    {
        private static IReadOnlyList<string> _result = Array.Empty<string>();

        private string[] _candidates = Array.Empty<string>();
        private bool[] _selected = Array.Empty<bool>();
        private Vector2 _scroll;

        /// <summary>Shows removal candidates selected by default and returns the chosen old paths.</summary>
        /// <param name="candidates">Redirect old paths without project references.</param>
        internal static IReadOnlyList<string> ShowModal(IReadOnlyList<string> candidates)
        {
            if (candidates is null) throw new ArgumentNullException(nameof(candidates));
            if (candidates.Count == 0) return Array.Empty<string>();

            _result = Array.Empty<string>();
            var dialog = CreateInstance<GameplayTagRedirectCleanupDialog>();
            dialog.titleContent = new GUIContent("Remove Obsolete Redirects");
            dialog._candidates = new string[candidates.Count];
            dialog._selected = new bool[candidates.Count];
            for (var index = 0; index < candidates.Count; index++)
            {
                dialog._candidates[index] = candidates[index];
                dialog._selected[index] = true;
            }

            dialog.minSize = new Vector2(420f, 260f);
            dialog.ShowModalUtility();
            return _result;
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Cancel();
                return;
            }

            EditorGUILayout.HelpBox(
                GameplayTagRedirectMaintenance.NoProjectReferencesWarning, MessageType.Warning);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var index = 0; index < _candidates.Length; index++)
            {
                _selected[index] = EditorGUILayout.ToggleLeft(_candidates[index], _selected[index]);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var cancelled = GUILayout.Button("Cancel", GUILayout.Width(100f));
            var accepted = GUILayout.Button("Remove Selected", GUILayout.Width(140f));
            EditorGUILayout.EndHorizontal();

            if (cancelled)
            {
                Cancel();
                return;
            }

            if (!accepted) return;

            var selected = new List<string>(_candidates.Length);
            for (var index = 0; index < _candidates.Length; index++)
            {
                if (_selected[index]) selected.Add(_candidates[index]);
            }

            _result = selected;
            Close();
            GUIUtility.ExitGUI();
        }

        private void Cancel()
        {
            _result = Array.Empty<string>();
            Close();
            GUIUtility.ExitGUI();
        }
    }
}
