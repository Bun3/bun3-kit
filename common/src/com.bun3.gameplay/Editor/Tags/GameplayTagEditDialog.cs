#nullable enable
using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal readonly struct GameplayTagTextEditRequest
    {
        internal GameplayTagTextEditRequest(string parentPath, string initialValue)
        {
            ParentPath = parentPath;
            InitialValue = initialValue;
        }

        internal string ParentPath { get; }

        internal string InitialValue { get; }
    }

    internal readonly struct GameplayTagTextEditResult
    {
        internal GameplayTagTextEditResult(bool accepted, string value)
        {
            Accepted = accepted;
            Value = value;
        }

        internal bool Accepted { get; }

        internal string Value { get; }

        internal static GameplayTagTextEditResult Cancelled =>
            new GameplayTagTextEditResult(false, string.Empty);

        internal static GameplayTagTextEditResult Accept(string value) =>
            new GameplayTagTextEditResult(true, value ?? string.Empty);
    }

    internal sealed class GameplayTagEditDialog : EditorWindow
    {
        private const string ValueControl = "GameplayTag.EditDialogValue";

        private static GameplayTagTextEditResult _result = GameplayTagTextEditResult.Cancelled;

        private GameplayTagTextEditRequest _request;
        private string _pathLabel = string.Empty;
        private string _valueLabel = string.Empty;
        private string _value = string.Empty;
        private bool _multiline;
        private bool _requireValue;
        private bool _focusValue = true;

        /// <summary>Splits the full path into a read-only parent and an editable last segment.</summary>
        internal static GameplayTagTextEditRequest CreateRenameRequest(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            var canonical = GameplayTagCatalogEditSession.Canonicalize(path, nameof(path));
            var separator = canonical.LastIndexOf('.');
            return separator < 0
                ? new GameplayTagTextEditRequest(string.Empty, canonical)
                : new GameplayTagTextEditRequest(
                    canonical.Substring(0, separator),
                    canonical.Substring(separator + 1));
        }

        internal static string FormatShadowedRenameWarning(GameplayTagRenameResult result)
        {
            if (result is null) throw new ArgumentNullException(nameof(result));
            var message = new StringBuilder(
                "The renamed old paths remain active because another Tag Source still declares them. "
                + "Their redirects are shadowed until those declarations are removed:");
            for (var index = 0; index < result.ShadowedOldPaths.Count; index++)
            {
                message.Append(Environment.NewLine);
                message.Append("• ");
                message.Append(result.ShadowedOldPaths[index]);
            }

            return message.ToString();
        }

        internal static void ShowShadowedRenameWarning(GameplayTagRenameResult result)
        {
            if (result is null) throw new ArgumentNullException(nameof(result));
            if (result.ShadowedOldPaths.Count == 0) return;
            EditorUtility.DisplayDialog(
                "Gameplay Tag Rename Warning",
                FormatShadowedRenameWarning(result),
                "OK");
        }

        /// <summary>Opens a rename modal editing only the last segment and returns the result.</summary>
        internal static GameplayTagTextEditResult ShowRename(string path)
        {
            return Show(
                "Rename Gameplay Tag",
                "Parent Path",
                "Tag Name",
                CreateRenameRequest(path),
                multiline: false,
                requireValue: true);
        }

        /// <summary>Opens a comment-editing modal showing the full path read-only and returns the result.</summary>
        internal static GameplayTagTextEditResult ShowComment(string path, string comment)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            return Show(
                "Edit Gameplay Tag Comment",
                "Tag Path",
                "Comment",
                new GameplayTagTextEditRequest(path, comment ?? string.Empty),
                multiline: true,
                requireValue: false);
        }

        private static GameplayTagTextEditResult Show(
            string title,
            string pathLabel,
            string valueLabel,
            GameplayTagTextEditRequest request,
            bool multiline,
            bool requireValue)
        {
            _result = GameplayTagTextEditResult.Cancelled;
            var dialog = CreateInstance<GameplayTagEditDialog>();
            dialog.titleContent = new GUIContent(title);
            dialog._request = request;
            dialog._pathLabel = pathLabel;
            dialog._valueLabel = valueLabel;
            dialog._value = request.InitialValue;
            dialog._multiline = multiline;
            dialog._requireValue = requireValue;
            dialog.minSize = new Vector2(360f, multiline ? 160f : 96f);
            dialog.ShowModalUtility();
            return _result;
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Escape)
            {
                Cancel();
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(_pathLabel, _request.ParentPath);
            }

            if (_multiline)
            {
                EditorGUILayout.LabelField(_valueLabel);
                GUI.SetNextControlName(ValueControl);
                _value = EditorGUILayout.TextArea(_value, GUILayout.ExpandHeight(true));
            }
            else
            {
                GUI.SetNextControlName(ValueControl);
                _value = EditorGUILayout.TextField(_valueLabel, _value);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var cancelled = GUILayout.Button("Cancel", GUILayout.Width(80f));
            bool accepted;
            using (new EditorGUI.DisabledScope(_requireValue && _value.Length == 0))
            {
                accepted = GUILayout.Button("OK", GUILayout.Width(80f));
            }

            EditorGUILayout.EndHorizontal();

            if (_focusValue && Event.current.type == EventType.Repaint)
            {
                EditorGUI.FocusTextInControl(ValueControl);
                _focusValue = false;
            }

            if (cancelled)
            {
                Cancel();
                return;
            }

            if (!accepted) return;
            _result = GameplayTagTextEditResult.Accept(_value);
            Close();
            GUIUtility.ExitGUI();
        }

        private void Cancel()
        {
            _result = GameplayTagTextEditResult.Cancelled;
            Close();
            GUIUtility.ExitGUI();
        }
    }
}
