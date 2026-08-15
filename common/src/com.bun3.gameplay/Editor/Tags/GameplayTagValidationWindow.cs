#nullable enable
using System;
using Bun3.Gameplay.Tags;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagValidationWindow : EditorWindow
    {
        private string _diagnostic = string.Empty;
        private string _stackTrace = string.Empty;
        private bool _showStackTrace;

        internal static void Show(string filePath, Exception error)
        {
            var window = GetWindow<GameplayTagValidationWindow>(utility: true, title: "Gameplay Tag Validation");
            window._diagnostic = FormatDiagnostic(filePath, error);
            window._stackTrace = error.StackTrace ?? string.Empty;
            window.Show();
        }

        internal static string FormatDiagnostic(string filePath, Exception error)
        {
            if (filePath is null) throw new ArgumentNullException(nameof(filePath));
            if (error is null) throw new ArgumentNullException(nameof(error));

            for (Exception? current = error; current is not null; current = current.InnerException)
            {
                if (current is TagCatalogException catalogError)
                {
                    return filePath + "\nJSON path: " + catalogError.JsonPath
                        + "\nLine: " + catalogError.LineNumber
                        + ", Position: " + catalogError.LinePosition
                        + "\n" + catalogError.Message;
                }
            }

            return filePath + "\n" + error.Message;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Gameplay Tag Catalog Error", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(_diagnostic, GUILayout.ExpandHeight(true));
            _showStackTrace = EditorGUILayout.Foldout(_showStackTrace, "Stack Trace");
            if (_showStackTrace)
            {
                EditorGUILayout.TextArea(_stackTrace, GUILayout.MinHeight(80f));
            }
        }
    }
}
