using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemCrossfadeTests
    {
        [UnityTest]
        public IEnumerator PlayMusic_WhilePlaying_CrossfadesOnOtherChannel()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            yield return null;
            var first = sys.ActiveMusic;
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false), fade: 1f);
            var second = sys.ActiveMusic;
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(sys.MusicChannels[first].State, Is.EqualTo(MusicState.FadingOut));
            Assert.That(sys.MusicChannels[second].State, Is.EqualTo(MusicState.FadingIn));
            sys.TickMusic(1.1f);
            Assert.That(sys.MusicChannels[first].State, Is.EqualTo(MusicState.Idle));
            Assert.That(sys.MusicChannels[second].State, Is.EqualTo(MusicState.Playing));
        }

        [UnityTest]
        public IEnumerator PlayMusic_DuringCrossfade_StealsFadingOutChannel()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var a = SoundSystemMusicTests.Def(withIntro: false);
            var b = SoundSystemMusicTests.Def(withIntro: false);
            var c = SoundSystemMusicTests.Def(withIntro: false);
            sys.PlayMusic(a);
            yield return null;
            sys.PlayMusic(b, fade: 1f);           // A fading out, B fading in
            var fadingOut = 1 - sys.ActiveMusic;   // A's channel
            sys.PlayMusic(c, fade: 1f);            // steal: C takes A's channel, B fades out
            Assert.That(sys.ActiveMusic, Is.EqualTo(fadingOut));
            Assert.That(sys.MusicChannels[sys.ActiveMusic].Def, Is.SameAs(c));
            Assert.That(sys.MusicChannels[1 - sys.ActiveMusic].State, Is.EqualTo(MusicState.FadingOut));
        }

        [UnityTest]
        public IEnumerator PlayMusic_ZeroFade_SwapsInstantly()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            yield return null;
            var first = sys.ActiveMusic;
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false), fade: 0f);
            Assert.That(sys.MusicChannels[first].State, Is.EqualTo(MusicState.Idle));
            Assert.IsTrue(sys.IsMusicPlaying);
            yield break;
        }
    }
}
