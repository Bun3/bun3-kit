using System.Text;
using Bun3.Core.UnifiedToggle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bun3.Core.Editor.UnifiedToggle
{
    /// <summary>
    /// Custom inspector applied to all classes deriving from <see cref="BaseUnifiedToggle"/>.
    /// </summary>
    [CustomEditor(typeof(BaseUnifiedToggle), true)]
    public class BaseUnifiedToggleEditor : UnityEditor.Editor
    {
        protected SerializedProperty _script;
        protected SerializedProperty _authorGroup;
        protected SerializedProperty _options;

        protected BaseUnifiedToggle m_Toggle;

        private string _prevSignature;
        private bool _invalidHierarchyDialogShown;
        private bool _missingAuthorGroupDialogShown;

        private void OnEnable()
        {
            _script = serializedObject.FindProperty("m_Script");
            _authorGroup = serializedObject.FindProperty(BaseUnifiedToggle.PropertyNameAuthorGroup);
            _options = serializedObject.FindProperty("_options");

            m_Toggle = target as BaseUnifiedToggle;

            _prevSignature = ComputeOptionsSignature(_options);
            _invalidHierarchyDialogShown = false;
            _missingAuthorGroupDialogShown = false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_script);
            }

            var prevGroup = _authorGroup.objectReferenceValue;
            EditorGUILayout.PropertyField(_authorGroup);

            var newGroupObj = _authorGroup.objectReferenceValue;
            if (prevGroup != newGroupObj)
            {
                // Reject assignments that would create a non-ancestor link. The same Transform
                // (self) is also rejected — see BaseUnifiedToggle.IsStrictAncestor.
                if (newGroupObj is UnifiedToggleGroup attempt
                    && !BaseUnifiedToggle.IsStrictAncestor(attempt.transform, m_Toggle.transform))
                {
                    UnifiedToggleDialog.ShowAuthorGroupAssignmentRefused(m_Toggle.name, attempt.name);
                    _authorGroup.objectReferenceValue = prevGroup;
                }
                else
                {
                    if (prevGroup is UnifiedToggleGroup oldGroup)
                        oldGroup.Unregister((BaseUnifiedToggle)target);

                    if (newGroupObj is UnifiedToggleGroup newGroup)
                        newGroup.Register((BaseUnifiedToggle)target);

                    _invalidHierarchyDialogShown = false;
                }
            }

            // Safety net for invalid states reached via other paths (scripts, prefab edits).
            // Shown once per editor lifetime to avoid spamming the popup every repaint.
            if (_authorGroup.objectReferenceValue is UnifiedToggleGroup current
                && !BaseUnifiedToggle.IsStrictAncestor(current.transform, m_Toggle.transform))
            {
                if (!_invalidHierarchyDialogShown)
                {
                    _invalidHierarchyDialogShown = true;
                    UnifiedToggleDialog.ShowAuthorGroupHierarchyHint();
                }
            }
            else
            {
                _invalidHierarchyDialogShown = false;
            }

            // Standalone toggles are not supported — alert once when Author Group is missing.
            if (_authorGroup.objectReferenceValue == null)
            {
                if (!_missingAuthorGroupDialogShown)
                {
                    _missingAuthorGroupDialogShown = true;
                    UnifiedToggleDialog.ShowAuthorGroupRequired(m_Toggle.name);
                }
            }
            else
            {
                _missingAuthorGroupDialogShown = false;
            }
            
            EditorGUI.BeginChangeCheck();
            
            EditorGUILayout.PropertyField(_options, true);

            var newSignature = ComputeOptionsSignature(_options);
            var optionsStructureChanged = _prevSignature != newSignature;

            if (EditorGUI.EndChangeCheck() || optionsStructureChanged)
            {
                _prevSignature = newSignature;
                
                OnOptionChanged();

                serializedObject.ApplyModifiedProperties();

                if (_authorGroup.objectReferenceValue is UnifiedToggleGroup group)
                {
                    Undo.RecordObject(target, "Sync Options");
                    group.UpdateValues();

                    serializedObject.Update();
                }

                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(m_Toggle.gameObject.scene);

                Repaint();
            }

            serializedObject.ApplyModifiedProperties();

            DrawPropertiesExcluding(serializedObject, BaseUnifiedToggle.PropertyNameAuthorGroup, "_options", "m_Script");
        }

        protected virtual void OnOptionChanged()
        {
        }

        /// <summary>
        /// 옵션 리스트의 구조적 변화(요소 추가/삭제/타입 변경)를 안정적으로 감지하기 위한 지문.
        /// BeginChangeCheck가 놓치는 SubclassSelector의 managedReferenceValue 할당까지 잡는다.
        /// </summary>
        private static string ComputeOptionsSignature(SerializedProperty optionsProperty)
        {
            if (optionsProperty == null || !optionsProperty.isArray)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append(optionsProperty.arraySize);
            for (var i = 0; i < optionsProperty.arraySize; i++)
            {
                var elem = optionsProperty.GetArrayElementAtIndex(i);
                sb.Append('|').Append(elem.managedReferenceFullTypename);
            }
            return sb.ToString();
        }
    }
}
