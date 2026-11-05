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
}
