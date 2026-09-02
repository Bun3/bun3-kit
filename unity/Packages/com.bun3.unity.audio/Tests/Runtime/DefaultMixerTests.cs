using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class DefaultMixerTests
    {
        [Test]
        public void BundledMixer_LoadsWithGroupsSnapshotsAndParams()
        {
            var mixer = Resources.Load<AudioMixer>("Bun3DefaultAudioMixer");
            Assert.IsNotNull(mixer, "bundled mixer asset must load from package Resources");
            Assert.That(mixer.FindMatchingGroups("Music"), Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(mixer.FindMatchingGroups("SFX"), Has.Length.GreaterThanOrEqualTo(1));
            Assert.That(mixer.FindMatchingGroups("Voice"), Has.Length.GreaterThanOrEqualTo(1));
            Assert.IsNotNull(mixer.FindSnapshot("Normal"));
            Assert.IsNotNull(mixer.FindSnapshot("Paused"));
            Assert.IsTrue(mixer.GetFloat("MasterVolume", out _), "MasterVolume must be exposed");
            Assert.IsTrue(mixer.GetFloat("MusicVolume", out _));
            Assert.IsTrue(mixer.GetFloat("SfxVolume", out _));
            Assert.IsTrue(mixer.GetFloat("VoiceVolume", out _));
        }

        [Test]
        public void NullMixerConfig_FallsBackToBundled_ChannelVolumeRoundTrips()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.SetChannelVolume(SoundChannel.Sfx, 0.5f);
            Assert.That(sys.GetChannelVolume(SoundChannel.Sfx), Is.EqualTo(0.5f).Within(0.01f));
        }
    }
}
