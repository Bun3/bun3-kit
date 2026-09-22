using Bun3.Unity.Audio;
using Bun3.Unity.Audio.SteamAudio;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioSetupTests
    {
        [Test]
        public void BinderUsesSharedOcclusionRatherThanRetainedLocalValue()
        {
            var go = new GameObject("Profile binding"); go.SetActive(false);
            var source = go.AddComponent<AudioSource>(); source.spatialBlend = 1;
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var profile = ScriptableObject.CreateInstance<SoundSpatialProfile>();
            try
            {
                def.Occlusion = false; profile.Occlusion = true; def.SpatialProfile = profile;
                var config = SteamAudioSoundSetup.Apply(new SoundSystemConfig());
                config.OnVoiceConfigured(source, def);
                Assert.That(go.GetComponent<global::SteamAudio.SteamAudioSource>().occlusion, Is.True);
                def.Occlusion = true; profile.Occlusion = false;
                config.OnVoiceConfigured(source, def);
                Assert.That(go.GetComponent<global::SteamAudio.SteamAudioSource>().occlusion, Is.False);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(def); Object.DestroyImmediate(profile); }
        }

        [Test]
        public void Apply_DisablesCoreOcclusion_AndRegistersBinder()
        {
            var config = new SoundSystemConfig();
            var returned = SteamAudioSoundSetup.Apply(config);
            Assert.That(returned, Is.SameAs(config));
            Assert.That(config.OcclusionChecksPerFrame, Is.EqualTo(0));
            Assert.IsNotNull(config.OnVoiceConfigured);
        }

        [Test]
        public void Apply_Twice_IsIdempotent()
        {
            var config = new SoundSystemConfig();
            SteamAudioSoundSetup.Apply(config);
            var once = config.OnVoiceConfigured;
            SteamAudioSoundSetup.Apply(config);
            Assert.That(config.OnVoiceConfigured, Is.SameAs(once), "binder must not be registered twice");
        }

        [Test]
        public void Apply_PreservesExistingHook_ByChaining()
        {
            var config = new SoundSystemConfig();
            var called = false;
            config.OnVoiceConfigured = (_, _) => called = true;
            SteamAudioSoundSetup.Apply(config);
            config.OnVoiceConfigured(null, null); // chained delegate must still call the original
            Assert.IsTrue(called);
        }
    }
}
