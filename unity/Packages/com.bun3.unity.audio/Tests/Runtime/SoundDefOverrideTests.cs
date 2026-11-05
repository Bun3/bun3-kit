using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundDefOverrideTests
    {
        SoundDef def;
        SoundPlaybackProfile playback;
        SoundRoutingProfile routing;
        SoundConcurrencyProfile concurrency;
        SoundSpatialProfile spatial;

        [SetUp]
        public void Setup()
        {
            def = ScriptableObject.CreateInstance<SoundDef>();
            playback = ScriptableObject.CreateInstance<SoundPlaybackProfile>();
            routing = ScriptableObject.CreateInstance<SoundRoutingProfile>();
            concurrency = ScriptableObject.CreateInstance<SoundConcurrencyProfile>();
            spatial = ScriptableObject.CreateInstance<SoundSpatialProfile>();
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(playback);
            Object.DestroyImmediate(routing);
            Object.DestroyImmediate(concurrency);
            Object.DestroyImmediate(spatial);
        }

        [Test]
        public void Effective_UsesProfileUnlessFieldIsOverridden()
        {
            playback.Volume = new FloatRange(0.2f, 0.2f);
            def.Volume = new FloatRange(0.7f, 0.7f);
            def.PlaybackProfile = playback;
            Assert.That(def.EffectiveVolume.Min, Is.EqualTo(0.2f));
            def.Overrides = SoundDefField.Volume;
            Assert.That(def.IsOverridden(SoundDefField.Volume), Is.True);
            Assert.That(def.IsOverridden(SoundDefField.Pitch), Is.False);
            Assert.That(def.EffectiveVolume.Min, Is.EqualTo(0.7f));
            Assert.That(def.EffectivePitch.Min, Is.EqualTo(playback.Pitch.Min));

            def.Overrides = SoundDefField.None;
            routing.VolumeGroup = "ui";
            def.VolumeGroup = "sfx";
            def.RoutingProfile = routing;
            Assert.That(def.EffectiveVolumeGroup, Is.EqualTo("ui"));
            def.Overrides = SoundDefField.VolumeGroup;
            Assert.That(def.EffectiveVolumeGroup, Is.EqualTo("sfx"));

            def.Overrides = SoundDefField.None;
            concurrency.Priority = 10;
            def.Priority = 200;
            def.ConcurrencyProfile = concurrency;
            Assert.That(def.EffectivePriority, Is.EqualTo(10));
            def.Overrides = SoundDefField.Priority;
            Assert.That(def.EffectivePriority, Is.EqualTo(200));

            def.Overrides = SoundDefField.None;
            spatial.Spatial = SpatialMode.Positional;
            def.Spatial = SpatialMode.None;
            def.SpatialProfile = spatial;
            Assert.That(def.EffectiveSpatial, Is.EqualTo(SpatialMode.Positional));
            def.Overrides = SoundDefField.Spatial;
            Assert.That(def.EffectiveSpatial, Is.EqualTo(SpatialMode.None));
        }

        [Test]
        public void EffectiveAcoustics_FollowsAcousticsOverride()
        {
            var shared = spatial.Acoustics.Local;
            shared.MaxDistance = 50f;
            spatial.Acoustics.Local = shared;
            var local = def.Acoustics.Local;
            local.MaxDistance = 18f;
            def.Acoustics.Local = local;
            def.SpatialProfile = spatial;
            Assert.That(def.EffectiveAcoustics.MaxDistance, Is.EqualTo(50f));
            def.Overrides = SoundDefField.Acoustics;
            Assert.That(def.EffectiveAcoustics.MaxDistance, Is.EqualTo(18f));
        }

        [Test]
        public void ProfileSwap_KeepsOverriddenFields()
        {
            def.Volume = new FloatRange(0.7f, 0.7f);
            def.PlaybackProfile = playback;
            def.Overrides = SoundDefField.Volume;
            var other = ScriptableObject.CreateInstance<SoundPlaybackProfile>();
            try
            {
                other.Pitch = new FloatRange(2f, 2f);
                def.PlaybackProfile = other;
                Assert.That(def.EffectiveVolume.Min, Is.EqualTo(0.7f));
                Assert.That(def.EffectivePitch.Min, Is.EqualTo(2f));
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test]
        public void NoProfile_IgnoresOverrideFlags()
        {
            def.Volume = new FloatRange(0.7f, 0.7f);
            def.Overrides = SoundDefField.Volume;
            Assert.That(def.EffectiveVolume.Min, Is.EqualTo(0.7f));
        }

        [Test]
        public void Effective_DoesNotAllocate()
        {
            def.PlaybackProfile = playback;
            def.RoutingProfile = routing;
            def.ConcurrencyProfile = concurrency;
            def.SpatialProfile = spatial;
            def.Overrides = SoundDefField.Volume | SoundDefField.Priority | SoundDefField.Acoustics;
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            TestDelegate read = () =>
            {
                float sum = 0f;
                object sink = null;
                for (int i = 0; i < 1000; i++)
                {
                    sum += def.EffectiveVolume.Min + def.EffectivePitch.Min + def.EffectiveCooldown
                        + def.EffectiveOcclusionVolumeAtFull + def.EffectiveAcoustics.MaxDistance
                        + def.EffectiveMaxInstances + def.EffectivePriority + (int)def.EffectiveSpatial;
                    if (def.EffectiveLoop || def.EffectiveOcclusion) sum += 1f;
                    sink = def.EffectiveMixerGroup;
                    sink = def.EffectiveVolumeGroup;
                }
                if (sum == float.NaN && sink != null) throw new System.InvalidOperationException();
            };
            read();
            Assert.That(read, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }
}
