using System;
using System.Collections;
using System.Collections.Generic;
using Bun3.Unity.Acoustics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.FreshProcess
{
    /// <summary>
    /// A stop-the-world GC must not stall the audio mixer while native SFX play. Once any managed audio callback has run
    /// in a process, every later GC stalls the mixer, so this runs only explicitly, alone, in a fresh process.
    /// </summary>
    [Explicit("Run alone in a fresh process; any earlier OnAudioFilterRead in the process attaches the mixer thread.")]
    public sealed class SteamAudioGcStallTests
    {
        sealed class WorldSettings : PlanarAcousticSettings
        {
            public override int SourceCapacity => 1;
            public override float OcclusionRadius => 0;
            public override int OcclusionSamples => 1;
            public override float ObstructedPathGain => 1;
        }

        [UnityTest]
        public IEnumerator GcDoesNotStallMixerAfterSpatializedPlayback()
        {
            var listener = new GameObject("Listener", typeof(AudioListener));
            var clip = AudioClip.Create("gc tone", 48000, 1, 48000, false);
            var data = new float[48000];
            for (int i = 0; i < data.Length; i++) data[i] = .2f * Mathf.Sin(2 * Mathf.PI * 300 * i / 48000f);
            clip.SetData(data, 0);
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { clip };
            def.Spatial = SpatialMode.Positional;
            def.Loop = true;
            var context = new SA.Context();
            var asset = SteamAudioAcousticBaker.Bake(context,
                new[] { V(-5, -2, -10), V(-5, 2, -10), V(5, 2, -10), V(5, -2, -10) },
                new[] { new SA.Triangle { index0 = 0, index1 = 1, index2 = 2 }, new SA.Triangle { index0 = 0, index1 = 2, index2 = 3 } },
                new[] { 0, 0 }, new[] { new SA.Material() },
                new[] { Sphere(-2), Sphere(0), Sphere(2) }, SteamAudioPathBakeSettings.Default, "gc-stall");
            var map = ScriptableObject.CreateInstance<PlanarAcousticMap>();
            map.Initialize(asset, new bool[5, 1],
                new AcousticGridFrame(new Vector3(-2.5f, 0, .5f), Vector3.right, Vector3.back, Vector3.up), -1, 1, 0);
            var world = new PlanarAcousticWorld(map, new WorldSettings());
            var go = new GameObject("Native SFX voice");
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            var output = new SteamAudioSoundOutput(source);
            var binding = new PlanarAcousticSfxBinding(source, output);
            // A large live heap makes each full collection long enough to measure against 21 ms DSP blocks.
            var heap = new List<object[]>(200000);
            for (int i = 0; i < 200000; i++) { var row = new object[100]; for (int j = 0; j < row.Length; j++) row[j] = new object(); heap.Add(row); }
            try
            {
                Assert.That(output.TryStart(def, clip, 1, SpatialMode.Positional), Is.EqualTo(VoiceOutputStartResult.Started));
                source.Play();
                for (int i = 0; i < 20; i++)
                {
                    source.transform.position = new Vector3(2, 0, 0);
                    binding.Prepare(world);
                    world.Tick(Vector2.zero, Time.realtimeSinceStartupAsDouble);
                    binding.Publish(world);
                    yield return new WaitForSecondsRealtime(.05f);
                }
                Assert.That(output.IsBlocked, Is.False, "The voice must be rendering a path while the GC runs.");
                for (int i = 0; i < 3; i++)
                {
                    double real0 = Time.realtimeSinceStartupAsDouble, dsp0 = AudioSettings.dspTime;
                    GC.Collect();
                    double real = Time.realtimeSinceStartupAsDouble - real0, dsp = AudioSettings.dspTime - dsp0;
                    TestContext.WriteLine("GC {0}: real={1:F3}s dsp={2:F3}s lost={3:F3}s", i, real, dsp, real - dsp);
                    Assert.That(real, Is.GreaterThan(.05), "The collection must outlast a few DSP blocks to be meaningful.");
                    Assert.That(real - dsp, Is.LessThan(.05), "The mixer stalled during GC.");
                    yield return new WaitForSecondsRealtime(.3f);
                }
            }
            finally
            {
                heap.Clear();
                binding.Dispose();
                output.Dispose();
                world.Dispose();
                Object.Destroy(go);
                Object.DestroyImmediate(map);
                Object.DestroyImmediate(asset);
                context.Release();
                Object.Destroy(def);
                Object.Destroy(clip);
                Object.Destroy(listener);
            }
        }

        static SA.Vector3 V(float x, float y, float z) => new() { x = x, y = y, z = z };
        static SA.Sphere Sphere(float x) => new() { center = V(x, 0, 0), radius = .6f };
    }
}
