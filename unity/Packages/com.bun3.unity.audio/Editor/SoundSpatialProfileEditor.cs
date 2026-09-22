using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    [CustomEditor(typeof(SoundSpatialProfile))]
    [CanEditMultipleObjects]
    internal sealed class SoundSpatialProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(SoundSpatialProfile.Spatial)));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_acoustics"),
                new GUIContent("Acoustics"),
                true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(SoundSpatialProfile.Occlusion)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(SoundSpatialProfile.OcclusionVolumeAtFull)));
            serializedObject.ApplyModifiedProperties();
        }
    }
}
