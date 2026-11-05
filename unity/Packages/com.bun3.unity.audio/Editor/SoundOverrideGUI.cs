using System;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>
    /// The look of profile-backed fields: inherited values are disabled, overridden values are bold with a margin bar
    /// like prefab overrides, and a right-click switches between the two.
    /// </summary>
    internal static class SoundOverrideGUI
    {
        private const float UseLocalWidth = 64f;
        private const float EditProfileWidth = 72f;
        private static readonly Color BarColor = new(1f / 255f, 153f / 255f, 235f / 255f, 0.75f);

        /// <summary>
        /// Draws a profile reference with Use local and Edit profile buttons when <paramref name="editable"/>.
        /// Clearing it, also in multi-object editing, discards the pending None and runs <paramref name="useLocal"/> instead.
        /// </summary>
        internal static void ProfileField(Rect rect, SerializedProperty reference, bool editable, Action useLocal)
        {
            var profile = reference.objectReferenceValue;
            var buttons = editable && profile != null;
            var field = rect;
            if (buttons) field.xMax -= UseLocalWidth + EditProfileWidth + 4f;
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(field, reference);
            if (EditorGUI.EndChangeCheck() && reference.objectReferenceValue == null)
            {
                reference.serializedObject.Update();
                useLocal();
            }
            if (!buttons) return;

            var button = new Rect(field.xMax + 2f, rect.y, UseLocalWidth, rect.height);
            if (GUI.Button(button, "Use local", EditorStyles.miniButton)) useLocal();
            button.x = button.xMax + 2f;
            button.width = EditProfileWidth;
            if (GUI.Button(button, "Edit profile", EditorStyles.miniButton))
            {
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
            }
        }

        /// <summary>Shows an Override or Revert menu when <paramref name="rect"/> is right-clicked.</summary>
        internal static void ContextMenu(Rect rect, bool inherited, GenericMenu.MenuFunction overrideField, GenericMenu.MenuFunction revert)
        {
            var current = Event.current;
            if (current.type != EventType.ContextClick || !rect.Contains(current.mousePosition)) return;
            var menu = new GenericMenu();
            if (inherited) menu.AddItem(new GUIContent("Override with local value"), false, overrideField);
            else menu.AddItem(new GUIContent("Revert to profile"), false, revert);
            menu.ShowAsContext();
            current.Use();
        }

        /// <summary>Marks an overridden value with a thin bar along the left edge of <paramref name="rect"/>.</summary>
        internal static void OverrideBar(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), BarColor);
        }

        /// <summary>Draws <paramref name="shown"/>; with neither flag set it is a plain local field.</summary>
        internal static void Field(Rect rect, SerializedProperty shown, bool inherited, bool overridden)
        {
            if (!inherited && !overridden)
            {
                EditorGUI.PropertyField(rect, shown, true);
                return;
            }
            // The bar sits at the view's left edge, where prefab overrides mark their rows.
            if (overridden) OverrideBar(new Rect(0f, rect.y, rect.width, rect.height));

            // EditorGUIUtility.SetBoldDefaultFont is internal, so the default label styles are switched for this field.
            var label = EditorStyles.label.fontStyle;
            var foldout = EditorStyles.foldout.fontStyle;
            var style = overridden ? FontStyle.Bold : FontStyle.Normal;
            EditorStyles.label.fontStyle = style;
            EditorStyles.foldout.fontStyle = style;
            try
            {
                using (new EditorGUI.DisabledScope(inherited))
                    EditorGUI.PropertyField(rect, shown, true);
            }
            finally
            {
                EditorStyles.label.fontStyle = label;
                EditorStyles.foldout.fontStyle = foldout;
            }
        }
    }
}
