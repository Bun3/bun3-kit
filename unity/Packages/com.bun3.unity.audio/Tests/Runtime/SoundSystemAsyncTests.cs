using System.Collections;
using Bun3.Unity.Audio;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemAsyncTests
    {
        private static SoundDef ShortClipDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("test-clip", 4410, 1, 44100, false) }; // 0.1 s
            return def;
        }

        [UnityTest]
        public IEnumerator PlayAsync_CompletesWhenClipEnds() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var started = Time.time;
            await sys.PlayAsync(ShortClipDef());
            Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(0.09f));
        });

        [UnityTest]
        public IEnumerator WaitAsync_OnInvalidHandle_CompletesImmediately() => UniTask.ToCoroutine(async () =>
        {
            await SoundHandle.Invalid.WaitAsync();
        });

        [UnityTest]
        public IEnumerator WaitAsync_CompletesOnSteal() => UniTask.ToCoroutine(async () =>
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var first = sys.Play(ShortClipDef());
            var waiting = first.WaitAsync();
            sys.Play(ShortClipDef()); // steals the only slot
            await waiting;            // must complete, not hang
        });
    }
}
