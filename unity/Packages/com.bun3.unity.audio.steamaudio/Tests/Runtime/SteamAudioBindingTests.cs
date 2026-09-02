using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using SteamAudioSourceComponent = global::SteamAudio.SteamAudioSource;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioBindingTests
    {
        private static SoundDef Def(SpatialMode spatial, bool occlusion)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("sa", 44100, 1, 44100, false) };
            def.Loop = true;
            def.Spatial = spatial;
            def.Occlusion = occlusion;
            return def;
        }

        [UnityTest]
        public IEnumerator Occluded3D_GetsSteamAudioSource_NoCoreLpf()
        {
            using var sys = new SoundSystem(SteamAudioSoundSetup.Apply(new SoundSystemConfig { SfxVoices = 2 }));
            sys.Play(Def(SpatialMode.Positional, occlusion: true), new Vector3(0f, 0f, 3f));
            var source = sys.SourceForTest(0);
            Assert.IsTrue(source.spatialize);
            Assert.IsTrue(source.TryGetComponent<SteamAudioSourceComponent>(out var sas));
            Assert.IsTrue(sas.enabled);
            Assert.IsTrue(sas.occlusion);
            Assert.IsNull(source.GetComponent<AudioLowPassFilter>(), "core LPF must not exist under adapter");
            yield break;
        }

        [UnityTest]
        public IEnumerator TwoD_DisablesSpatializeAndSteamSource()
        {
            using var sys = new SoundSystem(SteamAudioSoundSetup.Apply(new SoundSystemConfig { SfxVoices = 2 }));
            sys.Play(Def(SpatialMode.None, occlusion: false));
            var source = sys.SourceForTest(0);
            Assert.IsFalse(source.spatialize);
            if (source.TryGetComponent<SteamAudioSourceComponent>(out var sas))
            {
                Assert.IsFalse(sas.enabled);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator TwoDFirst_DoesNotAttachComponent()
        {
            using var sys = new SoundSystem(SteamAudioSoundSetup.Apply(new SoundSystemConfig { SfxVoices = 1 }));
            sys.Play(Def(SpatialMode.None, occlusion: false));
            Assert.IsFalse(sys.SourceForTest(0).TryGetComponent<SteamAudioSourceComponent>(out _),
                "2D-only playback must not create Steam Audio native sources");
            yield break;
        }

        [UnityTest]
        public IEnumerator SlotReuse_DoesNotDuplicateComponent()
        {
            using var sys = new SoundSystem(SteamAudioSoundSetup.Apply(new SoundSystemConfig { SfxVoices = 1 }));
            sys.Play(Def(SpatialMode.Positional, occlusion: true), Vector3.forward);
            sys.Play(Def(SpatialMode.Positional, occlusion: true), Vector3.forward); // steals slot 0
            var source = sys.SourceForTest(0);
            Assert.That(source.GetComponents<SteamAudioSourceComponent>(), Has.Length.EqualTo(1));
            yield break;
        }
    }
}
