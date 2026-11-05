using System;
using UnityEditor;

namespace Bun3.Unity.Audio.Editor
{
    /// <summary>The shared-profile groups of <see cref="SoundDef"/> fields.</summary>
    public enum SoundProfileGroup
    {
        /// <summary>Volume, Pitch and Loop from <see cref="SoundDef.PlaybackProfile"/>.</summary>
        Playback,

        /// <summary>MixerGroup and VolumeGroup from <see cref="SoundDef.RoutingProfile"/>.</summary>
        Routing,

        /// <summary>MaxInstances, Cooldown and Priority from <see cref="SoundDef.ConcurrencyProfile"/>.</summary>
        Concurrency,

        /// <summary>Spatial, Acoustics, Occlusion and OcclusionVolumeAtFull from <see cref="SoundDef.SpatialProfile"/>.</summary>
        Spatial,
    }

    /// <summary>
    /// Editor rules for per-field profile overrides on <see cref="SoundDef"/> and <see cref="SoundAcousticSelection"/>.
    /// Overriding copies the profile value first and detaching copies every inherited value first, so neither changes
    /// what plays. Each call applies its edit as one undoable change and only reads the profile asset.
    /// A call on a field or group without a profile changes nothing.
    /// </summary>
    public static class SoundDefOverrides
    {
        private const string OverridesPath = "_overrides";

        private static readonly SoundDefField[][] GroupFields =
        {
            new[] { SoundDefField.Volume, SoundDefField.Pitch, SoundDefField.Loop },
            new[] { SoundDefField.MixerGroup, SoundDefField.VolumeGroup },
            new[] { SoundDefField.MaxInstances, SoundDefField.Cooldown, SoundDefField.Priority },
            new[] { SoundDefField.Spatial, SoundDefField.Acoustics, SoundDefField.Occlusion, SoundDefField.OcclusionVolumeAtFull },
        };

        /// <summary>Every acoustic field in inspector order.</summary>
        internal static readonly SoundAcousticField[] AcousticFields =
        {
            SoundAcousticField.DistanceAttenuation, SoundAcousticField.AttenuationProfile,
            SoundAcousticField.MinDistance, SoundAcousticField.MaxDistance,
            SoundAcousticField.InheritSpatialBlend, SoundAcousticField.SpatialBlendProfile,
            SoundAcousticField.MonoDistance, SoundAcousticField.FullSpatialDistance,
        };

        /// <summary>The fields of <paramref name="group"/> in inspector order.</summary>
        internal static SoundDefField[] FieldsOf(SoundProfileGroup group) => GroupFields[(int)group];

        /// <summary>Returns the profile group that owns a single <paramref name="field"/>.</summary>
        public static SoundProfileGroup GroupOf(SoundDefField field)
        {
            for (var group = 0; group < GroupFields.Length; group++)
                if (Array.IndexOf(GroupFields[group], field) >= 0) return (SoundProfileGroup)group;
            throw new ArgumentOutOfRangeException(nameof(field), field, "Expected a single SoundDefField flag.");
        }

        /// <summary>Returns the serialized path of a single <paramref name="field"/>; the profile asset uses the same path.</summary>
        public static string PropertyPath(SoundDefField field) =>
            field == SoundDefField.Acoustics ? "_acoustics" : field.ToString();

        /// <summary>Returns the serialized path of the profile reference of <paramref name="group"/>.</summary>
        public static string ProfilePropertyPath(SoundProfileGroup group) => $"{group}Profile";

        /// <summary>Whether <paramref name="field"/> has a profile and uses its value.</summary>
        public static bool IsInherited(SerializedObject def, SoundDefField field) =>
            ProfileOf(def, GroupOf(field)).objectReferenceValue != null && (Flags(def) & field) == 0;

        /// <summary>Copies the profile value into the local field, then marks it overridden.</summary>
        public static void Override(SerializedObject def, SoundDefField field)
        {
            if (!IsInherited(def, field)) return;
            using (var profile = new SerializedObject(ProfileOf(def, GroupOf(field)).objectReferenceValue))
                Copy(profile.FindProperty(PropertyPath(field)), def.FindProperty(PropertyPath(field)));
            SetFlags(def, Flags(def) | field);
            def.ApplyModifiedProperties();
        }

        /// <summary>Clears the override mark so the profile value is used again; the local value stays unused.</summary>
        public static void Revert(SerializedObject def, SoundDefField field)
        {
            if (ProfileOf(def, GroupOf(field)).objectReferenceValue == null) return;
            SetFlags(def, Flags(def) & ~field);
            def.ApplyModifiedProperties();
        }

        /// <summary>Copies every inherited value of <paramref name="group"/> into local fields, then clears its flags and profile.</summary>
        public static void UseLocal(SerializedObject def, SoundProfileGroup group)
        {
            var reference = ProfileOf(def, group);
            if (reference.objectReferenceValue == null) return;
            var flags = Flags(def);
            using (var profile = new SerializedObject(reference.objectReferenceValue))
            {
                foreach (var field in FieldsOf(group))
                {
                    if ((flags & field) == 0) Copy(profile.FindProperty(PropertyPath(field)), def.FindProperty(PropertyPath(field)));
                    flags &= ~field;
                }
            }
            SetFlags(def, flags);
            reference.objectReferenceValue = null;
            def.ApplyModifiedProperties();
        }

        /// <summary>Whether an acoustic field has an acoustic profile and uses its value.</summary>
        public static bool IsAcousticInherited(SerializedProperty selection, SoundAcousticField field) =>
            AcousticProfileOf(selection).objectReferenceValue != null && (AcousticFlags(selection) & field) == 0;

        /// <summary>Copies the acoustic profile value into the local setting, then marks it overridden.</summary>
        public static void OverrideAcoustic(SerializedProperty selection, SoundAcousticField field)
        {
            if (!IsAcousticInherited(selection, field)) return;
            using (var profile = new SerializedObject(AcousticProfileOf(selection).objectReferenceValue))
                CopyAcoustic(profile, selection, field);
            SetAcousticFlags(selection, AcousticFlags(selection) | field);
            selection.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Clears the acoustic override mark so the profile value is used again; the local value stays unused.</summary>
        public static void RevertAcoustic(SerializedProperty selection, SoundAcousticField field)
        {
            if (AcousticProfileOf(selection).objectReferenceValue == null) return;
            SetAcousticFlags(selection, AcousticFlags(selection) & ~field);
            selection.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Copies every inherited acoustic value into local settings, then clears the flags and profile.
        /// A multi-object selection applies pending edits, then detaches each target with its own flags.
        /// </summary>
        public static void UseLocalAcoustics(SerializedProperty selection)
        {
            var owner = selection.serializedObject;
            if (owner.isEditingMultipleObjects)
            {
                owner.ApplyModifiedProperties();
                foreach (var target in owner.targetObjects)
                    using (var single = new SerializedObject(target))
                        UseLocalAcoustics(single.FindProperty(selection.propertyPath));
                owner.Update();
                return;
            }

            var reference = AcousticProfileOf(selection);
            if (reference.objectReferenceValue == null) return;
            var flags = AcousticFlags(selection);
            using (var profile = new SerializedObject(reference.objectReferenceValue))
                foreach (var field in AcousticFields)
                    if ((flags & field) == 0) CopyAcoustic(profile, selection, field);
            SetAcousticFlags(selection, SoundAcousticField.None);
            reference.objectReferenceValue = null;
            selection.serializedObject.ApplyModifiedProperties();
        }

        private static SerializedProperty ProfileOf(SerializedObject def, SoundProfileGroup group) =>
            def.FindProperty(ProfilePropertyPath(group));

        private static SoundDefField Flags(SerializedObject def) => (SoundDefField)def.FindProperty(OverridesPath).intValue;

        private static void SetFlags(SerializedObject def, SoundDefField flags) => def.FindProperty(OverridesPath).intValue = (int)flags;

        private static SerializedProperty AcousticProfileOf(SerializedProperty selection) =>
            selection.FindPropertyRelative(nameof(SoundAcousticSelection.Profile));

        private static SoundAcousticField AcousticFlags(SerializedProperty selection) =>
            (SoundAcousticField)selection.FindPropertyRelative(nameof(SoundAcousticSelection.Overrides)).intValue;

        private static void SetAcousticFlags(SerializedProperty selection, SoundAcousticField flags) =>
            selection.FindPropertyRelative(nameof(SoundAcousticSelection.Overrides)).intValue = (int)flags;

        private static void CopyAcoustic(SerializedObject profile, SerializedProperty selection, SoundAcousticField field) =>
            Copy(
                profile.FindProperty($"{nameof(SoundAcousticProfile.Settings)}.{field}"),
                selection.FindPropertyRelative($"{nameof(SoundAcousticSelection.Local)}.{field}"));

        private static void Copy(SerializedProperty from, SerializedProperty to) => to.boxedValue = from.boxedValue;
    }
}
