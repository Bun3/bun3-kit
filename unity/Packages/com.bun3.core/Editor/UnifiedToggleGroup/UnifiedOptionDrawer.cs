using Bun3.Core.UnifiedToggle;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Bun3.Core.Editor.UnifiedToggle
{
    /// <summary>
    /// SubclassSelector가 fall-through할 때 잡혀, 옵션 본체의 자식들을 직접 그린다.
    /// 자식 중 _options(per-preset 값 리스트)는 add/remove/drag 모두 비활성화된
    /// ReorderableList로 대체하여 사이즈가 코드(SetOptionValues)로만 바뀌도록 강제한다.
    /// </summary>
    [CustomPropertyDrawer(typeof(UnifiedOptionBase), useForChildren: true)]
    public sealed class UnifiedOptionDrawer : PropertyDrawer
    {
        // 캐시하지 않는다. ReorderableList 내부의 SerializedProperty가 다음 프레임에
        // stale 되어 ReorderableList.count → minArraySize에서 InvalidOperationException을
        // 던지기 때문. SerializeReference 경로의 SerializedProperty는 수명이 매우 짧아서
        // 매 호출마다 새로 만든다.
        private static ReorderableList CreateList(SerializedProperty arrayProp)
        {
            var list = new ReorderableList(
                arrayProp.serializedObject, arrayProp,
                draggable: false,
                displayHeader: true,
                displayAddButton: false,
                displayRemoveButton: false);

            list.drawHeaderCallback = rect =>
                EditorGUI.LabelField(rect, "Options");

            // 배열 요소(elem) 자체가 아닌 elem.option만 그린다.
            //   - 배열 요소 컨텍스트 메뉴(Duplicate/Delete) 미표시
            //   - foldout 화살표가 ReorderableList 좌측 테두리를 침범하지 않음
            list.drawElementCallback = (rect, index, _, _) =>
            {
                var sp = list.serializedProperty;
                if (sp == null || index >= sp.arraySize) return;

                var elem = sp.GetArrayElementAtIndex(index);
                var keyProp = elem.FindPropertyRelative("key");
                var optionProp = elem.FindPropertyRelative("option");
                var elementLabel = keyProp != null && keyProp.propertyType == SerializedPropertyType.String
                    ? new GUIContent(keyProp.stringValue)
                    : new GUIContent($"Element {index}");

                if (optionProp != null)
                    EditorGUI.PropertyField(rect, optionProp, elementLabel, true);
                else
                    EditorGUI.LabelField(rect, elementLabel, new GUIContent("(missing 'option' field)"));
            };

            list.elementHeightCallback = i =>
            {
                var sp = list.serializedProperty;
                if (sp == null || i >= sp.arraySize) return EditorGUIUtility.singleLineHeight;

                var elem = sp.GetArrayElementAtIndex(i);
                var optionProp = elem.FindPropertyRelative("option");
                var h = optionProp != null
                    ? EditorGUI.GetPropertyHeight(optionProp, true)
                    : EditorGUIUtility.singleLineHeight;
                return h + EditorGUIUtility.standardVerticalSpacing;
            };

            return list;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float total = 0f;
            var iter = property.Copy();
            var end = property.GetEndProperty();

            // SerializeReference 타입 변경 직후 첫 프레임엔 children 트리가 비어 보일 수 있다.
            // 0을 리턴하면 외부 리스트가 0 높이를 캐시해 "접었다 펴기" 전까지 보이지 않으므로
            // single line 높이를 반환해 다음 프레임 정상 측정될 때까지 자리를 확보한다.
            if (!iter.NextVisible(true)) return EditorGUIUtility.singleLineHeight;

            while (!SerializedProperty.EqualContents(iter, end))
            {
                if (iter.name == "_options" && iter.isArray)
                {
                    total += CreateList(iter.Copy()).GetHeight();
                }
                else
                {
                    total += EditorGUI.GetPropertyHeight(iter, true)
                             + EditorGUIUtility.standardVerticalSpacing;
                }

                if (!iter.NextVisible(false)) break;
            }

            return total > 0f ? total : EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var y = position.y;
            var iter = property.Copy();
            var end = property.GetEndProperty();

            if (!iter.NextVisible(true))
            {
                EditorGUI.EndProperty();
                return;
            }

            while (!SerializedProperty.EqualContents(iter, end))
            {
                if (iter.name == "_options" && iter.isArray)
                {
                    var list = CreateList(iter.Copy());
                    var h = list.GetHeight();
                    list.DoList(new Rect(position.x, y, position.width, h));
                    y += h;
                }
                else
                {
                    var h = EditorGUI.GetPropertyHeight(iter, true);
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), iter, true);
                    y += h + EditorGUIUtility.standardVerticalSpacing;
                }

                if (!iter.NextVisible(false)) break;
            }

            EditorGUI.EndProperty();
        }
    }
}
