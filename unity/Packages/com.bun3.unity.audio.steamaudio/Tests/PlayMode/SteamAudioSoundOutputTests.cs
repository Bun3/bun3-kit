using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using System.Collections;
using System.Reflection;
using Bun3.Unity.Acoustics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class NativeSfxTap : MonoBehaviour
    {
        private float _peak;
        private long _frames;
        public float Peak => System.Threading.Volatile.Read(ref _peak);
        public long Frames => System.Threading.Interlocked.Read(ref _frames);
        private void OnAudioFilterRead(float[] samples, int channels)
        {
            float peak = _peak;
            for (int i = 0; i < samples.Length; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
            System.Threading.Volatile.Write(ref _peak, peak);
            if (channels > 0) System.Threading.Interlocked.Add(ref _frames, samples.Length / channels);
        }
    }

    public sealed class SteamAudioSoundOutputTests
    {
        static readonly float[] Coefficients = { .2820948f, .4886025f, 0, 0 };
        static PathRenderSettings Settings => new(new SA.CoordinateSpace3
        {
            right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
        });

        private sealed class WorldSettings : PlanarAcousticSettings
        {
            public override int SourceCapacity => 1;
            public override float OcclusionRadius => 0;
            public override int OcclusionSamples => 1;
            public override float ObstructedPathGain => 1;
            public override float MonoDistance => 1;
            public override float FullSpatialDistance => 3;
        }

        static AudioClip Clip(int frames = 4800, int channels = 2)
        {
            var clip = AudioClip.Create("Native SFX prepared test", frames, channels, 48000, false);
            var data = new float[frames * channels];
            for (int frame = 0; frame < frames; frame++)
                for (int channel = 0; channel < channels; channel++) data[frame * channels + channel] = .2f * (float)Math.Sin(frame * .09);
            clip.SetData(data, 0);
            return clip;
        }

        [UnityTest]
        public IEnumerator SoundSystemKeepsAShortProcessedVoiceUntilItsNativeOutputDrains()
        {
            var listener = new GameObject("Pooled native SFX listener");
            listener.AddComponent<AudioListener>();
            var clip = Clip(480, 1);
            var def = ScriptableObject.CreateInstance<SoundDef>();
            using var cache = new SteamAudioClipCache(10000);
            SoundSystem system = null;
            SteamAudioSoundOutput output = null;
            AudioSource source = null;
            NativeSfxTap tap = null;
            try
            {
                cache.PrepareClips(new[] { clip });
                def.Clips = new[] { clip };
                def.Spatial = SpatialMode.Positional;
                def.MinDistance = 3;
                system = new SoundSystem(new SoundSystemConfig
                {
                    SfxVoices = 1,
                    CreateVoiceOutput = created =>
                    {
                        source = created;
                        output = new SteamAudioSoundOutput(created, cache) { StartBlocked = false };
                        tap = created.gameObject.AddComponent<NativeSfxTap>();
                        return output;
                    }
                });
                var handle = system.Play(def);
                Assert.That(handle.IsValid, Is.True);
                var minimum = output.GetType().GetProperty("MinDistance");
                Assert.That(minimum, Is.Not.Null);
                Assert.That(minimum.GetValue(output), Is.EqualTo(3f));
                var original = output.CurrentParameters;
                Assert.That(original.TryPublish(Coefficients, Settings), Is.True);
                float deadline = Time.realtimeSinceStartup + 3;
                while (handle.IsValid && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(tap.Peak, Is.GreaterThan(1e-6f), "The pool's logical clip clock must not stop the native driver before downstream signal delivery.");
                Assert.That(handle.IsValid, Is.False);
                Assert.That(source.isPlaying, Is.False);
                Assert.That(original.IsValid, Is.False);
                def.Spatial = SpatialMode.None;
                var ui = system.Play(def);
                Assert.That(ui.IsValid, Is.True);
                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(output.CurrentParameters.IsValid, Is.False);
                ui.Stop();
            }
            finally
            {
                system?.Dispose(); UnityEngine.Object.Destroy(listener);
                UnityEngine.Object.Destroy(clip); UnityEngine.Object.Destroy(def);
            }
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator NativeStereoDriverPreservesActualUnitySourceGain()
        {
            var listener = new GameObject("Native SFX listener");
            listener.AddComponent<AudioListener>();
            var go = new GameObject("Native SFX source");
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var clip = Clip();
            var def = ScriptableObject.CreateInstance<SoundDef>();
            using var cache = new SteamAudioClipCache(100000);
            SteamAudioSoundOutput output = null;
            AudioMixer mixer = null;
            float masterBefore = 0, sfxBefore = 0;
            bool restoreMixer = false;
            try
            {
                cache.PrepareClips(new[] { clip });
                output = new SteamAudioSoundOutput(source, cache) { StartBlocked = false };
                def.Spatial = SpatialMode.Positional;
                def.Loop = true;
                def.MaxDistance = 40;
                Assert.That(output.TryStart(def, clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
                Assert.That(output.CurrentParameters.TryPublish(Coefficients, Settings), Is.True);
                Assert.That(output.MaxDistance, Is.EqualTo(40));
                var tap = go.AddComponent<NativeSfxTap>();
                source.volume = 1;
                source.Play();
                float deadline = Time.realtimeSinceStartup + 3;
                while (tap.Peak <= 1e-6f && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(tap.Peak, Is.GreaterThan(1e-6f), "Real source-filter callbacks must render prepared stereo clip PCM.");
                var samples = new float[1024];
                double full = 0, quarter = 0, silent = 0;
                yield return Measure(samples, value => full = value);
                source.volume = .25f;
                yield return Measure(samples, value => quarter = value);
                source.volume = 0;
                long mutedFrames = tap.Frames;
                yield return Measure(samples, value => silent = value);
                Assert.That(full, Is.GreaterThan(1e-5));
                Assert.That(quarter / full, Is.InRange(.20, .30), "The driver must preserve Unity source gain exactly once.");
                Assert.That(silent, Is.Zero);
                Assert.That(tap.Frames, Is.GreaterThan(mutedFrames), "Zero Unity gain must not strand native completion by suppressing driver callbacks.");
                mixer = Resources.Load<AudioMixer>("Bun3DefaultAudioMixer");
                Assert.That(mixer, Is.Not.Null);
                Assert.That(mixer.GetFloat("MasterVolume", out masterBefore), Is.True);
                Assert.That(mixer.GetFloat("SfxVolume", out sfxBefore), Is.True);
                restoreMixer = true;
                Assert.That(mixer.SetFloat("MasterVolume", 0), Is.True);
                Assert.That(mixer.SetFloat("SfxVolume", 0), Is.True);
                source.outputAudioMixerGroup = mixer.FindMatchingGroups("SFX")[0];
                source.volume = 1;
                double mixerFull = 0, mixerQuarter = 0;
                yield return Measure(samples, value => mixerFull = value);
                Assert.That(mixer.SetFloat("SfxVolume", 20 * Mathf.Log10(.25f)), Is.True);
                yield return Measure(samples, value => mixerQuarter = value);
                Assert.That(mixerFull, Is.GreaterThan(1e-5));
                Assert.That(mixerQuarter / mixerFull, Is.InRange(.20, .30), "The native driver must preserve the ordinary Unity mixer bus once.");
                Assert.That(source.pitch, Is.EqualTo(1));
                Assert.That(source.spatialBlend, Is.Zero);
                Assert.That(source.spatialize, Is.False);
            }
            finally
            {
                if (restoreMixer) { mixer.SetFloat("MasterVolume", masterBefore); mixer.SetFloat("SfxVolume", sfxBefore); }
                output?.Dispose();
                UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(listener);
                UnityEngine.Object.Destroy(clip); UnityEngine.Object.Destroy(def);
            }
            yield return null;
            yield return null;
        }

        static IEnumerator Measure(float[] samples, Action<double> result)
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < start + .12f) yield return null;
            double sum = 0;
            int count = 0;
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < start + .15f)
            {
                AudioListener.GetOutputData(samples, 0);
                for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
                count += samples.Length;
                yield return null;
            }
            result(Math.Sqrt(sum / Math.Max(count, 1)));
        }

        [UnityTest]
        public IEnumerator NearFieldNativeSfxHasIdenticalChannelsAndKeepsOutputGate()
        {
            var go = new GameObject("Near field SFX");
            var source = go.AddComponent<AudioSource>();
            var clip = Clip();
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var spatial = ScriptableObject.CreateInstance<SoundSpatialProfile>();
            var acoustic = ScriptableObject.CreateInstance<SoundAcousticProfile>();
            var blend = ScriptableObject.CreateInstance<SpatialBlendProfile>();
            using var cache = new SteamAudioClipCache(100000);
            SteamAudioSoundOutput output = null;
            try
            {
                cache.PrepareClips(new[] { clip });
                output = new SteamAudioSoundOutput(source, cache) { StartBlocked = false };
                def.Spatial = SpatialMode.None; def.Loop = true;
                spatial.Spatial = SpatialMode.Positional;
                blend.MonoDistance = 100;
                blend.FullSpatialDistance = 101;
                acoustic.Settings = new SoundAcousticSettings
                {
                    DistanceAttenuation = false,
                    MinDistance = 3,
                    MaxDistance = 18,
                    InheritSpatialBlend = false,
                    SpatialBlendProfile = blend,
                    MonoDistance = 100,
                    FullSpatialDistance = 101,
                };
                spatial.Acoustics.Profile = acoustic;
                def.SpatialProfile = spatial;
                Assert.That(output.TryStart(def, clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
                Assert.That(output.Acoustics.MinDistance, Is.EqualTo(3));
                Assert.That(output.Acoustics.MaxDistance, Is.EqualTo(18));
                Assert.That(output.DistanceAttenuation, Is.False);
                Assert.That(output.InheritSpatialBlend, Is.False);
                Assert.That(output.SpatialBlendProfile, Is.SameAs(blend));
                var process = (Action<float[], int>)Delegate.CreateDelegate(typeof(Action<float[], int>), output,
                    typeof(SteamAudioSoundOutput).GetMethod("Process", BindingFlags.NonPublic | BindingFlags.Instance));
                var block = new float[2048];
                SteamAudioAcousticAsset asset = null;
                PlanarAcousticMap map = null;
                var context = new SA.Context();
                try
                {
                    asset = CreateAcousticAsset(context);
                    map = CreateAcousticMap(asset);
                    using var world = new PlanarAcousticWorld(map, new WorldSettings());
                    using var binding = new PlanarAcousticSfxBinding(source, output);
                    binding.Prepare(world);
                    world.Tick(new Vector2(2, 0), 0);
                    var handle = binding.SourceHandle;
                    Assert.That(binding.GetSpatialBlend(world), Is.Zero);
                    binding.Publish(world);
                    for (int n = 0; n < 4; n++) { Array.Fill(block, 1f); process(block, 2); }
                    for (int i = 0; i < block.Length; i += 2)
                        Assert.That(block[i], Is.EqualTo(block[i + 1]));
                    long generation = output.CurrentParameters.Generation;
                    var changed = acoustic.Settings;
                    changed.MinDistance = 4;
                    changed.MaxDistance = 22;
                    changed.SpatialBlendProfile = null;
                    changed.MonoDistance = 0;
                    changed.FullSpatialDistance = 0;
                    acoustic.Settings = changed;
                    binding.Prepare(world);
                    world.Tick(new Vector2(2, 0), 1);
                    binding.Publish(world);
                    Assert.That(output.Acoustics.MinDistance, Is.EqualTo(4));
                    Assert.That(output.Acoustics.MaxDistance, Is.EqualTo(22));
                    Assert.That(output.Acoustics.MonoDistance, Is.Zero);
                    Assert.That(output.CurrentParameters.Generation, Is.EqualTo(generation),
                        "Shared profile edits must update the active native generation without restarting playback.");
                    Assert.That(binding.SourceHandle, Is.EqualTo(handle));
                    Assert.That(binding.GetSpatialBlend(world), Is.EqualTo(1),
                        "The active binding must consume the live shared width settings without a new generation.");
                    float stereoDifference = 0;
                    for (int n = 0; n < 4; n++)
                    {
                        Array.Fill(block, 1f);
                        process(block, 2);
                        for (int i = 0; i < block.Length; i += 2)
                            stereoDifference = Mathf.Max(stereoDifference, Mathf.Abs(block[i] - block[i + 1]));
                    }
                    Assert.That(stereoDifference, Is.GreaterThan(1e-6f),
                        "Publish must update the same active generation from mono to native spatial stereo.");
                    float observed = 0;
                    Assert.That(() =>
                    {
                        for (int i = 0; i < 1000; i++) observed += output.Acoustics.MinDistance;
                    }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
                    Assert.That(observed, Is.EqualTo(4000));
                    var lease = output.CurrentParameters;
                    Assert.That(lease.TryPublish(Coefficients,
                        new PathRenderSettings(Settings.Listener, spatialBlend: 0)), Is.True);
                    for (int n = 0; n < 4; n++) { Array.Fill(block, 1f); process(block, 2); }
                    double energy = 0;
                    for (int i = 0; i < block.Length; i += 2)
                    {
                        Assert.That(block[i], Is.EqualTo(block[i + 1]));
                        energy += block[i] * block[i];
                    }
                    Assert.That(energy, Is.GreaterThan(1e-8));
                    lease.TrySetBlocked(true);
                    Array.Fill(block, 1f); process(block, 2);
                    Assert.That(block, Is.All.EqualTo(0));
                }
                finally
                {
                    if (map != null) UnityEngine.Object.DestroyImmediate(map);
                    if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
                    context.Release();
                }
            }
            finally
            {
                output?.Dispose(); UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(clip); UnityEngine.Object.Destroy(def);
                UnityEngine.Object.Destroy(spatial); UnityEngine.Object.Destroy(acoustic);
                UnityEngine.Object.Destroy(blend);
            }
            yield return null;
            yield return null;
        }

        static SteamAudioAcousticAsset CreateAcousticAsset(SA.Context context) => SteamAudioAcousticBaker.Bake(context,
            new[] { V(-5, -2, -10), V(-5, 2, -10), V(5, 2, -10), V(5, -2, -10) },
            new[] { new SA.Triangle { index0 = 0, index1 = 1, index2 = 2 },
                new SA.Triangle { index0 = 0, index1 = 2, index2 = 3 } },
            new[] { 0, 0 }, new[] { new SA.Material() },
            new[] { Sphere(-2), Sphere(0), Sphere(2) }, SteamAudioPathBakeSettings.Default, "shared-sfx-settings");

        static PlanarAcousticMap CreateAcousticMap(SteamAudioAcousticAsset asset)
        {
            var map = ScriptableObject.CreateInstance<PlanarAcousticMap>();
            map.Initialize(asset, new bool[5, 1],
                new AcousticGridFrame(new Vector3(-2.5f, 0, .5f), Vector3.right, Vector3.back, Vector3.up), -1, 1, 0);
            return map;
        }

        static SA.Vector3 V(float x, float y, float z) => new() { x = x, y = y, z = z };
        static SA.Sphere Sphere(float x) => new() { center = V(x, 0, 0), radius = .6f };

        [UnityTest]
        public IEnumerator LogicalPitchChangesRenderedFrequencyAndCompletionRate()
        {
            var go = new GameObject("Native SFX pitch validation");
            var source = go.AddComponent<AudioSource>();
            var clip = AudioClip.Create("Periodic native SFX tone", 4800, 1, 48000, false);
            var tone = new float[4800];
            for (int i = 0; i < tone.Length; i++) tone[i] = .2f * (float)Math.Sin(2 * Math.PI * 500 * i / 48000);
            clip.SetData(tone, 0);
            var def = ScriptableObject.CreateInstance<SoundDef>();
            using var cache = new SteamAudioClipCache(100000);
            SteamAudioSoundOutput output = null;
            try
            {
                cache.PrepareClips(new[] { clip });
                output = new SteamAudioSoundOutput(source, cache) { StartBlocked = false };
                def.Spatial = SpatialMode.Positional;
                var process = (Action<float[], int>)Delegate.CreateDelegate(typeof(Action<float[], int>), output,
                    typeof(SteamAudioSoundOutput).GetMethod("Process", BindingFlags.NonPublic | BindingFlags.Instance));
                var block = new float[2048];
                int normal = CountToneCrossings(output, process, def, clip, block, 1);
                int low = CountToneCrossings(output, process, def, clip, block, .5f);
                int high = CountToneCrossings(output, process, def, clip, block, 2);
                Assert.That(normal, Is.GreaterThan(20));
                Assert.That((double)low / normal, Is.InRange(.47, .53));
                Assert.That((double)high / normal, Is.InRange(1.94, 2.06));
                int normalDuration = CountCompletionBlocks(output, process, def, clip, block, 1);
                int slowDuration = CountCompletionBlocks(output, process, def, clip, block, .5f);
                int fastDuration = CountCompletionBlocks(output, process, def, clip, block, 2);
                Assert.That(fastDuration, Is.LessThan(normalDuration));
                Assert.That(normalDuration, Is.LessThan(slowDuration));
                double expectedDifference = clip.samples * ((double)AudioSettings.outputSampleRate / clip.frequency) * (2 - .5);
                Assert.That((slowDuration - fastDuration) * (block.Length / 2), Is.EqualTo(expectedDifference).Within(block.Length));
            }
            finally
            {
                output?.Dispose(); UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(clip); UnityEngine.Object.Destroy(def);
            }
            yield return null;
            yield return null;
        }

        static int CountToneCrossings(SteamAudioSoundOutput output, Action<float[], int> process,
            SoundDef def, AudioClip clip, float[] block, float pitch)
        {
            def.Loop = true;
            Assert.That(output.TryStart(def, clip, pitch), Is.EqualTo(VoiceOutputStartResult.Started));
            Assert.That(output.CurrentParameters.TryPublish(Coefficients, Settings), Is.True);
            for (int n = 0; n < 4; n++) { Array.Fill(block, 1f); process(block, 2); }
            float previous = block[block.Length - 2];
            int crossings = 0;
            for (int n = 0; n < 16; n++)
            {
                Array.Fill(block, 1f); process(block, 2);
                for (int i = 0; i < block.Length; i += 2)
                {
                    if (previous <= 0 && block[i] > 0) crossings++;
                    previous = block[i];
                }
            }
            return crossings;
        }

        static int CountCompletionBlocks(SteamAudioSoundOutput output, Action<float[], int> process,
            SoundDef def, AudioClip clip, float[] block, float pitch)
        {
            def.Loop = false;
            Assert.That(output.TryStart(def, clip, pitch), Is.EqualTo(VoiceOutputStartResult.Started));
            output.CurrentParameters.TryPublish(Coefficients, Settings);
            int blocks = 0;
            while (!output.IsComplete && blocks < 100) { Array.Fill(block, 1f); process(block, 2); blocks++; }
            Assert.That(output.IsComplete, Is.True);
            return blocks;
        }

        [UnityTest]
        public IEnumerator PitchPauseTailAndHeldRetirementKeepGenerationOwnership()
        {
            var go = new GameObject("Native SFX controlled owner");
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var clip = Clip(480, 1);
            var def = ScriptableObject.CreateInstance<SoundDef>();
            using var cache = new SteamAudioClipCache(10000);
            SteamAudioSoundOutput output = null;
            try
            {
                cache.PrepareClips(new[] { clip });
                output = new SteamAudioSoundOutput(source, cache) { StartBlocked = false };
                def.Spatial = SpatialMode.Positional;
                Assert.That(output.TryStart(def, clip, 0), Is.EqualTo(VoiceOutputStartResult.Started));
                var old = output.CurrentParameters;
                Assert.That(old.TryPublish(Coefficients, Settings), Is.True);
                var method = typeof(SteamAudioSoundOutput).GetMethod("Process", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(method, Is.Not.Null);
                var process = (Action<float[], int>)Delegate.CreateDelegate(typeof(Action<float[], int>), output, method);
                var block = new float[2048];
                for (int i = 0; i < 8; i++) { Array.Fill(block, 1f); process(block, 2); }
                Assert.That(block, Is.All.EqualTo(0));
                Assert.That(output.IsComplete, Is.False, "Pitch zero must pause input and native tail state.");
                output.SetPitch(2);
                double energy = 0;
                for (int i = 0; i < 20 && !output.IsComplete; i++)
                {
                    Array.Fill(block, 1f); process(block, 2);
                    foreach (float sample in block) energy += sample * sample;
                }
                Assert.That(energy, Is.GreaterThan(1e-8));
                Assert.That(output.IsComplete, Is.True);
                output.Retire();
                Assert.That(old.IsValid, Is.False);
                def.Loop = true;
                Assert.That(output.TryStart(def, clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
                Assert.That(old.TrySetBlocked(false), Is.False);
                var claim = typeof(SteamAudioSoundOutput).GetMethod("TryClaimReader", BindingFlags.NonPublic | BindingFlags.Instance);
                var release = typeof(SteamAudioSoundOutput).GetMethod("ReleaseReader", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(claim, Is.Not.Null);
                Assert.That(claim.Invoke(output, null), Is.True);
                output.Retire();
                Assert.That(output.TryStart(def, clip, 1), Is.EqualTo(VoiceOutputStartResult.Unavailable));
                release.Invoke(output, null);
                Assert.That(output.TryStart(def, clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
                Assert.That(output.CurrentParameters.TryPublish(Coefficients, Settings), Is.True);
                for (int i = 0; i < 10; i++) { Array.Fill(block, 1f); process(block, 2); }
                Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                    UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                    "GC allocation recorder must detect a known allocation before measuring this path.");
                Assert.That(() =>
                {
                    for (int i = 0; i < 100; i++) { Array.Fill(block, 1f); process(block, 2); }
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
                output.Retire();
                int unavailable = 0;
                Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                    UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                    "GC allocation recorder must detect a known allocation before measuring this path.");
                Assert.That(() =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        if (output.TryStart(def, clip, 1) != VoiceOutputStartResult.Started) unavailable++;
                        output.Retire();
                    }
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "Warm ownership reuse must not create clips, sources, arrays or publisher objects per play.");
                Assert.That(unavailable, Is.Zero);
                def.Spatial = SpatialMode.None;
                Assert.That(output.TryStart(def, clip, 1), Is.EqualTo(VoiceOutputStartResult.Unsupported));
                Assert.That(output.CurrentParameters.IsValid, Is.False);
                Array.Fill(block, .25f); process(block, 2);
                Assert.That(block, Is.All.EqualTo(.25f), "Unsupported UI/stereo output must pass through unchanged.");
                Assert.That(output.MinDistance, Is.Zero);
                def.Spatial = SpatialMode.Positional;
                def.MinDistance = float.NaN;
                Assert.That(output.TryStart(def, clip, 1), Is.EqualTo(VoiceOutputStartResult.Unavailable));
                Assert.That(output.Failure, Is.EqualTo(SteamAudioSoundFailure.InvalidRequest));
                def.MinDistance = -3;
                Assert.That(output.TryStart(def, clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
                Assert.That(output.MinDistance, Is.Zero);
                def.MinDistance = 3;
                Assert.That(output.TryStart(def, clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
                Assert.That(output.MinDistance, Is.EqualTo(3));
                def.MinDistance = 8;
                Assert.That(output.MinDistance, Is.EqualTo(3), "Authored edits take effect on the next ownership request.");
                var host = (MonoBehaviour)typeof(SteamAudioSoundOutput).GetField("_retirement", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);
                Assert.That(claim.Invoke(output, null), Is.True);
                UnityEngine.Object.Destroy(go);
                yield return null;
                Assert.That(host != null, Is.True, "Destroying the source cannot destroy native ownership while its reader claim is held.");
                release.Invoke(output, null);
                yield return null;
                yield return null;
                Assert.That(host == null, Is.True, "The independent retirement host must reclaim without another source callback.");
            }
            finally
            {
                output?.Dispose(); UnityEngine.Object.Destroy(go);
                UnityEngine.Object.Destroy(clip); UnityEngine.Object.Destroy(def);
            }
            yield return null;
            yield return null;
        }
    }
}
