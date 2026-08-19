using Bun3.Unity.Core.UnifiedToggle;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Bun3.Unity.Core.Editor.UnifiedToggle
{
    /// <summary>
    /// Catches SubclassSelector fall-through and draws the option's children directly.
    /// The _options child (per-preset value list) is drawn as a ReorderableList with
    /// add/remove/drag disabled, so its size can only change via SetOptionValues.
    /// </summary>
    [CustomPropertyDrawer(typeof(UnifiedOptionBase), useForChildren: true)]
    public sealed class UnifiedOptionDrawer : PropertyDrawer
    {
        // Never cache: the list's internal SerializedProperty goes stale next frame and
        // ReorderableList.count → minArraySize throws InvalidOperationException.
        // SerializeReference-path SerializedProperties are short-lived, so build fresh each call.
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

            // Draw only elem.option, not the array element itself:
            //   - hides the element context menu (Duplicate/Delete)
            //   - keeps the foldout arrow off the ReorderableList's left border
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

            // Right after a SerializeReference type change the children tree can appear empty
            // for one frame. Returning 0 would let the outer list cache zero height (invisible
            // until re-folded), so return single-line height until the next frame measures correctly.
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
