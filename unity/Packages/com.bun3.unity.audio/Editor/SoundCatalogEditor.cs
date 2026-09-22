using System;
using UnityEditor;
using UnityEngine;
namespace Bun3.Unity.Audio.Editor
{
    [CustomEditor(typeof(SoundCatalog))]
    internal sealed class SoundCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var catalog = (SoundCatalog)target;
            try { catalog.ValidateOrThrow(); }
            catch (ArgumentException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); return; }
            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                var entry = catalog.Entries[i];
                if (entry.Definition.Clips == null || entry.Definition.Clips.Length == 0)
                    EditorGUILayout.HelpBox(entry.Key + ": no direct clips. Preload Addressable clips before playback if configured.", MessageType.Warning);
            }
        }
    }
}
