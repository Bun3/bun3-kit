using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>
    /// Adds a Preview row to the <see cref="SoundDef"/> inspector: picks a random clip from
    /// <see cref="SoundDef.Clips"/> and plays it through Unity's internal editor audio-preview
    /// API (<c>UnityEditor.AudioUtil</c>), reached via reflection since that type is internal
    /// and its method set has churned across Editor versions. The reflection lookup runs once
    /// per domain load in a static constructor; a missing method disables the button instead of
    /// throwing during inspector draw. Verified against Unity 6000.3's
    /// <c>PlayPreviewClip(AudioClip, int, bool)</c> / <c>StopAllPreviewClips()</c> signatures.
    /// Pitch/volume variation is not reproduced by AudioUtil, so preview plays the raw clip and
    /// the def's rolled ranges are shown as read-only info instead.
    /// </summary>
    [CustomEditor(typeof(SoundDef))]
    internal sealed class SoundDefEditor : UnityEditor.Editor
    {
        // Not readonly: a failed Invoke nulls the offending one out so the button disables
        // itself and the warning it logs ("preview disabled") stays true for the rest of the session.
        private static MethodInfo PlayPreviewClipMethod;
        private static MethodInfo StopAllPreviewClipsMethod;

        static SoundDefEditor()
        {
            try
            {
                var audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
                PlayPreviewClipMethod = audioUtilType?.GetMethod(
                    "PlayPreviewClip",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(AudioClip), typeof(int), typeof(bool) },
                    null);
                StopAllPreviewClipsMethod = audioUtilType?.GetMethod(
                    "StopAllPreviewClips",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "Bun3.Unity.Audio.Editor: failed to reflect UnityEditor.AudioUtil preview " +
                    $"methods; SoundDef preview disabled. {e.Message}");
            }
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var def = (SoundDef)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            var hasClips = HasAnyClip(def.Clips);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasClips || PlayPreviewClipMethod == null))
                {
                    if (GUILayout.Button("▶ Preview"))
                    {
                        PlayRandomClip(def);
                    }
                }
                using (new EditorGUI.DisabledScope(StopAllPreviewClipsMethod == null))
                {
                    if (GUILayout.Button("■ Stop"))
                    {
                        StopAllPreview();
                    }
                }
            }

            EditorGUILayout.LabelField(
                $"Volume [{def.Volume.Min:0.##}, {def.Volume.Max:0.##}]  " +
                $"Pitch [{def.Pitch.Min:0.##}, {def.Pitch.Max:0.##}] " +
                "(preview plays the raw clip; rolled ranges are not applied)",
                EditorStyles.miniLabel);

            var hasAddressable = HasAddressableClips(def);
            if (!hasClips && !hasAddressable)
            {
                EditorGUILayout.HelpBox(
                    "No preview clips: assign Clips (or AddressableClips) to preview this sound.",
                    MessageType.Warning);
            }
            else if (!hasClips)
            {
                EditorGUILayout.HelpBox(
                    "Only Addressable clips are assigned; they need an async load, so preview " +
                    "them via Play Mode instead.",
                    MessageType.Info);
            }
        }

        private static bool HasAnyClip(AudioClip[] clips)
        {
            if (clips == null)
            {
                return false;
            }
            for (var i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasAddressableClips(SoundDef def)
        {
#if BUN3_ADDRESSABLES
            return def.AddressableClips != null && def.AddressableClips.Length > 0;
#else
            return false;
#endif
        }

        private static void PlayRandomClip(SoundDef def)
        {
            if (PlayPreviewClipMethod == null)
            {
                Debug.LogWarning(
                    "Bun3.Unity.Audio.Editor: AudioUtil.PlayPreviewClip not found on this " +
                    "Editor version; preview disabled.");
                return;
            }

            var candidates = new List<AudioClip>(def.Clips.Length);
            foreach (var clip in def.Clips)
            {
                if (clip != null)
                {
                    candidates.Add(clip);
                }
            }
            if (candidates.Count == 0)
            {
                return;
            }

            var picked = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            try
            {
                PlayPreviewClipMethod.Invoke(null, new object[] { picked, 0, false });
            }
            catch (Exception e)
            {
                PlayPreviewClipMethod = null;
                Debug.LogWarning(
                    $"Bun3.Unity.Audio.Editor: AudioUtil.PlayPreviewClip threw; preview " +
                    $"disabled for this session. {e.Message}");
            }
        }

        private static void StopAllPreview()
        {
            try
            {
                StopAllPreviewClipsMethod.Invoke(null, null);
            }
            catch (Exception e)
            {
                StopAllPreviewClipsMethod = null;
                Debug.LogWarning(
                    $"Bun3.Unity.Audio.Editor: AudioUtil.StopAllPreviewClips threw; stop " +
                    $"disabled for this session. {e.Message}");
            }
        }
    }
}
