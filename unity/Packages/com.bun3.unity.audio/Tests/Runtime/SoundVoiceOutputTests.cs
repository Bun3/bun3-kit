using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundVoiceOutputTests
    {
        private sealed class FakeOutput : ISoundVoiceOutput
        {
            internal readonly AudioSource Source;
            internal AudioClip Driver;
            internal VoiceOutputStartResult Result = VoiceOutputStartResult.Started;
            internal bool Complete, Busy, Active;
            internal int Starts, Retires, Disposals, PitchCalls;
            internal float Pitch;
            internal AudioClip RetiredClip;
            public bool IsComplete => Complete;
            internal FakeOutput(AudioSource source, AudioClip driver) { Source = source; Driver = driver; }
            public VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch)
            {
                Starts++;
                if (Busy) return VoiceOutputStartResult.Unavailable;
                if (Result != VoiceOutputStartResult.Started) return Result;
                Active = true; Complete = false; Pitch = logicalPitch;
                Source.clip = Driver; Source.loop = true; Source.pitch = 1; Source.spatialBlend = 0;
                return VoiceOutputStartResult.Started;
            }
            public void SetPitch(float logicalPitch) { Pitch = logicalPitch; PitchCalls++; }
            public void Retire()
            {
                if (!Active) return;
                RetiredClip = Source.clip; Active = false; Retires++;
            }
            public void Dispose() { Retire(); Disposals++; }
        }

        private sealed class Occlusion : IOcclusionProvider
        {
            internal int Calls;
            public float Evaluate(in Vector3 listener, in Vector3 source) { Calls++; return 1; }
        }

        private SoundDef _def;
        private AudioClip _clip, _driver;

        [SetUp]
        public void Setup()
        {
            _def = ScriptableObject.CreateInstance<SoundDef>();
            _clip = AudioClip.Create("Output input", 4800, 1, 48000, false);
            _driver = AudioClip.Create("Output driver", 480, 2, 48000, false);
            _def.Clips = new[] { _clip };
            _def.Spatial = SpatialMode.Positional;
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(_def); Object.DestroyImmediate(_clip); Object.DestroyImmediate(_driver);
        }

        private static void Factory(SoundSystemConfig config, Func<AudioSource, ISoundVoiceOutput> factory)
        {
            var field = typeof(SoundSystemConfig).GetField("CreateVoiceOutput");
            Assert.That(field, Is.Not.Null, "Missing prewarmed output-owner factory.");
            field.SetValue(config, factory);
        }

        private SoundSystem Create(out FakeOutput backend, SoundSystemConfig config = null)
        {
            config ??= new SoundSystemConfig { SfxVoices = 1, OcclusionChecksPerFrame = 0 };
            FakeOutput created = null;
            Factory(config, source => created = new FakeOutput(source, _driver));
            var system = new SoundSystem(config);
            backend = created;
            return system;
        }

        [UnityTest]
        public IEnumerator FactoryRunsOncePerSfxSourceAfterPreparation()
        {
            var config = new SoundSystemConfig { SfxVoices = 2, OcclusionChecksPerFrame = 0 };
            var prepared = 0; var factories = 0;
            config.OnSourceCreated = _ => prepared++;
            Factory(config, source => { Assert.That(prepared, Is.EqualTo(++factories)); return new FakeOutput(source, _driver); });
            using (var system = new SoundSystem(config))
            {
                system.Play(_def); system.Play(_def); system.Play(_def);
                Assert.That(factories, Is.EqualTo(2));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OwnedVoiceWaitsForOutputDrainInsteadOfClipClock()
        {
            using (var system = Create(out var output))
            {
                var handle = system.Play(_def);
                var completions = 0;
                system.SetCompletionCallback(handle, _ => completions++);
                system.Tick(10);
                Assert.That(handle.IsValid, Is.True);
                Assert.That(output.Source.clip, Is.SameAs(_driver));
                output.Complete = true;
                system.Tick(.01f);
                Assert.That(handle.IsValid, Is.False);
                Assert.That(output.Retires, Is.EqualTo(1));
                Assert.That(completions, Is.EqualTo(1));
                system.Tick(.01f);
                Assert.That(completions, Is.EqualTo(1));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnsupportedRetainsOrdinaryPlaybackAndClock()
        {
            using (var system = Create(out var output))
            {
                output.Result = VoiceOutputStartResult.Unsupported;
                var handle = system.Play(_def);
                Assert.That(handle.IsValid, Is.True);
                Assert.That(output.Source.clip, Is.SameAs(_clip));
                system.Tick(1);
                Assert.That(handle.IsValid, Is.False);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnavailableDropsHandledRequestWithoutDryFallback()
        {
            using (var system = Create(out var output))
            {
                output.Result = VoiceOutputStartResult.Unavailable;
                Assert.That(system.Play(_def).IsValid, Is.False);
                Assert.That(output.Source.isPlaying, Is.False);
                Assert.That(output.Source.clip, Is.Null);
                output.Result = VoiceOutputStartResult.Started;
                Assert.That(system.Play(_def).IsValid, Is.True);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator StealRetiresBeforeReconfigurationAndSignalsOldGeneration()
        {
            using (var system = Create(out var output))
            {
                var old = system.Play(_def);
                var callbacks = 0; var callbackHandle = default(SoundHandle);
                system.SetCompletionCallback(old, h => { callbacks++; callbackHandle = h; });
                var replacement = system.Play(_def);
                Assert.That(output.RetiredClip, Is.SameAs(_driver));
                Assert.That(old.IsValid, Is.False);
                Assert.That(replacement.IsValid, Is.True);
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(callbackHandle.IsValid, Is.False);
                old.Stop();
                system.Tick(.01f);
                Assert.That(replacement.IsValid, Is.True);
                Assert.That(output.Retires, Is.EqualTo(1));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BusyRetirementCannotResetOrFallBackAndStillCompletesStolenVoice()
        {
            using (var system = Create(out var output))
            {
                var old = system.Play(_def);
                var callbacks = 0;
                system.SetCompletionCallback(old, _ => callbacks++);
                output.Busy = true;
                Assert.That(system.Play(_def).IsValid, Is.False);
                Assert.That(callbacks, Is.EqualTo(1));
                Assert.That(output.Retires, Is.EqualTo(1));
                Assert.That(output.Source.clip, Is.Null);
                output.Busy = false;
                Assert.That(system.Play(_def).IsValid, Is.True);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator StopAndFadeRetireAtCompletionWhilePitchZeroRemainsActive()
        {
            using (var system = Create(out var output))
            {
                var handle = system.Play(_def);
                handle.SetPitch(0);
                Assert.That(output.Pitch, Is.Zero);
                Assert.That(output.Source.pitch, Is.EqualTo(1));
                system.Tick(10);
                Assert.That(handle.IsValid, Is.True);
                handle.Stop(1);
                system.Tick(.5f);
                Assert.That(handle.IsValid, Is.True);
                Assert.That(output.Retires, Is.Zero);
                system.Tick(.6f);
                Assert.That(handle.IsValid, Is.False);
                Assert.That(output.Retires, Is.EqualTo(1));
                var next = system.Play(_def);
                next.Stop(); system.Tick(.01f);
                Assert.That(next.IsValid, Is.False);
                Assert.That(output.Retires, Is.EqualTo(2));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OwnedSpatialProcessingBypassesBuiltinOcclusionButKeepsCoreGain()
        {
            var listener = new GameObject("Output listener");
            var occlusion = new Occlusion();
            var config = new SoundSystemConfig { SfxVoices = 1, Listener = listener.transform, OcclusionProvider = occlusion };
            try
            {
                using var system = Create(out var output, config);
                _def.Occlusion = true;
                var handle = system.Play(_def);
                handle.SetVolume(.25f);
                system.Tick(1);
                Assert.That(occlusion.Calls, Is.Zero);
                Assert.That(output.Source.volume, Is.EqualTo(system.Table.CurrentVolume(0)).Within(.00001f));
                Assert.That(output.Source.GetComponent<AudioLowPassFilter>().enabled, Is.False);
            }
            finally { Object.DestroyImmediate(listener); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisposeRetiresOwnedOutputAndDisposesEachPreparedBackendOnce()
        {
            var system = Create(out var output);
            system.Play(_def);
            system.Dispose(); system.Dispose();
            Assert.That(output.Retires, Is.EqualTo(1));
            Assert.That(output.Disposals, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FactoryFailureDisposesPreviouslyPreparedBackend()
        {
            var config = new SoundSystemConfig { SfxVoices = 2, OcclusionChecksPerFrame = 0 };
            FakeOutput first = null;
            Factory(config, source => first == null ? first = new FakeOutput(source, _driver) :
                throw new InvalidOperationException("Expected factory failure"));
            Assert.Throws<InvalidOperationException>(() => new SoundSystem(config));
            Assert.That(first.Disposals, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator WarmOwnedPlayPitchTickAndStopDoNotAllocate()
        {
            using (var system = Create(out _))
            {
                for (var i = 0; i < 8; i++)
                {
                    var handle = system.Play(_def); handle.SetPitch(.5f); system.Tick(.01f); handle.Stop(); system.Tick(.01f);
                }
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 64; i++)
                {
                    var handle = system.Play(_def); handle.SetPitch(.5f); system.Tick(.01f); handle.Stop(); system.Tick(.01f);
                }
                Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            }
            yield return null;
        }
    }
}
