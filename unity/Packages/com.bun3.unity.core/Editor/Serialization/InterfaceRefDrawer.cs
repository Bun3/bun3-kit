using System;
using System.Collections.Generic;
using System.Reflection;
using Bun3.Unity.Core.Serialization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Core.Editor.Serialization
{
    /// <summary>Object field for <see cref="InterfaceRef{T}"/> that only stores objects implementing <c>T</c>.</summary>
    [CustomPropertyDrawer(typeof(InterfaceRef<>))]
    public sealed class InterfaceRefDrawer : PropertyDrawer
    {
        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var target = property.FindPropertyRelative("target");
            var refType = RefType(fieldInfo.FieldType);
            var interfaceType = refType.GetGenericArguments()[0];
            label = new GUIContent(label.text, interfaceType.Name);

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            var dropped = EditorGUI.ObjectField(position, label, target.objectReferenceValue, typeof(Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                var resolved = (Object)refType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { dropped });
                if (dropped == null || resolved != null) target.objectReferenceValue = resolved;
                else Debug.LogWarning(dropped.name + " does not implement " + interfaceType.Name, dropped);
            }
            EditorGUI.EndProperty();
        }

        // The field may be InterfaceRef<T>, an array of it, or a List of it.
        private static Type RefType(Type fieldType)
        {
            if (fieldType.IsArray) return fieldType.GetElementType();
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>)) return fieldType.GetGenericArguments()[0];
            return fieldType;
        }
    }
}
