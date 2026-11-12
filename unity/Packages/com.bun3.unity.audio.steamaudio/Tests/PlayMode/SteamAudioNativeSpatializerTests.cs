using System;
using System.Collections;
using System.Reflection;
using Bun3.Unity.Acoustics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    /// <summary>Native SFX output through the Steam Audio Unity spatializer: no managed audio-thread code.</summary>
    public sealed class SteamAudioNativeSpatializerTests
    {
        const int PathingMix = 27, PathingWidth = 34, SimulationHandle = 31;

        sealed class WorldSettings : PlanarAcousticSettings
        {
            public override int SourceCapacity => 1;
            public override float OcclusionRadius => 0;
            public override int OcclusionSamples => 1;
            public override float ObstructedPathGain => 1;
            public override float MonoDistance => 1;
            public override float FullSpatialDistance => 3;
        }

        GameObject _listener;
        AudioClip _mono, _stereo;
        SoundDef _def;
        SoundCatalog _catalog;
        SA.Context _context;
        SteamAudioAcousticAsset _asset;
        PlanarAcousticMap _map;

        [SetUp]
        public void SetUp()
        {
            _listener = new GameObject("Listener", typeof(AudioListener));
            _mono = Tone("mono", 1);
            _stereo = Tone("stereo", 2);
            _def = ScriptableObject.CreateInstance<SoundDef>();
            _def.Clips = new[] { _mono };
            _def.Spatial = SpatialMode.Positional;
            _def.Loop = true;
            _catalog = ScriptableObject.CreateInstance<SoundCatalog>();
            _catalog.SetEntries(new[] { new SoundCatalogEntry("fx.3d", _def) });
        }

        [TearDown]
        public void TearDown()
        {
            SoundSystem.Shutdown();
            if (_map != null) Object.DestroyImmediate(_map);
            if (_asset != null) Object.DestroyImmediate(_asset);
            _context?.Release();
            Object.Destroy(_listener);
            Object.Destroy(_catalog);
            Object.Destroy(_def);
            Object.Destroy(_mono);
            Object.Destroy(_stereo);
        }

        static AudioClip Tone(string name, int channels)
        {
            const int frames = 48000;
            var clip = AudioClip.Create(name, frames, channels, 48000, false);
            var data = new float[frames * channels];
            for (int frame = 0; frame < frames; frame++)
                for (int channel = 0; channel < channels; channel++)
                    data[frame * channels + channel] = .2f * Mathf.Sin(2 * Mathf.PI * 300 * frame / 48000f);
            clip.SetData(data, 0);
            return clip;
        }

        PlanarAcousticWorld World()
        {
            _context = new SA.Context();
            _asset = SteamAudioAcousticBaker.Bake(_context,
                new[] { V(-5, -2, -10), V(-5, 2, -10), V(5, 2, -10), V(5, -2, -10) },
                new[] { new SA.Triangle { index0 = 0, index1 = 1, index2 = 2 }, new SA.Triangle { index0 = 0, index1 = 2, index2 = 3 } },
                new[] { 0, 0 }, new[] { new SA.Material() },
                new[] { Sphere(-2), Sphere(0), Sphere(2) }, SteamAudioPathBakeSettings.Default, "native-spatializer");
            _map = ScriptableObject.CreateInstance<PlanarAcousticMap>();
            _map.Initialize(_asset, new bool[5, 1],
                new AcousticGridFrame(new Vector3(-2.5f, 0, .5f), Vector3.right, Vector3.back, Vector3.up), -1, 1, 0);
            return new PlanarAcousticWorld(_map, new WorldSettings());
        }

        static SA.Vector3 V(float x, float y, float z) => new() { x = x, y = y, z = z };
        static SA.Sphere Sphere(float x) => new() { center = V(x, 0, 0), radius = .6f };

        static float Param(AudioSource source, int index)
        {
            Assert.That(source.GetSpatializerFloat(index, out float value), Is.True, "Spatializer parameter " + index);
            return value;
        }

        static void Publish(PlanarAcousticWorld world, PlanarAcousticSfxBinding binding, AudioSource source, Vector2 position, Vector2 listener)
        {
            source.transform.position = position;
            binding.Prepare(world);
            world.Tick(listener, Time.realtimeSinceStartupAsDouble);
            binding.Publish(world);
        }

        [UnityTest]
        public IEnumerator PoolVoicesRunNoManagedAudioCallback()
        {
            SoundSystem.Initialize(_catalog, SteamAudioSoundSetup.ApplyNativeOutput(
                new SoundSystemConfig { SfxVoices = 3 }, new PlanarAcousticOutputs()));
            SoundSystem.PlayAt("fx.3d", Vector3.one);
            yield return null;
            for (int i = 0; i < 3; i++)
                foreach (var behaviour in SoundSystem.Current.SourceForTest(i).GetComponents<MonoBehaviour>())
                    Assert.That(behaviour.GetType().GetMethod("OnAudioFilterRead",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Is.Null, behaviour.GetType().FullName);
        }

        [UnityTest]
        public IEnumerator VoicePlaysAuthoredClipWithPitchAndLoop()
        {
            SoundSystem.Initialize(_catalog, SteamAudioSoundSetup.ApplyNativeOutput(
                new SoundSystemConfig { SfxVoices = 1 }, new PlanarAcousticOutputs()));
            var handle = SoundSystem.PlayAt("fx.3d", Vector3.one);
            handle.SetPitch(1.5f);
            yield return null;
            var source = SoundSystem.Current.SourceForTest(0);
            Assert.That(source.clip, Is.SameAs(_mono));
            Assert.That(source.loop, Is.True);
            Assert.That(source.spatialize, Is.True);
            Assert.That(source.isPlaying, Is.True);
            Assert.That(source.pitch, Is.EqualTo(1.5f).Within(1e-6f));
        }

        [UnityTest]
        public IEnumerator BlockedPathSetsPathingMixZero()
        {
            using var world = World();
            var go = new GameObject("Native SFX voice");
            var source = go.AddComponent<AudioSource>();
            source.clip = _mono;
            var output = new SteamAudioSoundOutput(source);
            using var binding = new PlanarAcousticSfxBinding(source, output);
            Assert.That(output.TryStart(_def, _mono, 1, SpatialMode.Positional), Is.EqualTo(VoiceOutputStartResult.Started));
            source.Play();

            Publish(world, binding, source, new Vector2(2, 0), Vector2.zero);
            Assert.That(output.IsBlocked, Is.False);
            Assert.That(Param(source, PathingMix), Is.EqualTo(Mathf.Sqrt(2)).Within(1e-5f),
                "Mono clips enter the spatializer at 1/sqrt(2) per channel; the path gain compensates.");
            Assert.That(output.HasSimulationSource, Is.True, "The covered path registers its simulation source.");

            Publish(world, binding, source, new Vector2(10, 0), Vector2.zero);
            Assert.That(output.IsBlocked, Is.True);
            Assert.That(Param(source, PathingMix), Is.Zero);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(Rms(0), Is.LessThan(1e-6), "A blocked path renders silence.");
            output.Dispose();
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator WidthFollowsSpatialBlendProfile()
        {
            Assume.That(SteamAudioSpatializerPatch.IsPatched, "The patched audioplugin_phonon.dll is not installed.");
            using var world = World();
            var go = new GameObject("Native SFX voice");
            var source = go.AddComponent<AudioSource>();
            source.clip = _mono;
            var output = new SteamAudioSoundOutput(source);
            using var binding = new PlanarAcousticSfxBinding(source, output);
            Assert.That(output.TryStart(_def, _mono, 1, SpatialMode.Positional), Is.EqualTo(VoiceOutputStartResult.Started));
            source.Play();

            for (int i = 0; i < 8; i++)
            {
                Publish(world, binding, source, new Vector2(-1.6f, 0), new Vector2(-2, 0));
                yield return new WaitForSecondsRealtime(.05f);
            }
            Assert.That(Param(source, PathingWidth), Is.Zero, "Within Mono Distance the pathing output is dual mono.");
            var left = new float[1024];
            var right = new float[1024];
            AudioListener.GetOutputData(left, 0);
            AudioListener.GetOutputData(right, 1);
            Assert.That(Rms(0), Is.GreaterThan(0), "The near-field voice must be audible.");
            for (int i = 0; i < left.Length; i++) Assert.That(left[i], Is.EqualTo(right[i]).Within(1e-5f), "frame " + i);
            Publish(world, binding, source, new Vector2(2, 0), new Vector2(-2, 0));
            Assert.That(Param(source, PathingWidth), Is.EqualTo(1), "Beyond Full Spatial Distance the output keeps full width.");
            yield return null;
            output.Dispose();
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator PositionalVoiceKeepsTheManagedRendererLevel([Values(1, 2)] int channels)
        {
            using var world = World();
            var clip = channels == 1 ? _mono : _stereo;
            var go = new GameObject("Native SFX voice " + clip.name);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            var output = new SteamAudioSoundOutput(source);
            var binding = new PlanarAcousticSfxBinding(source, output);
            Assert.That(output.TryStart(_def, clip, 1, SpatialMode.Positional), Is.EqualTo(VoiceOutputStartResult.Started));
            source.Play();
            // Batch frames can be far shorter than one DSP block, and the first spatializer instance initializes lazily:
            // wait in real time until the output is audible, then require every measured read to be audible.
            int audible = 0;
            for (double deadline = Time.realtimeSinceStartupAsDouble + 3; audible < 3 && Time.realtimeSinceStartupAsDouble < deadline;)
            {
                Publish(world, binding, source, new Vector2(2, 0), Vector2.zero);
                yield return new WaitForSecondsRealtime(.05f);
                audible = Rms() > 0 ? audible + 1 : 0;
            }
            Assert.That(audible, Is.EqualTo(3), "The positional voice never became audible.");
            double native = 0;
            for (int read = 0; read < 6; read++)
            {
                Publish(world, binding, source, new Vector2(2, 0), Vector2.zero);
                yield return new WaitForSecondsRealtime(.05f);
                double rms = Rms();
                Assert.That(rms, Is.GreaterThan(0), "The pathing output dropped out at read " + read);
                native += rms;
            }
            native /= 6;
            var coefficients = new float[4];
            Assert.That(world.TryGetPath(binding.SourceHandle, coefficients, out var path), Is.True);
            double managed = ManagedRendererLeftRms(coefficients, path);
            Debug.Log("[native-level] channels=" + channels + " native=" + native + " managed=" + managed + " ratio=" + native / managed);
            binding.Dispose();
            output.Dispose();
            Object.Destroy(go);
            Assert.That(native / managed, Is.EqualTo(1).Within(.1), "Positional SFX keep the loudness of the removed managed renderer.");
        }

        // The removed managed renderer: mono (stereo clips averaged) through SteamAudioPathRenderer at unit gain.
        static double ManagedRendererLeftRms(float[] coefficients, PlanarAcousticPath path)
        {
            using var renderer = SteamAudioPathRenderer.CreateDefault(48000, 1024);
            var mono = new float[1024];
            var stereo = new float[2048];
            double sum = 0;
            for (int block = 0; block < 8; block++)
            {
                for (int i = 0; i < mono.Length; i++) mono[i] = .2f * Mathf.Sin(2 * Mathf.PI * 300 * (block * 1024 + i) / 48000f);
                renderer.Render(mono, stereo, coefficients, path.SimulationResult.EqLow, path.SimulationResult.EqMid,
                    path.SimulationResult.EqHigh, path.Listener);
                if (block < 4) continue;
                for (int i = 0; i < 1024; i++) sum += stereo[2 * i] * stereo[2 * i];
            }
            return Math.Sqrt(sum / 4096);
        }

        static double Rms(int channel = 0)
        {
            var left = new float[2048];
            AudioListener.GetOutputData(left, channel);
            double sum = 0;
            foreach (float sample in left) sum += sample * sample;
            return Math.Sqrt(sum / left.Length);
        }

        [UnityTest]
        public IEnumerator ListenerPauseDoesNotCompleteOneShot()
        {
            var go = new GameObject("Native SFX one-shot");
            var source = go.AddComponent<AudioSource>();
            source.clip = _mono;
            var output = new SteamAudioSoundOutput(source);
            var oneShot = ScriptableObject.CreateInstance<SoundDef>();
            oneShot.Clips = new[] { _mono };
            oneShot.Spatial = SpatialMode.Positional;
            try
            {
                Assert.That(output.TryStart(oneShot, _mono, 1, SpatialMode.Positional), Is.EqualTo(VoiceOutputStartResult.Started));
                source.Play();
                yield return null;
                AudioListener.pause = true;
                yield return null;
                Assert.That(output.IsComplete, Is.False, "A paused listener must not end positional one-shots.");
            }
            finally
            {
                AudioListener.pause = false;
                output.Dispose();
                Object.Destroy(go);
                Object.Destroy(oneShot);
            }
        }

        [UnityTest]
        public IEnumerator PublishReappliesPathingOnlyConfiguration()
        {
            using var world = World();
            var go = new GameObject("Native SFX voice");
            var source = go.AddComponent<AudioSource>();
            source.clip = _mono;
            var output = new SteamAudioSoundOutput(source);
            using var binding = new PlanarAcousticSfxBinding(source, output);
            Assert.That(output.TryStart(_def, _mono, 1, SpatialMode.Positional), Is.EqualTo(VoiceOutputStartResult.Started));
            source.Play();
            // A recreated effect instance comes back with the plugin defaults: direct path on, pathing off.
            source.SetSpatializerFloat(6, 0);
            source.SetSpatializerFloat(23, 1);
            Publish(world, binding, source, new Vector2(2, 0), Vector2.zero);
            Assert.That(Param(source, 6), Is.EqualTo(1));
            Assert.That(Param(source, 23), Is.Zero);
            yield return null;
            output.Dispose();
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator ReleasingVoiceImmediatelyAfterPlayIsSafe()
        {
            SoundSystem.Initialize(_catalog, SteamAudioSoundSetup.ApplyNativeOutput(
                new SoundSystemConfig { SfxVoices = 2 }, new PlanarAcousticOutputs()));
            for (int i = 0; i < 100; i++) SoundSystem.PlayAt("fx.3d", Vector3.one).Stop();
            yield return null;
            SoundSystem.Shutdown();
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DetectReportsMissingStockPatched()
        {
            Assert.That(SteamAudioSpatializer.Classify(string.Empty, true), Is.EqualTo(SteamAudioSpatializerSupport.Missing));
            Assert.That(SteamAudioSpatializer.Classify("Other Spatializer", true), Is.EqualTo(SteamAudioSpatializerSupport.Missing));
            Assert.That(SteamAudioSpatializer.Classify("Steam Audio Spatializer", false), Is.EqualTo(SteamAudioSpatializerSupport.Stock));
            Assert.That(SteamAudioSpatializer.Classify("Steam Audio Spatializer", true), Is.EqualTo(SteamAudioSpatializerSupport.Patched));
        }
    }
}
