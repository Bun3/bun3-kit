#nullable enable
using System;
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

        /// <summary>전체 경로를 읽기 전용 부모와 편집 가능한 마지막 세그먼트로 나눕니다.</summary>
        internal static GameplayTagTextEditRequest CreateRenameRequest(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            var separator = path.LastIndexOf('.');
            return separator < 0
                ? new GameplayTagTextEditRequest(string.Empty, path)
                : new GameplayTagTextEditRequest(
                    path.Substring(0, separator),
                    path.Substring(separator + 1));
        }

        /// <summary>마지막 세그먼트만 편집하는 이름 변경 modal을 열고 결과를 반환합니다.</summary>
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

        /// <summary>전체 경로를 읽기 전용으로 보여 주는 comment 편집 modal을 열고 결과를 반환합니다.</summary>
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
