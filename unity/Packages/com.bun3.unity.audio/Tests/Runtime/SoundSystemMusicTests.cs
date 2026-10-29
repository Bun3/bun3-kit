using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemMusicTests
    {
        internal static MusicDef Def(bool withIntro, float defaultFade = 0f)
        {
            var def = ScriptableObject.CreateInstance<MusicDef>();
            if (withIntro)
            {
                def.Intro = AudioClip.Create("intro", 4410, 1, 44100, false); // 0.1 s
            }
            def.Loop = AudioClip.Create("loop", 4410, 1, 44100, false);
            def.DefaultFade = defaultFade;
            return def;
        }

        private static SoundDef SoundMusic(float volume = 1f)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("bgm", 4410, 1, 44100, false) };
            def.Volume = new FloatRange(volume, volume);
            def.VolumeGroup = "music";
            return def;
        }

        [UnityTest]
        public IEnumerator PlayMusic_SoundDef_LoopsOnMusicChannelWithLiveGroupGain()
        {
            var gain = 0.5f;
            string resolved = null;
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 1,
                GroupGain = group => { resolved = group; return gain; },
            });
            var def = SoundMusic(volume: 0.8f);
            sys.PlayMusic(def);
            var ch = sys.ActiveMusic;
            Assert.IsTrue(sys.IsMusicPlaying);
            Assert.That(sys.MusicChannels[ch].Sound, Is.SameAs(def));
            Assert.That(sys.MusicLoopSources[ch].clip, Is.SameAs(def.Clips[0]));
            Assert.IsTrue(sys.MusicLoopSources[ch].loop);
            Assert.That(sys.MusicLoopSources[ch].volume, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(resolved, Is.EqualTo("music"));
            Assert.That(sys.Table.Slots[0].State, Is.EqualTo(VoiceState.Idle), "music must not take a voice");
            gain = 0.25f;
            sys.TickMusic(0f);
            Assert.That(sys.MusicLoopSources[ch].volume, Is.EqualTo(0.2f).Within(0.001f));
            yield break;
        }

        [UnityTest]
        public IEnumerator PlayMusic_SoundDefRouting_IsRestoredForMusicDefOnSameChannel()
        {
            var config = new SoundSystemConfig { SfxVoices = 1 };
            using var sys = new SoundSystem(config);
            Assume.That(config.SfxGroup, Is.Not.Null);
            Assume.That(config.MusicGroup, Is.Not.Null);
            var def = SoundMusic();
            def.MixerGroup = config.SfxGroup;
            sys.PlayMusic(def);
            var soundChannel = sys.ActiveMusic;
            Assert.That(sys.MusicLoopSources[soundChannel].outputAudioMixerGroup, Is.SameAs(config.SfxGroup));
            sys.PlayMusic(Def(withIntro: false), fade: 0f);
            sys.PlayMusic(Def(withIntro: false), fade: 0f);
            Assert.That(sys.ActiveMusic, Is.EqualTo(soundChannel));
            Assert.That(sys.MusicChannels[soundChannel].Sound, Is.Null);
            Assert.That(sys.MusicLoopSources[soundChannel].outputAudioMixerGroup, Is.SameAs(config.MusicGroup));
            yield break;
        }

        [UnityTest]
        public IEnumerator Priority_AppliesToVoiceSource_AndMusicOutranksVoices()
        {
            var def = SoundMusic();
            def.Priority = 40;
            AudioSource configured = null;
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 1,
                OnVoiceConfigured = (source, _) => configured = source,
            });
            sys.Play(def);
            Assert.That(configured.priority, Is.EqualTo(40));
            for (var i = 0; i < 2; i++)
            {
                Assert.That(sys.MusicIntroSources[i].priority, Is.Zero);
                Assert.That(sys.MusicLoopSources[i].priority, Is.Zero);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator PlayMusic_WithIntro_HandsOffToLoop()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(Def(withIntro: true));
            Assert.IsTrue(sys.IsMusicPlaying);
            var ch = sys.ActiveMusic;
            Assert.IsTrue(sys.MusicChannels[ch].LoopScheduled);
            // 0.1 s intro + headroom + margin: by 0.5 s the loop source must be playing.
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(sys.MusicLoopSources[ch].isPlaying);
            Assert.IsFalse(sys.MusicIntroSources[ch].isPlaying);
            Assert.IsTrue(sys.IsMusicPlaying); // loop never completes on its own
        }

        [UnityTest]
        public IEnumerator PlayMusic_NoIntro_StartsOnLoop()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(Def(withIntro: false));
            var ch = sys.ActiveMusic;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.IsTrue(sys.MusicLoopSources[ch].isPlaying);
            Assert.IsFalse(sys.MusicIntroSources[ch].isPlaying);
        }

        [UnityTest]
        public IEnumerator StopMusic_WithFade_EndsTrack()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(Def(withIntro: false));
            yield return null;
            sys.StopMusic(fadeOut: 0.1f);
            // 10x the fade: real-time waits under full-suite batch load need generous
            // slack; the assertion is state-based so extra wait only costs wall-clock.
            yield return new WaitForSecondsRealtime(1.0f);
            Assert.IsFalse(sys.IsMusicPlaying);
            Assert.IsFalse(sys.MusicLoopSources[0].isPlaying);
        }

        [UnityTest]
        public IEnumerator PlayMusic_FadeIn_RampsFactor()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(Def(withIntro: false), fade: 1f);
            var ch = sys.ActiveMusic;
            sys.TickMusic(0.5f);
            Assert.That(sys.MusicChannels[ch].Fade.Factor, Is.EqualTo(0.5f).Within(0.01f));
            sys.TickMusic(0.6f);
            Assert.That(sys.MusicChannels[ch].State, Is.EqualTo(MusicState.Playing));
            yield break;
        }
    }
}
