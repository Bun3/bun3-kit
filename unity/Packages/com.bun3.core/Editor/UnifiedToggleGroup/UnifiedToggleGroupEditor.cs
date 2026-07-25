using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using GroupRuntime = Bun3.Core.UnifiedToggle.UnifiedToggleGroup;

namespace Bun3.Core.Editor.UnifiedToggle
{
    [CustomEditor(typeof(GroupRuntime), true)]
    public class UnifiedToggleGroupEditor : UnityEditor.Editor
    {
        private SerializedProperty _presetsProperty;
        private SerializedProperty _togglesProperty;

        private void OnEnable()
        {
            _presetsProperty = serializedObject.FindProperty("_presets");
            _togglesProperty = serializedObject.FindProperty("_toggles");
        }

        public override void OnInspectorGUI()
        {
            var group = (GroupRuntime)target;

            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(_presetsProperty, true);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_togglesProperty, true);
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                group.UpdateValues();
                MarkAllDirty(group);
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawButtons(group);
        }

        private void MarkAllDirty(GroupRuntime group)
        {
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
        }

        private void DrawButtons(GroupRuntime group)
        {
            var changed = false;
            EditorGUILayout.BeginHorizontal();

            var presets = group.GetPresets();
            if (presets == null) return;

            foreach (var value in presets)
            {
                var origin = GUI.color;
                GUI.color = group.CurrentPreset == value ? Color.green : Color.white;
                if (GUILayout.Button(value, GUILayout.Height(30)))
                {
                    changed = true;
                    group.SetValue(value);
                }

                GUI.color = origin;
            }

            EditorGUILayout.EndHorizontal();

            if (changed)
            {
                MarkAllDirty(group);
            }
        }
    }
}
