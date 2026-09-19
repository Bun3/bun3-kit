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
        public void PlaybackClips_ExposeRuntimePrecedenceAndAuthoredFallback()
        {
            var property = typeof(SoundDef).GetProperty("PlaybackClips");
            Assert.That(property, Is.Not.Null, "Cold-path preparation must inspect the clips actually used for playback.");
            Assert.That(property.PropertyType, Is.EqualTo(typeof(System.Collections.Generic.IReadOnlyList<AudioClip>)));
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var authored = AudioClip.Create("authored", 10, 1, 44100, false);
            var loaded = AudioClip.Create("loaded", 10, 1, 44100, false);
            try
            {
                Assert.That(property.GetValue(def), Is.Null);
                def.Clips = new[] { authored };
                def.RuntimeClips = new[] { loaded };
                Assert.That(((System.Collections.Generic.IReadOnlyList<AudioClip>)property.GetValue(def))[0], Is.SameAs(loaded));
                def.RuntimeClips = null;
                Assert.That(((System.Collections.Generic.IReadOnlyList<AudioClip>)property.GetValue(def))[0], Is.SameAs(authored));
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(authored);
                Object.DestroyImmediate(loaded);
            }
        }

        [Test]
        public void PreparedDirectDefinition_FirstCooldownPlaybackDoesNotAllocate()
        {
            var method = typeof(SoundSystem).GetMethod("Prepare", new[] { typeof(SoundDef) });
            Assert.That(method, Is.Not.Null, "Direct definitions need an explicit cold-path preparation API.");
            using var system = new SoundSystem(new SoundSystemConfig { SfxVoices = 1 });
            var warm = ScriptableObject.CreateInstance<SoundDef>();
            var direct = ScriptableObject.CreateInstance<SoundDef>();
            var clip = AudioClip.Create("prepared-direct", 4410, 1, 44100, false);
            try
            {
                warm.Clips = new[] { clip };
                system.Play(warm).Stop();
                system.Tick(.1f);
                direct.Clips = new[] { clip };
                direct.Cooldown = 1f;
                method.Invoke(system, new object[] { direct });
                SoundHandle first = default;
                Assert.That(() => System.GC.KeepAlive(new byte[1024]), Is.AllocatingGCMemory(),
                    "GC allocation recorder must detect a known allocation before measuring this path.");
                Assert.That(() => { first = system.Play(direct); }, Is.Not.AllocatingGCMemory());
                Assert.That(first.IsValid, Is.True);
                Assert.That(system.Play(direct).IsValid, Is.False, "Preparation must preserve cooldown enforcement.");
            }
            finally
            {
                Object.DestroyImmediate(warm);
                Object.DestroyImmediate(direct);
                Object.DestroyImmediate(clip);
            }
        }

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

            Assert.That(() => System.GC.KeepAlive(new byte[1024]), Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
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

            Assert.That(() => System.GC.KeepAlive(new byte[1024]), Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                sys.TickMusic(0.1f);          // mid-fade tick
                sys.TickMusic(1f);            // fade completes (no awaiter → no signal)
                sys.TickMusic(0.1f);          // steady-state tick
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void OcclusionTickPath_DoesNotAllocate()
        {
            var listenerGo = new GameObject("alloc-listener");
            listenerGo.AddComponent<AudioListener>();
            using var sys = new SoundSystem(new SoundSystemConfig
            {
                SfxVoices = 4,
                Listener = listenerGo.transform,
                OcclusionChecksPerFrame = 4,
            });
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { AudioClip.Create("occ-alloc", 44100, 1, 44100, false) };
            def.Loop = true;
            def.Occlusion = true;
            sys.Play(def, new Vector3(0f, 0f, 5f));
            sys.Tick(0.02f); // warm all paths (listener cached, provider constructed)

            Assert.That(() => System.GC.KeepAlive(new byte[1024]), Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                sys.Tick(0.02f);
                sys.Tick(0.02f);
            }, Is.Not.AllocatingGCMemory());
            Object.DestroyImmediate(listenerGo);
        }
    }
}
