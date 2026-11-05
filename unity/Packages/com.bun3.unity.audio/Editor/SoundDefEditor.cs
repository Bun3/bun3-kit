using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>
    /// Draws <see cref="SoundDef"/> with per-field profile overrides (see <see cref="SoundDefOverrides"/>) and a Preview
    /// row. Preview picks a random clip from
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

        private readonly SerializedObject[] _profiles = new SerializedObject[4];

        private void OnDisable()
        {
            foreach (var profile in _profiles) profile?.Dispose();
        }

        private void DrawGroup(SoundProfileGroup group)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(group.ToString(), EditorStyles.boldLabel);
            var reference = serializedObject.FindProperty(SoundDefOverrides.ProfilePropertyPath(group));
            var profile = ProfileObject(group, reference.objectReferenceValue);
            SoundOverrideGUI.ProfileField(EditorGUILayout.GetControlRect(), reference, GUI.enabled,
                () => SoundDefOverrides.UseLocal(serializedObject, group));
            foreach (var field in SoundDefOverrides.FieldsOf(group))
                DrawField(field, profile);
        }

        private void DrawField(SoundDefField field, SerializedObject profile)
        {
            var path = SoundDefOverrides.PropertyPath(field);
            var local = serializedObject.FindProperty(path);
            var inherited = profile != null && SoundDefOverrides.IsInherited(serializedObject, field);
            var shown = inherited ? profile.FindProperty(path) : local;
            shown.isExpanded = local.isExpanded;
            var rect = EditorGUILayout.GetControlRect(true, EditorGUI.GetPropertyHeight(shown, true));
            if (profile != null && GUI.enabled)
            {
                // Acoustics answers only on its header line; its own rows carry the acoustic profile menu.
                var menuRect = field == SoundDefField.Acoustics ? new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight) : rect;
                SoundOverrideGUI.ContextMenu(menuRect, inherited,
                    () => { serializedObject.Update(); SoundDefOverrides.Override(serializedObject, field); },
                    () => { serializedObject.Update(); SoundDefOverrides.Revert(serializedObject, field); });
            }
            SoundOverrideGUI.Field(rect, shown, inherited, profile != null && !inherited);
            local.isExpanded = shown.isExpanded;
        }

        // Read-only view of the assigned profile; never applied.
        private SerializedObject ProfileObject(SoundProfileGroup group, Object profile)
        {
            ref var cached = ref _profiles[(int)group];
            if (profile == null) return null;
            if (cached == null || cached.targetObject != profile)
            {
                cached?.Dispose();
                cached = new SerializedObject(profile);
            }
            cached.Update();
            return cached;
        }

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
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(SoundDef.Clips)), true);
#if BUN3_ADDRESSABLES
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(SoundDef.AddressableClips)), true);
#endif
            for (var group = SoundProfileGroup.Playback; group <= SoundProfileGroup.Spatial; group++)
                DrawGroup(group);
            serializedObject.ApplyModifiedProperties();

            DrawPreview((SoundDef)target);
        }

        private static void DrawPreview(SoundDef def)
        {
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
                $"Volume [{def.EffectiveVolume.Min:0.##}, {def.EffectiveVolume.Max:0.##}]  " +
                $"Pitch [{def.EffectivePitch.Min:0.##}, {def.EffectivePitch.Max:0.##}] " +
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
