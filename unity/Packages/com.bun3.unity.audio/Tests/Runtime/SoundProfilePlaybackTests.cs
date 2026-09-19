using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundProfilePlaybackTests
    {
        [TestCase(AudioRolloffMode.Logarithmic)]
        [TestCase(AudioRolloffMode.Linear)]
        [TestCase(AudioRolloffMode.Custom)]
        public void DistanceAttenuationDisabled_KeepsSpatialSourceAndRestoresPoolRolloff(AudioRolloffMode mode)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var spatial = ScriptableObject.CreateInstance<SoundSpatialProfile>();
            var clip = AudioClip.Create("attenuation-pool", 4410, 1, 44100, false);
            var originalCurve = AnimationCurve.Linear(0, 1, 1, 0.2f);
            try
            {
                def.Clips = new[] { clip };
                def.SpatialProfile = spatial;
                spatial.Spatial = SpatialMode.Positional;
                spatial.DistanceAttenuation = false;
                AudioSource configured = null;
                using var system = new SoundSystem(new SoundSystemConfig
                {
                    SfxVoices = 1,
                    OnSourceCreated = source =>
                    {
                        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, originalCurve);
                        source.rolloffMode = mode;
                    },
                    OnVoiceConfigured = (source, _) => configured = source,
                });
                var first = system.Play(def);
                Assert.That(first.IsValid, Is.True);
                Assert.That(configured.spatialBlend, Is.EqualTo(1f));
                Assert.That(configured.rolloffMode, Is.EqualTo(AudioRolloffMode.Custom));
                var flat = configured.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                Assert.That(flat.Evaluate(0), Is.EqualTo(1f));
                Assert.That(flat.Evaluate(0.5f), Is.EqualTo(1f));
                Assert.That(flat.Evaluate(1), Is.EqualTo(1f));
                first.Stop();

                spatial.DistanceAttenuation = true;
                var second = system.Play(def);
                Assert.That(second.IsValid, Is.True);
                Assert.That(configured.rolloffMode, Is.EqualTo(mode));
                if (mode == AudioRolloffMode.Custom)
                {
                    var restored = configured.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                    Assert.That(restored.Evaluate(0.5f), Is.EqualTo(0.6f).Within(0.001f));
                    Assert.That(restored.Evaluate(1), Is.EqualTo(0.2f).Within(0.001f));
                }
                second.Stop();
                Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                    UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                    "GC allocation recorder must detect a known allocation before measuring this path.");
                Assert.That(() =>
                {
                    for (int i = 0; i < 32; i++)
                    {
                        spatial.DistanceAttenuation = (i & 1) != 0;
                        system.Play(def).Stop();
                    }
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(spatial);
                Object.DestroyImmediate(clip);
            }
        }

        [UnityTest]
        public IEnumerator Playback_ConfiguresSourceAndGainFromSharedProfiles()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var playback = ScriptableObject.CreateInstance<SoundPlaybackProfile>();
            var routing = ScriptableObject.CreateInstance<SoundRoutingProfile>();
            var spatial = ScriptableObject.CreateInstance<SoundSpatialProfile>();
            var clip = AudioClip.Create("profile-playback", 4410, 1, 44100, false);
            try
            {
                var mixer = Resources.Load<AudioMixer>("Bun3DefaultAudioMixer");
                Assert.That(mixer, Is.Not.Null);
                routing.MixerGroup = mixer.FindMatchingGroups("Music")[0];
                routing.VolumeGroup = "shared";
                playback.Volume = new FloatRange(0.5f, 0.5f);
                playback.Pitch = new FloatRange(1.5f, 1.5f);
                playback.Loop = true;
                spatial.Spatial = SpatialMode.Positional;
                spatial.MinDistance = 2f;
                spatial.MaxDistance = 12f;
                def.Clips = new[] { clip };
                def.PlaybackProfile = playback;
                def.RoutingProfile = routing;
                def.SpatialProfile = spatial;
                string resolvedGroup = null;
                AudioSource configured = null;
                using var system = new SoundSystem(new SoundSystemConfig
                {
                    SfxVoices = 1,
                    GroupGain = group => { resolvedGroup = group; return 0.4f; },
                    OnVoiceConfigured = (source, _) => configured = source,
                });
                system.Play(def);
                Assert.That(configured, Is.Not.Null);
                Assert.That(configured.outputAudioMixerGroup, Is.SameAs(routing.MixerGroup));
                Assert.That(resolvedGroup, Is.EqualTo("shared"));
                Assert.That(configured.volume, Is.EqualTo(0.2f).Within(0.001f));
                Assert.That(configured.pitch, Is.EqualTo(1.5f).Within(0.001f));
                Assert.That(configured.loop, Is.True);
                Assert.That(configured.spatialBlend, Is.EqualTo(1f));
                Assert.That(configured.minDistance, Is.EqualTo(2f));
                Assert.That(configured.maxDistance, Is.EqualTo(12f));
            }
            finally
            {
                Object.DestroyImmediate(def); Object.DestroyImmediate(playback); Object.DestroyImmediate(routing);
                Object.DestroyImmediate(spatial); Object.DestroyImmediate(clip);
            }
            yield break;
        }
    }
}
