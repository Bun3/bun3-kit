using System.Collections;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemMusicAsyncTests
    {
        [UnityTest]
        public IEnumerator PlayMusicAsync_CompletesWhenFadeInEnds() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var started = Time.realtimeSinceStartup;
            await sys.PlayMusicAsync(SoundSystemMusicTests.Def(withIntro: false), fade: 0.2f);
            Assert.That(Time.realtimeSinceStartup - started, Is.GreaterThanOrEqualTo(0.15f));
            Assert.IsTrue(sys.IsMusicPlaying);
        });

        [UnityTest]
        public IEnumerator PlayMusicAsync_ZeroFade_CompletesImmediately() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            await sys.PlayMusicAsync(SoundSystemMusicTests.Def(withIntro: false), fade: 0f);
            Assert.IsTrue(sys.IsMusicPlaying);
        });

        [UnityTest]
        public IEnumerator ReplacedTrack_AwaiterCompletesNormally() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var waiting = sys.PlayMusicAsync(SoundSystemMusicTests.Def(withIntro: false), fade: 5f);
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false), fade: 0f); // instant replace
            await waiting; // must complete (normally), not hang
        });

        [UnityTest]
        public IEnumerator StopMusicAsync_CompletesAfterFadeOut() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false));
            await sys.StopMusicAsync(0.1f);
            Assert.IsFalse(sys.IsMusicPlaying);
        });

        [UnityTest]
        public IEnumerator PlayMusicAsync_RejectedDef_WhileFadingIn_CompletesImmediately() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            sys.PlayMusic(SoundSystemMusicTests.Def(withIntro: false), fade: 5f); // active channel mid-fade
            var bad = ScriptableObject.CreateInstance<MusicDef>();                 // no Loop clip
            LogAssert.Expect(LogType.Warning, "SoundSystem.PlayMusic: def has no loop clip; ignored.");
            var task = sys.PlayMusicAsync(bad);
            Assert.IsTrue(task.Status.IsCompleted(), "rejected def must complete immediately, not wait on the active fade");
            await task;
        });
    }
}
