#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    [CustomPropertyDrawer(typeof(GameplayTagRef))]
    internal sealed class GameplayTagRefDrawer : PropertyDrawer
    {
        private const float ClearButtonWidth = 22f;
        private const float Spacing = 2f;

        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property is null) throw new ArgumentNullException(nameof(property));
            if (label is null) throw new ArgumentNullException(nameof(label));

            EditorGUI.BeginProperty(position, label, property);
            try
            {
                var pathProperty = property.FindPropertyRelative("_path");
                if (pathProperty is null)
                {
                    EditorGUI.LabelField(position, label, new GUIContent("Invalid GameplayTagRef"));
                    return;
                }

                var initialPath = GetInitialPickerPath(property, out var isMixed);
                var workspace = GameplayTagRefInspectorWorkspace.OpenCurrent();
                var state = GameplayTagRefFieldState.Describe(initialPath, isMixed, workspace);
                var content = new GUIContent(state.DisplayText, state.Tooltip);
                if (state.HasWarning)
                {
                    content.image = EditorGUIUtility.IconContent("console.warnicon.sml").image;
                }

                var valueRect = EditorGUI.PrefixLabel(position, label);
                var clearRect = new Rect(
                    valueRect.xMax - ClearButtonWidth,
                    valueRect.y,
                    ClearButtonWidth,
                    valueRect.height);
                var dropdownRect = new Rect(
                    valueRect.x,
                    valueRect.y,
                    Math.Max(0f, valueRect.width - ClearButtonWidth - Spacing),
                    valueRect.height);

                var previousMixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = isMixed;
                try
                {
                    if (EditorGUI.DropdownButton(
                        dropdownRect,
                        content,
                        FocusType.Keyboard,
                        EditorStyles.popup))
                    {
                        OpenPicker(property, initialPath);
                    }
                }
                finally
                {
                    EditorGUI.showMixedValue = previousMixed;
                }

                using (new EditorGUI.DisabledScope(!isMixed && pathProperty.stringValue.Length == 0))
                {
                    if (GUI.Button(clearRect, new GUIContent("×", "None으로 지웁니다."), EditorStyles.miniButton))
                    {
                        ApplyPath(
                            property.serializedObject.targetObjects,
                            property.propertyPath,
                            string.Empty);
                    }
                }
            }
            finally
            {
                EditorGUI.EndProperty();
            }
        }

        internal static string GetInitialPickerPath(
            SerializedProperty property,
            out bool isMixed)
        {
            if (property is null) throw new ArgumentNullException(nameof(property));
            var pathProperty = property.FindPropertyRelative("_path")
                ?? throw new ArgumentException("GameplayTagRef path property가 없습니다.", nameof(property));
            isMixed = pathProperty.hasMultipleDifferentValues;
            return isMixed ? string.Empty : pathProperty.stringValue ?? string.Empty;
        }

        internal static bool ApplyPath(
            IReadOnlyList<UnityEngine.Object> targets,
            string propertyPath,
            string selectedPath)
        {
            if (targets is null) throw new ArgumentNullException(nameof(targets));
            if (propertyPath is null) throw new ArgumentNullException(nameof(propertyPath));
            if (selectedPath is null) throw new ArgumentNullException(nameof(selectedPath));

            var canonicalPath = selectedPath.Length == 0
                ? string.Empty
                : new GameplayTagRef(selectedPath).Path;
            var liveTargets = new List<UnityEngine.Object>(targets.Count);
            for (var index = 0; index < targets.Count; index++)
            {
                if (targets[index] != null) liveTargets.Add(targets[index]);
            }

            if (liveTargets.Count == 0) return false;

            var serialized = new SerializedObject(liveTargets.ToArray());
            serialized.UpdateIfRequiredOrScript();
            var referenceProperty = serialized.FindProperty(propertyPath);
            var pathProperty = referenceProperty?.FindPropertyRelative("_path");
            if (pathProperty is null) return false;

            pathProperty.stringValue = canonicalPath;
            serialized.ApplyModifiedProperties();
            return true;
        }

        private static void OpenPicker(SerializedProperty property, string initialPath)
        {
            var targets = (UnityEngine.Object[])property.serializedObject.targetObjects.Clone();
            var propertyPath = property.propertyPath;
            GameplayTagPickerWindow.ShowLive(
                GameplayTagRefInspectorWorkspace.OpenCurrent,
                initialPath,
                selected => ApplyPath(targets, propertyPath, selected));
        }
    }
}
