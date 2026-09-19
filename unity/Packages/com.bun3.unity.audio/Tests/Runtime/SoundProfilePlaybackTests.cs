using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundProfilePlaybackTests
    {
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
