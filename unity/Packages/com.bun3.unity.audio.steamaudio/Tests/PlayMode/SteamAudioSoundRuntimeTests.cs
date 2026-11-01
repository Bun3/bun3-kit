using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    /// <summary>Verifies that pooled native SFX outputs share one Steam Audio runtime.</summary>
    public sealed class SteamAudioSoundRuntimeTests
    {
        private AudioClip _clip;
        private SoundDef _definition;
        private SteamAudioClipCache _cache;
        private GameObject[] _sources;

        private static int HostCount => Resources.FindObjectsOfTypeAll<SteamAudioSoundRetirementHost>().Length;

        private static SteamAudioPathRenderer Renderer(SteamAudioSoundOutput output) => (SteamAudioPathRenderer)typeof(SteamAudioSoundOutput)
            .GetField("_renderer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);

        [SetUp]
        public void SetUp()
        {
            _clip = AudioClip.Create("Shared runtime test", 480, 1, 48000, false);
            _definition = ScriptableObject.CreateInstance<SoundDef>();
            _definition.Clips = new[] { _clip };
            _definition.Spatial = SpatialMode.Positional;
            _cache = new SteamAudioClipCache(10000);
            _cache.PrepareDefinition(_definition);
            _sources = new GameObject[3];
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var source in _sources) Object.Destroy(source);
            _cache.Dispose();
            Object.Destroy(_definition);
            Object.Destroy(_clip);
        }

        private SteamAudioSoundOutput[] CreateOutputs(SteamAudioSoundRuntime runtime, int count)
        {
            var outputs = new SteamAudioSoundOutput[count];
            for (int i = 0; i < count; i++)
            {
                _sources[i] = new GameObject("Shared runtime source " + i);
                outputs[i] = new SteamAudioSoundOutput(_sources[i].AddComponent<AudioSource>(), _cache, runtime);
            }
            return outputs;
        }

        private static void DisposeAll(SteamAudioSoundOutput[] outputs)
        {
            foreach (var output in outputs) output.Dispose();
        }

        [Test]
        public void SharedRuntime_OutputsShareHrtfAndDriverClip()
        {
            using var runtime = new SteamAudioSoundRuntime();
            var outputs = CreateOutputs(runtime, 2);
            try
            {
                foreach (var output in outputs)
                    Assert.That(output.TryStart(_definition, _clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
                Assert.That(_sources[0].GetComponent<AudioSource>().clip, Is.SameAs(runtime.DriverClip));
                Assert.That(_sources[1].GetComponent<AudioSource>().clip, Is.SameAs(runtime.DriverClip));
                Assert.That(Renderer(outputs[0]).HrtfHandleForTest, Is.Not.EqualTo(System.IntPtr.Zero));
                Assert.That(Renderer(outputs[1]).HrtfHandleForTest, Is.EqualTo(Renderer(outputs[0]).HrtfHandleForTest));
            }
            finally { DisposeAll(outputs); }
        }

        [Test]
        public void SharedRuntime_CreatesOneRetirementHost()
        {
            int before = HostCount;
            using var runtime = new SteamAudioSoundRuntime();
            var outputs = CreateOutputs(runtime, 3);
            try { Assert.That(HostCount - before, Is.EqualTo(1)); }
            finally { DisposeAll(outputs); }
        }

        [UnityTest]
        public IEnumerator OutputDispose_KeepsSharedResources_RuntimeDisposeReleasesThem()
        {
            int before = HostCount;
            var runtime = new SteamAudioSoundRuntime();
            AudioClip driver = null;
            try
            {
                var outputs = CreateOutputs(runtime, 2);
                foreach (var output in outputs)
                    Assert.That(output.TryStart(_definition, _clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
                DisposeAll(outputs);
                yield return null;
                driver = runtime.DriverClip;
                Assert.That(driver != null, Is.True, "Disposing outputs must keep the shared driver clip.");
                Assert.That(HostCount - before, Is.EqualTo(1), "Disposing outputs must keep the shared retirement host.");
            }
            finally { runtime.Dispose(); }
            yield return null;
            Assert.That(driver == null, Is.True, "Runtime disposal must destroy the shared driver clip.");
            Assert.That(HostCount, Is.EqualTo(before), "Runtime disposal must destroy the shared retirement host.");
        }
    }
}
