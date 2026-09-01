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
            yield return new WaitForSecondsRealtime(0.3f);
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
            Assert.That(sys.MusicChannels[ch].FadeFactor, Is.EqualTo(0.5f).Within(0.01f));
            sys.TickMusic(0.6f);
            Assert.That(sys.MusicChannels[ch].State, Is.EqualTo(MusicState.Playing));
            yield break;
        }
    }
}
