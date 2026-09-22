using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    [CustomPropertyDrawer(typeof(SoundAcousticSelection))]
    internal sealed class SoundAcousticSelectionDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position.height = EditorGUIUtility.singleLineHeight;
            property.isExpanded = EditorGUI.Foldout(position, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                var profile = property.FindPropertyRelative(nameof(SoundAcousticSelection.Profile));
                var local = property.FindPropertyRelative(nameof(SoundAcousticSelection.Local));
                position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUI.PropertyField(position, profile);
                    if (!profile.hasMultipleDifferentValues && profile.objectReferenceValue == null)
                    {
                        position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                        SoundAcousticSettingsGUI.Draw(ref position, local);
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
                var profile = property.FindPropertyRelative(nameof(SoundAcousticSelection.Profile));
                if (!profile.hasMultipleDifferentValues && profile.objectReferenceValue == null)
                {
                    lines += SoundAcousticSettingsGUI.GetLineCount(
                        property.FindPropertyRelative(nameof(SoundAcousticSelection.Local)));
                }
            }
            return lines * EditorGUIUtility.singleLineHeight
                + (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
        }
    }

    internal static class SoundAcousticSettingsGUI
    {
        internal static void DrawLayout(SerializedProperty settings)
        {
            EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(SoundAcousticSettings.DistanceAttenuation)));
            if (settings.FindPropertyRelative(nameof(SoundAcousticSettings.DistanceAttenuation)).boolValue)
            {
                var attenuationProfile = settings.FindPropertyRelative(nameof(SoundAcousticSettings.AttenuationProfile));
                EditorGUILayout.PropertyField(attenuationProfile);
                if (!attenuationProfile.hasMultipleDifferentValues && attenuationProfile.objectReferenceValue == null)
                {
                    EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(SoundAcousticSettings.MinDistance)));
                    EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(SoundAcousticSettings.MaxDistance)));
                }
            }

            EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(SoundAcousticSettings.InheritSpatialBlend)));
            if (!settings.FindPropertyRelative(nameof(SoundAcousticSettings.InheritSpatialBlend)).boolValue)
            {
                var blendProfile = settings.FindPropertyRelative(nameof(SoundAcousticSettings.SpatialBlendProfile));
                EditorGUILayout.PropertyField(blendProfile);
                if (!blendProfile.hasMultipleDifferentValues && blendProfile.objectReferenceValue == null)
                {
                    EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(SoundAcousticSettings.MonoDistance)));
                    EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(SoundAcousticSettings.FullSpatialDistance)));
                }
            }
        }

        internal static void Draw(ref Rect position, SerializedProperty settings)
        {
            DrawField(ref position, settings, nameof(SoundAcousticSettings.DistanceAttenuation));
            if (settings.FindPropertyRelative(nameof(SoundAcousticSettings.DistanceAttenuation)).boolValue)
            {
                var attenuationProfile = settings.FindPropertyRelative(nameof(SoundAcousticSettings.AttenuationProfile));
                DrawField(ref position, attenuationProfile);
                if (!attenuationProfile.hasMultipleDifferentValues && attenuationProfile.objectReferenceValue == null)
                {
                    DrawField(ref position, settings, nameof(SoundAcousticSettings.MinDistance));
                    DrawField(ref position, settings, nameof(SoundAcousticSettings.MaxDistance));
                }
            }

            DrawField(ref position, settings, nameof(SoundAcousticSettings.InheritSpatialBlend));
            if (!settings.FindPropertyRelative(nameof(SoundAcousticSettings.InheritSpatialBlend)).boolValue)
            {
                var blendProfile = settings.FindPropertyRelative(nameof(SoundAcousticSettings.SpatialBlendProfile));
                DrawField(ref position, blendProfile);
                if (!blendProfile.hasMultipleDifferentValues && blendProfile.objectReferenceValue == null)
                {
                    DrawField(ref position, settings, nameof(SoundAcousticSettings.MonoDistance));
                    DrawField(ref position, settings, nameof(SoundAcousticSettings.FullSpatialDistance));
                }
            }
        }

        internal static int GetLineCount(SerializedProperty settings)
        {
            var lines = 2;
            if (settings.FindPropertyRelative(nameof(SoundAcousticSettings.DistanceAttenuation)).boolValue)
            {
                lines++;
                var profile = settings.FindPropertyRelative(nameof(SoundAcousticSettings.AttenuationProfile));
                if (!profile.hasMultipleDifferentValues && profile.objectReferenceValue == null) lines += 2;
            }
            if (!settings.FindPropertyRelative(nameof(SoundAcousticSettings.InheritSpatialBlend)).boolValue)
            {
                lines++;
                var profile = settings.FindPropertyRelative(nameof(SoundAcousticSettings.SpatialBlendProfile));
                if (!profile.hasMultipleDifferentValues && profile.objectReferenceValue == null) lines += 2;
            }
            return lines;
        }

        private static void DrawField(ref Rect position, SerializedProperty parent, string name)
        {
            DrawField(ref position, parent.FindPropertyRelative(name));
        }

        private static void DrawField(ref Rect position, SerializedProperty property)
        {
            EditorGUI.PropertyField(position, property);
            position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        }
    }
}
