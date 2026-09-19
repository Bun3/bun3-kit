using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemReentrancyTests
    {
        [UnityTest]
        public IEnumerator DisposeSilencesPlayingSourcesBeforeDeferredObjectDestruction()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var clip = AudioClip.Create("Immediate silence", 44100, 1, 44100, false);
            def.Clips = new[] { clip };
            def.Loop = true;
            var system = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            try
            {
                var handle = system.Play(def);
                var source = system.SourceForTest(handle.SlotIndex);
                Assert.That(source.isPlaying, Is.True);
                system.Dispose();
                Assert.That(source.isPlaying, Is.False, "Dispose must silence sources before notifying callbacks.");
            }
            finally
            {
                system.Dispose();
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(clip);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConfigurationHookDisposesSystem_SourceCannotStartAfterDisposal()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var clip = AudioClip.Create("Disposed hook", 44100, 1, 44100, false);
            def.Clips = new[] { clip };
            def.Loop = true;
            var config = new SoundSystemConfig { SfxVoices = 1 };
            var system = new SoundSystem(config);
            AudioSource configured = null;
            config.OnVoiceConfigured = (source, _) => { configured = source; system.Dispose(); };
            try
            {
                var handle = system.Play(def);
                Assert.That(handle.IsValid, Is.False);
                Assert.That(configured.isPlaying, Is.False, "A disposed system must not start its pending source.");
            }
            finally
            {
                system.Dispose();
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(clip);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ConfigurationHookReusesSlot_OuterPlayDoesNotRestartReplacementSource()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var clip = AudioClip.Create("Reused hook", 44100, 1, 44100, false);
            def.Clips = new[] { clip };
            def.Loop = true;
            var config = new SoundSystemConfig { SfxVoices = 1 };
            using var system = new SoundSystem(config);
            var replacement = SoundHandle.Invalid;
            config.OnVoiceConfigured = (source, _) =>
            {
                config.OnVoiceConfigured = null;
                replacement = system.Play(def);
                source.Pause();
            };
            try
            {
                var outer = system.Play(def);
                Assert.That(outer.IsValid, Is.False);
                Assert.That(replacement.IsValid, Is.True);
                Assert.That(system.SourceForTest(replacement.SlotIndex).isPlaying, Is.False,
                    "The outer call must not resume the replacement voice paused by its callback.");
            }
            finally
            {
                system.Dispose();
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(clip);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator StolenCallbackReusesSlot_OuterPlayHandleCannotOwnCallbackVoice()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var clip = AudioClip.Create("Reentrant", 44100, 1, 44100, false);
            def.Clips = new[] { clip };
            def.Loop = true;
            try
            {
                using var system = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
                var original = system.Play(def);
                var callbackVoice = SoundHandle.Invalid;
                system.SetCompletionCallback(original, _ => callbackVoice = system.Play(def));
                var interrupted = system.Play(def);
                Assert.That(callbackVoice.IsValid, Is.True);
                Assert.That(interrupted.IsValid, Is.False,
                    "The callback replaced this call's voice before Play returned.");
                interrupted.SetVolume(0f);
                Assert.That(system.Table.Slots[callbackVoice.SlotIndex].VolumeScale, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(clip);
            }
            yield return null;
        }
    }
}
