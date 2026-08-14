#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal static class GameplayTagDiagnosticsPanel
    {
        internal static void Draw(
            IReadOnlyList<string> diagnostics,
            string? localSourcePath)
        {
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
            if (diagnostics.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("GameplayTag Workspace is invalid", EditorStyles.boldLabel);
            for (var index = 0; index < diagnostics.Count; index++)
            {
                EditorGUILayout.HelpBox(diagnostics[index], MessageType.Error);
            }

            EditorGUILayout.BeginHorizontal();
            if (CanOpenSource(localSourcePath)
                && GUILayout.Button("Open Source", GUILayout.Width(100f)))
            {
                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(
                    Path.GetFullPath(localSourcePath!), 1);
            }

            if (GUILayout.Button("Copy Details", GUILayout.Width(100f)))
            {
                CopyDetails(diagnostics);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        internal static bool CanOpenSource(string? localSourcePath) =>
            !string.IsNullOrWhiteSpace(localSourcePath) && File.Exists(localSourcePath);

        internal static void CopyDetails(IReadOnlyList<string> diagnostics)
        {
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
            EditorGUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, diagnostics);
        }

        internal static void ShowWarning(string title, string diagnostic)
        {
            if (title is null) throw new ArgumentNullException(nameof(title));
            if (diagnostic is null) throw new ArgumentNullException(nameof(diagnostic));
            EditorUtility.DisplayDialog(title, diagnostic, "OK");
        }
    }
}
