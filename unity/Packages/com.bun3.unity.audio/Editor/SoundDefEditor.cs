using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>
    /// Draws <see cref="SoundDef"/> with per-field profile overrides (see <see cref="SoundDefOverrides"/>), the charge
    /// settings of a looping definition, and Preview and Stop through <see cref="SoundPreview"/>.
    /// </summary>
    [CustomEditor(typeof(SoundDef))]
    internal sealed class SoundDefEditor : UnityEditor.Editor
    {
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
            DrawCharge((SoundDef)target);
            serializedObject.ApplyModifiedProperties();

            DrawPreview((SoundDef)target);
        }

        private void DrawCharge(SoundDef def)
        {
            if (!def.EffectiveLoop) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(new GUIContent("Charge", "Used only when this sound plays as a charge loop through SoundSystem.PlayCharge."), EditorStyles.boldLabel);
            var charge = serializedObject.FindProperty(nameof(SoundDef.Charge));
            EditorGUILayout.PropertyField(charge.FindPropertyRelative(nameof(SoundChargeSettings.VolumeByProgress)));
            EditorGUILayout.PropertyField(charge.FindPropertyRelative(nameof(SoundChargeSettings.PitchByProgress)));
            EditorGUILayout.PropertyField(charge.FindPropertyRelative(nameof(SoundChargeSettings.CompleteFadeOut)));
            EditorGUILayout.PropertyField(charge.FindPropertyRelative(nameof(SoundChargeSettings.CancelFadeOut)));
        }

        private static void DrawPreview(SoundDef def)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            var hasClips = HasAnyClip(def.Clips);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasClips))
                    if (GUILayout.Button("▶ Preview")) SoundPreview.Play(def);
                if (GUILayout.Button("■ Stop")) SoundPreview.Stop();
            }

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
    }
}
