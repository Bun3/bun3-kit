using Bun3.Unity.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class AllocationTests
    {
        [Test]
        public void PlayAndTick_DoNotAllocate()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 8 });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("test-clip", 4410, 1, 44100, false) };
            def.Cooldown = 0f;

            // Warm every lazy path once (def.Cooldown is 0 here, so the cooldown dictionary
            // itself is never touched — see VoiceTable.TryAllocate).
            sys.Play(def).Stop();
            sys.Tick(0.02f);

            Assert.That(() =>
            {
                var handle = sys.Play(def);
                sys.Tick(0.02f);
                handle.Stop(0.1f);
                sys.Tick(0.2f);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void MusicTick_DoesNotAllocate()
        {
            using var sys = new SoundSystem(new SoundSystemConfig { SfxVoices = 2 });
            var def = ScriptableObject.CreateInstance<MusicDef>();
            def.Loop = AudioClip.Create("loop", 4410, 1, 44100, false);
            sys.PlayMusic(def, fade: 0.5f);   // warm: channel active, fading
            sys.TickMusic(0.1f);

            Assert.That(() =>
            {
                sys.TickMusic(0.1f);          // mid-fade tick
                sys.TickMusic(1f);            // fade completes (no awaiter → no signal)
                sys.TickMusic(0.1f);          // steady-state tick
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
