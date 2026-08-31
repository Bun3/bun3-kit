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
            IReadOnlyList<GameplayTagWorkspaceDiagnostic> diagnostics)
        {
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
            if (diagnostics.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("GameplayTag Workspace is invalid", EditorStyles.boldLabel);
            for (var index = 0; index < diagnostics.Count; index++)
            {
                var diagnostic = diagnostics[index];
                EditorGUILayout.HelpBox(diagnostic.Message, MessageType.Error);
                if (CanOpenSource(diagnostic)
                    && GUILayout.Button("Open Source", GUILayout.Width(100f)))
                {
                    OpenSource(
                        diagnostic,
                        path => UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(
                            path, 1));
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Details", GUILayout.Width(100f)))
            {
                var messages = new string[diagnostics.Count];
                for (var index = 0; index < messages.Length; index++)
                {
                    messages[index] = diagnostics[index].Message;
                }

                CopyDetails(messages);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        internal static bool CanOpenSource(string? localSourcePath) =>
            !string.IsNullOrWhiteSpace(localSourcePath) && File.Exists(localSourcePath);

        internal static bool CanOpenSource(GameplayTagWorkspaceDiagnostic diagnostic)
        {
            if (diagnostic is null) throw new ArgumentNullException(nameof(diagnostic));
            return CanOpenSource(diagnostic.LocalSourcePath);
        }

        internal static bool OpenSource(
            GameplayTagWorkspaceDiagnostic diagnostic,
            Action<string> open)
        {
            if (diagnostic is null) throw new ArgumentNullException(nameof(diagnostic));
            if (open is null) throw new ArgumentNullException(nameof(open));
            if (!CanOpenSource(diagnostic)) return false;
            open(Path.GetFullPath(diagnostic.LocalSourcePath!));
            return true;
        }

        internal static void CopyDetails(IReadOnlyList<string> diagnostics)
        {
            if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
            EditorGUIUtility.systemCopyBuffer = string.Join(Environment.NewLine, diagnostics);
        }

        internal static void ShowWarning(string title, string diagnostic)
        {
            if (title is null) throw new ArgumentNullException(nameof(title));
            if (diagnostic is null) throw new ArgumentNullException(nameof(diagnostic));
            if (Application.isBatchMode)
            {
                Debug.LogWarning($"{title}: {diagnostic}");
                return;
            }

            EditorUtility.DisplayDialog(title, diagnostic, "OK");
        }
    }
}
