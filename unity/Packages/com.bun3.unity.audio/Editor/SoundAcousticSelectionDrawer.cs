using System;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    [CustomPropertyDrawer(typeof(SoundAcousticSelection))]
    internal sealed class SoundAcousticSelectionDrawer : PropertyDrawer
    {
        private SerializedObject _profile;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                var selection = property.Copy();
                var reference = property.FindPropertyRelative(nameof(SoundAcousticSelection.Profile));
                var profile = ProfileObject(reference);
                // Override flags are edited on one object at a time so targets with different flags are never merged.
                var editable = GUI.enabled && !property.serializedObject.isEditingMultipleObjects;
                using (new EditorGUI.IndentLevelScope())
                {
                    NextLine(ref line);
                    SoundOverrideGUI.ProfileField(line, reference, editable, () => SoundDefOverrides.UseLocalAcoustics(selection));
                    if (!reference.hasMultipleDifferentValues)
                    {
                        foreach (var field in SoundDefOverrides.AcousticFields)
                        {
                            if (!IsVisible(selection, profile, field)) continue;
                            NextLine(ref line);
                            DrawField(line, selection, profile, field, editable);
                        }
                    }
                }
            }
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var lines = 1;
            if (property.isExpanded)
            {
                lines++;
                var reference = property.FindPropertyRelative(nameof(SoundAcousticSelection.Profile));
                if (!reference.hasMultipleDifferentValues)
                {
                    var profile = ProfileObject(reference);
                    foreach (var field in SoundDefOverrides.AcousticFields)
                        if (IsVisible(property, profile, field)) lines++;
                }
            }
            return lines * EditorGUIUtility.singleLineHeight
                + (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
        }

        private static void DrawField(Rect rect, SerializedProperty selection, SerializedObject profile, SoundAcousticField field, bool editable)
        {
            var inherited = profile != null && SoundDefOverrides.IsAcousticInherited(selection, field);
            if (profile != null && editable)
            {
                SoundOverrideGUI.ContextMenu(rect, inherited,
                    () => { selection.serializedObject.Update(); SoundDefOverrides.OverrideAcoustic(selection, field); },
                    () => { selection.serializedObject.Update(); SoundDefOverrides.RevertAcoustic(selection, field); });
            }
            SoundOverrideGUI.Field(rect, Shown(selection, profile, field), inherited, profile != null && !inherited);
        }

        private static bool IsVisible(SerializedProperty selection, SerializedObject profile, SoundAcousticField field) =>
            SoundAcousticSettingsGUI.IsVisible(field, dependency => Shown(selection, profile, dependency));

        private static SerializedProperty Shown(SerializedProperty selection, SerializedObject profile, SoundAcousticField field) =>
            profile != null && SoundDefOverrides.IsAcousticInherited(selection, field)
                ? profile.FindProperty($"{nameof(SoundAcousticProfile.Settings)}.{field}")
                : selection.FindPropertyRelative($"{nameof(SoundAcousticSelection.Local)}.{field}");

        // Read-only view of the assigned profile; never applied.
        private SerializedObject ProfileObject(SerializedProperty reference)
        {
            var profile = reference.hasMultipleDifferentValues ? null : reference.objectReferenceValue;
            if (profile == null) return null;
            if (_profile == null || _profile.targetObject != profile)
            {
                _profile?.Dispose();
                _profile = new SerializedObject(profile);
            }
            _profile.Update();
            return _profile;
        }

        private static void NextLine(ref Rect line) =>
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
    }

    internal static class SoundAcousticSettingsGUI
    {
        internal static void DrawLayout(SerializedProperty settings)
        {
            foreach (var field in SoundDefOverrides.AcousticFields)
                if (IsVisible(field, dependency => settings.FindPropertyRelative(dependency.ToString())))
                    EditorGUILayout.PropertyField(settings.FindPropertyRelative(field.ToString()));
        }

        /// <summary>Whether <paramref name="field"/> applies, given the shown values of the fields it depends on.</summary>
        internal static bool IsVisible(SoundAcousticField field, Func<SoundAcousticField, SerializedProperty> shown) => field switch
        {
            SoundAcousticField.AttenuationProfile => shown(SoundAcousticField.DistanceAttenuation).boolValue,
            SoundAcousticField.MinDistance or SoundAcousticField.MaxDistance =>
                shown(SoundAcousticField.DistanceAttenuation).boolValue && IsNone(shown(SoundAcousticField.AttenuationProfile)),
            SoundAcousticField.SpatialBlendProfile => !shown(SoundAcousticField.InheritSpatialBlend).boolValue,
            SoundAcousticField.MonoDistance or SoundAcousticField.FullSpatialDistance =>
                !shown(SoundAcousticField.InheritSpatialBlend).boolValue && IsNone(shown(SoundAcousticField.SpatialBlendProfile)),
            _ => true,
        };

        private static bool IsNone(SerializedProperty profile) =>
            !profile.hasMultipleDifferentValues && profile.objectReferenceValue == null;
    }

    /// <summary>
    /// The look of profile-backed fields: inherited values are disabled, overridden values are bold with a margin bar
    /// like prefab overrides, and a right-click switches between the two.
    /// </summary>
    internal static class SoundOverrideGUI
    {
        private const float UseLocalWidth = 64f;
        private const float EditProfileWidth = 72f;
        private static readonly Color BarColor = new(1f / 255f, 153f / 255f, 235f / 255f, 0.75f);

        /// <summary>Draws a profile reference with Use local and Edit profile buttons; clearing it runs <paramref name="useLocal"/> instead.</summary>
        internal static void ProfileField(Rect rect, SerializedProperty reference, bool editable, Action useLocal)
        {
            var profile = reference.objectReferenceValue;
            var buttons = editable && profile != null;
            var field = rect;
            if (buttons) field.xMax -= UseLocalWidth + EditProfileWidth + 4f;
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(field, reference);
            var cleared = EditorGUI.EndChangeCheck() && buttons && reference.objectReferenceValue == null;
            if (cleared) reference.objectReferenceValue = profile;
            if (!buttons) return;

            var button = new Rect(field.xMax + 2f, rect.y, UseLocalWidth, rect.height);
            if (GUI.Button(button, "Use local", EditorStyles.miniButton) || cleared) useLocal();
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

        /// <summary>Draws <paramref name="shown"/>; with neither flag set it is a plain local field.</summary>
        internal static void Field(Rect rect, SerializedProperty shown, bool inherited, bool overridden)
        {
            if (!inherited && !overridden)
            {
                EditorGUI.PropertyField(rect, shown, true);
                return;
            }
            if (overridden && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(0f, rect.y, 2f, rect.height), BarColor);

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
