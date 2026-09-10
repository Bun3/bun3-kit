using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bun3.Unity.Inspector.Editor
{
    /// <summary>Draws project sorting layers without changing unresolved serialized values.</summary>
    [CustomPropertyDrawer(typeof(SortingLayerAttribute))]
    public sealed class SortingLayerDrawer : PropertyDrawer
    {
        /// <inheritdoc />
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * (IsSupported(property) ? 1 : 2);
        }

        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using (new EditorGUI.PropertyScope(position, label, property))
            {
                if (!IsSupported(property))
                {
                    EditorGUI.HelpBox(position, "SortingLayer requires an int ID or string name field.", MessageType.Error);
                    return;
                }

                var layers = SortingLayer.layers;
                var selected = -1;
                for (var i = 0; i < layers.Length; i++)
                {
                    if (property.propertyType == SerializedPropertyType.Integer
                        ? property.intValue == layers[i].id
                        : property.stringValue == layers[i].name)
                    {
                        selected = i;
                        break;
                    }
                }

                var missing = selected < 0 && !property.hasMultipleDifferentValues;
                var offset = missing ? 1 : 0;
                var options = new GUIContent[layers.Length + offset];
                if (missing)
                {
                    var value = property.propertyType == SerializedPropertyType.Integer
                        ? property.intValue.ToString()
                        : "\"" + property.stringValue + "\"";
                    options[0] = new GUIContent("Missing: " + value);
                    selected = 0;
                }
                for (var i = 0; i < layers.Length; i++)
                    options[i + offset] = new GUIContent(layers[i].name, "ID: " + layers[i].id);

                var previousMixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                try
                {
                    EditorGUI.BeginChangeCheck();
                    var choice = EditorGUI.Popup(position, label, property.hasMultipleDifferentValues ? -1 : selected, options);
                    if (EditorGUI.EndChangeCheck() && choice >= offset && choice < options.Length)
                    {
                        var layer = layers[choice - offset];
                        if (property.propertyType == SerializedPropertyType.Integer)
                            property.intValue = layer.id;
                        else
                            property.stringValue = layer.name;
                    }
                }
                finally
                {
                    EditorGUI.showMixedValue = previousMixed;
                }
            }
        }

        /// <inheritdoc />
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var field = property.Copy();
            return new IMGUIContainer(() =>
            {
                if (field.serializedObject.targetObject == null)
                    return;
                field.serializedObject.UpdateIfRequiredOrScript();
                var label = new GUIContent(field.displayName, field.tooltip);
                var position = EditorGUILayout.GetControlRect(true, GetPropertyHeight(field, label));
                EditorGUI.BeginChangeCheck();
                OnGUI(position, field, label);
                if (EditorGUI.EndChangeCheck())
                    field.serializedObject.ApplyModifiedProperties();
            });
        }

        private static bool IsSupported(SerializedProperty property)
        {
            return (property.propertyType == SerializedPropertyType.Integer && property.type == "int")
                || property.propertyType == SerializedPropertyType.String;
        }
    }
}
