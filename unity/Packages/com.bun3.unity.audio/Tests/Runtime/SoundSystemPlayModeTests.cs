using System.Collections;
using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemPlayModeTests
    {
        private static SoundDef ShortClipDef()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("test-clip", 4410, 1, 44100, false) }; // 0.1 s
            return def;
        }

        [UnityTest]
        public IEnumerator Play_CompletesAndHandleGoesInvalid()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 4 });
            var handle = sys.Play(ShortClipDef());
            Assert.IsTrue(handle.IsValid);
            Assert.IsTrue(handle.IsPlaying);
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(handle.IsPlaying);
            Assert.IsFalse(handle.IsValid);
        }

        [UnityTest]
        public IEnumerator StaleHandle_AllCallsAreNoOps()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var first = sys.Play(ShortClipDef());
            var second = sys.Play(ShortClipDef()); // steals slot 0
            Assert.IsFalse(first.IsValid);
            first.Stop();          // must not throw, must not affect `second`
            first.SetVolume(0f);
            Assert.IsTrue(second.IsPlaying);
            yield break;
        }

        [UnityTest]
        public IEnumerator Dispose_DestroysPoolAndUnregistersTick()
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var handle = sys.Play(ShortClipDef());
            sys.Dispose();
            Assert.IsFalse(handle.IsValid);
            yield return null; // Object.Destroy defers actual destruction to end of frame.
            Assert.That(Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None), Is.Empty);
        }
    }
}
