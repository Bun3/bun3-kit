using System.Collections;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemMusicPauseTests
    {
        [UnityTest]
        public IEnumerator PauseDuringIntro_CancelsLoopSchedule_ResumeReschedules()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            // 0.5 s intro so the pause lands safely inside it.
            var def = ScriptableObject.CreateInstance<MusicDef>();
            def.Intro = AudioClip.Create("intro", 22050, 1, 44100, false);
            def.Loop = AudioClip.Create("loop", 4410, 1, 44100, false);
            sys.PlayMusic(def);
            var ch = sys.ActiveMusic;
            yield return new WaitForSecondsRealtime(0.15f); // inside intro
            sys.PauseMusic();
            Assert.IsTrue(sys.IsMusicPaused);
            Assert.IsFalse(sys.MusicChannels[ch].LoopScheduled, "pause must cancel the pending loop schedule");
            yield return new WaitForSecondsRealtime(0.6f);  // long past original loop start
            Assert.IsFalse(sys.MusicLoopSources[ch].isPlaying, "loop must not fire while paused");
            sys.ResumeMusic();
            Assert.IsTrue(sys.MusicChannels[ch].LoopScheduled, "resume must reschedule the loop");
            yield return new WaitForSecondsRealtime(0.6f);  // remaining intro (~0.35 s) + margin
            Assert.IsTrue(sys.MusicLoopSources[ch].isPlaying, "loop must start after the remaining intro");
        }

        [UnityTest]
        public IEnumerator PauseDuringLoop_ResumeContinues()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            var ch = sys.ActiveMusic;
            yield return new WaitForSecondsRealtime(0.2f); // loop running
            sys.PauseMusic();
            yield return null;
            Assert.IsFalse(sys.MusicLoopSources[ch].isPlaying);
            sys.ResumeMusic();
            yield return null;
            Assert.IsTrue(sys.MusicLoopSources[ch].isPlaying);
            Assert.IsFalse(sys.IsMusicPaused);
        }

        [UnityTest]
        public IEnumerator Pause_FreezesFade()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false), fade: 1f);
            var ch = sys.ActiveMusic;
            sys.TickMusic(0.5f);
            sys.PauseMusic();
            var frozen = sys.MusicChannels[ch].Fade.Factor;
            sys.TickMusic(10f);
            Assert.That(sys.MusicChannels[ch].Fade.Factor, Is.EqualTo(frozen));
            yield break;
        }

        [UnityTest]
        public IEnumerator PauseImmediately_NoIntro_ResumeReschedulesLoop()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            var ch = sys.ActiveMusic;
            sys.PauseMusic(); // same frame: inside the schedule headroom → cancels the loop schedule
            Assert.IsFalse(sys.MusicChannels[ch].LoopScheduled);
            sys.ResumeMusic();
            Assert.IsTrue(sys.MusicChannels[ch].LoopScheduled, "no-intro track must reschedule its loop on resume");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsTrue(sys.MusicLoopSources[ch].isPlaying);
        }

        [UnityTest]
        public IEnumerator StopMusicAsync_WhilePaused_CompletesImmediately() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            sys.PauseMusic();
            await sys.StopMusicAsync(1f);   // paused → instant silence, must not hang
            Assert.IsFalse(sys.IsMusicPlaying);
            Assert.That(sys.MusicChannels[0].State, Is.EqualTo(MusicState.Idle));
        });
    }
}
