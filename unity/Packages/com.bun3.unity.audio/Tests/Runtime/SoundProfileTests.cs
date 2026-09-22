using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundProfileTests
    {
        [Test]
        public void SpatialAndRoutingProfiles_ReplaceDefaultsWithoutOverwritingLocalSettings()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var spatial = ScriptableObject.CreateInstance<SoundSpatialProfile>();
            var routing = ScriptableObject.CreateInstance<SoundRoutingProfile>();
            var attenuation = ScriptableObject.CreateInstance<DistanceAttenuationProfile>();
            var blend = ScriptableObject.CreateInstance<SpatialBlendProfile>();
            try
            {
                def.Spatial = SpatialMode.Follow;
                var acoustics = def.Acoustics.Local;
                acoustics.MinDistance = 5f;
                acoustics.MaxDistance = 60f;
                acoustics.DistanceAttenuation = false;
                acoustics.AttenuationProfile = attenuation;
                def.Occlusion = true;
                def.OcclusionVolumeAtFull = 0.2f;
                acoustics.InheritSpatialBlend = false;
                acoustics.SpatialBlendProfile = blend;
                acoustics.MonoDistance = 2f;
                acoustics.FullSpatialDistance = 6f;
                def.Acoustics.Local = acoustics;
                def.VolumeGroup = "local";
                def.SpatialProfile = spatial;
                def.RoutingProfile = routing;
                Assert.That(def.EffectiveSpatial, Is.EqualTo(SpatialMode.None));
                Assert.That(def.EffectiveAcoustics.MinDistance, Is.EqualTo(1f));
                Assert.That(def.EffectiveAcoustics.MaxDistance, Is.EqualTo(30f));
                Assert.That(def.EffectiveAcoustics.DistanceAttenuation, Is.True);
                Assert.That(def.EffectiveAcoustics.AttenuationProfile, Is.Null);
                Assert.That(def.EffectiveOcclusion, Is.False);
                Assert.That(def.EffectiveOcclusionVolumeAtFull, Is.EqualTo(-1f));
                Assert.That(def.EffectiveAcoustics.InheritSpatialBlend, Is.True);
                Assert.That(def.EffectiveAcoustics.SpatialBlendProfile, Is.Null);
                Assert.That(def.EffectiveAcoustics.MonoDistance, Is.EqualTo(1f));
                Assert.That(def.EffectiveAcoustics.FullSpatialDistance, Is.EqualTo(3f));
                Assert.That(def.EffectiveAcoustics.MinDistance, Is.EqualTo(1f));
                Assert.That(def.EffectiveVolumeGroup, Is.EqualTo("sfx"));
                def.SpatialProfile = null;
                def.RoutingProfile = null;
                Assert.That(def.EffectiveSpatial, Is.EqualTo(SpatialMode.Follow));
                Assert.That(def.EffectiveAcoustics.MinDistance, Is.EqualTo(5f));
                Assert.That(def.EffectiveAcoustics.MaxDistance, Is.EqualTo(60f));
                Assert.That(def.EffectiveAcoustics.DistanceAttenuation, Is.False);
                Assert.That(def.EffectiveAcoustics.AttenuationProfile, Is.SameAs(attenuation));
                Assert.That(def.EffectiveOcclusion, Is.True);
                Assert.That(def.EffectiveOcclusionVolumeAtFull, Is.EqualTo(0.2f));
                Assert.That(def.EffectiveAcoustics.InheritSpatialBlend, Is.False);
                Assert.That(def.EffectiveAcoustics.SpatialBlendProfile, Is.SameAs(blend));
                Assert.That(def.EffectiveAcoustics.MonoDistance, Is.EqualTo(2f));
                Assert.That(def.EffectiveAcoustics.FullSpatialDistance, Is.EqualTo(6f));
                Assert.That(def.EffectiveAcoustics.MinDistance, Is.EqualTo(5f));
                Assert.That(def.EffectiveVolumeGroup, Is.EqualTo("local"));
            }
            finally
            {
                Object.DestroyImmediate(def); Object.DestroyImmediate(spatial); Object.DestroyImmediate(routing);
                Object.DestroyImmediate(attenuation); Object.DestroyImmediate(blend);
            }
        }

        [Test]
        public void SharedPlayback_OverridesWholeGroup_AndRemovingItRestoresLocalValues()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var profile = ScriptableObject.CreateInstance<SoundPlaybackProfile>();
            try
            {
                def.Volume = new FloatRange(0.2f, 0.3f);
                def.Pitch = new FloatRange(0.5f, 0.6f);
                def.Loop = true;
                def.PlaybackProfile = profile;
                Assert.That(def.EffectiveVolume.Min, Is.EqualTo(1f));
                Assert.That(def.EffectivePitch.Min, Is.EqualTo(1f));
                Assert.That(def.EffectiveLoop, Is.False);
                profile.Volume = new FloatRange(0.8f, 0.9f);
                Assert.That(def.EffectiveVolume.Min, Is.EqualTo(0.8f));
                def.PlaybackProfile = null;
                Assert.That(def.EffectiveVolume.Min, Is.EqualTo(0.2f));
                Assert.That(def.EffectivePitch.Min, Is.EqualTo(0.5f));
                Assert.That(def.EffectiveLoop, Is.True);
            }
            finally { Object.DestroyImmediate(def); Object.DestroyImmediate(profile); }
        }

        [Test]
        public void SharedConcurrency_UsesIndependentDefinitionIdentity()
        {
            var first = ScriptableObject.CreateInstance<SoundDef>();
            var second = ScriptableObject.CreateInstance<SoundDef>();
            var profile = ScriptableObject.CreateInstance<SoundConcurrencyProfile>();
            try
            {
                profile.MaxInstances = 1;
                profile.Cooldown = 1f;
                first.ConcurrencyProfile = second.ConcurrencyProfile = profile;
                var table = new VoiceTable(4);
                Assert.That(table.TryAllocate(first, 10f, out var firstSlot, out _, out _), Is.True);
                Assert.That(table.TryAllocate(second, 10f, out var secondSlot, out var stolen, out _), Is.True);
                Assert.That(secondSlot, Is.Not.EqualTo(firstSlot));
                Assert.That(stolen, Is.EqualTo(-1));
                Assert.That(table.TryAllocate(first, 10f, out _, out _, out _), Is.False);
                profile.Cooldown = 0f;
                Assert.That(table.TryAllocate(first, 10f, out _, out stolen, out _), Is.True);
                Assert.That(stolen, Is.EqualTo(firstSlot));
                Assert.That(table.Slots[secondSlot].Def, Is.SameAs(second));
            }
            finally
            {
                Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ConcurrencyProfile_DefaultsOverrideLocalLimits_ThenRestoreThem()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var profile = ScriptableObject.CreateInstance<SoundConcurrencyProfile>();
            try
            {
                def.MaxInstances = 2;
                def.Cooldown = 5f;
                def.ConcurrencyProfile = profile;
                Assert.That(def.EffectiveMaxInstances, Is.Zero);
                Assert.That(def.EffectiveCooldown, Is.Zero);
                def.ConcurrencyProfile = null;
                Assert.That(def.EffectiveMaxInstances, Is.EqualTo(2));
                Assert.That(def.EffectiveCooldown, Is.EqualTo(5f));
            }
            finally { Object.DestroyImmediate(def); Object.DestroyImmediate(profile); }
        }

        [Test]
        public void VoiceAllocation_RollsEffectivePlayback()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var profile = ScriptableObject.CreateInstance<SoundPlaybackProfile>();
            try
            {
                profile.Volume = new FloatRange(0.25f, 0.25f);
                profile.Pitch = new FloatRange(1.5f, 1.5f);
                profile.Loop = true;
                def.PlaybackProfile = profile;
                var table = new VoiceTable(1);
                Assert.That(table.TryAllocate(def, 10f, out var slot, out _, out _), Is.True);
                Assert.That(table.Slots[slot].BaseVolume, Is.EqualTo(0.25f));
                Assert.That(table.Slots[slot].Pitch, Is.EqualTo(1.5f));
                Assert.That(table.Slots[slot].Loop, Is.True);
            }
            finally { Object.DestroyImmediate(def); Object.DestroyImmediate(profile); }
        }
    }
}
