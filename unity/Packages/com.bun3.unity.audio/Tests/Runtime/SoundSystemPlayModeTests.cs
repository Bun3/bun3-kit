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
            yield return new WaitForSeconds(0.3f);
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
            Assert.IsTrue(second.IsValid);
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

        [UnityTest]
        public IEnumerator Play_WithFadeIn_RampsVolume()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var loopDef = ScriptableObject.CreateInstance<SoundDef>();
            loopDef.Clips = new[] { AudioClip.Create("loop-clip", 44100, 1, 44100, false) };
            loopDef.Loop = true;
            var handle = sys.Play(loopDef, fadeIn: 1f);
            sys.Tick(0.5f);
            Assert.That(sys.Table.Slots[handle.SlotIndex].Fade.Factor, Is.EqualTo(0.5f).Within(0.001f));
            yield break;
        }

        [UnityTest]
        public IEnumerator SetCompletionCallback_FiresOnceOnNaturalEnd_WithStaleHandle()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var handle = sys.Play(ShortClipDef());
            var callCount = 0;
            var received = SoundHandle.Invalid;
            sys.SetCompletionCallback(handle, h =>
            {
                callCount++;
                received = h;
            });
            sys.Tick(0.2f); // past the 0.1 s clip
            Assert.That(callCount, Is.EqualTo(1));
            Assert.IsFalse(received.IsValid);
            yield break;
        }

        [UnityTest]
        public IEnumerator SetCompletionCallback_FiresOnSteal()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var first = sys.Play(ShortClipDef());
            var callCount = 0;
            sys.SetCompletionCallback(first, _ => callCount++);
            sys.Play(ShortClipDef()); // steals the only slot
            Assert.That(callCount, Is.EqualTo(1));
            yield break;
        }

        [UnityTest]
        public IEnumerator CallbackDisposingSystem_DoesNotCorruptSignalBatch()
        {
            var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var otherEnded = false;
            var h1 = sys.Play(ShortClipDef());
            var h2 = sys.Play(ShortClipDef());
            sys.SetCompletionCallback(h1, _ => sys.Dispose()); // re-entrant dispose from signal phase
            sys.SetCompletionCallback(h2, _ => otherEnded = true);
            sys.Tick(1f); // both complete same tick
            Assert.IsTrue(otherEnded, "second voice's callback must still fire");
            yield break;
        }
    }
}
