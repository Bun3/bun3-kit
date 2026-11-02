using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    /// <summary>Verifies that the shared native runtime recovers only what an audio device change actually lost.</summary>
    public sealed class SteamAudioDeviceChangeTests
    {
        private AudioClip _clip;
        private SoundDef _definition;
        private SteamAudioClipCache _cache;
        private SteamAudioSoundRuntime _runtime;
        private GameObject[] _sources;
        private SteamAudioSoundOutput[] _outputs;
        private SteamAudioSoundOutput _heldReader;
        private readonly List<SteamAudioSoundOutput> _ownedOutputs = new();
        private readonly List<GameObject> _ownedSources = new();

        private static int HostCount => Resources.FindObjectsOfTypeAll<SteamAudioSoundRetirementHost>().Length;

        private static object Field(SteamAudioSoundOutput output, string name) =>
            typeof(SteamAudioSoundOutput).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);

        private static SteamAudioPathRenderer Renderer(SteamAudioSoundOutput output) => (SteamAudioPathRenderer)Field(output, "_renderer");

        private static SteamAudioSoundRuntime RuntimeOf(SteamAudioSoundOutput output) => (SteamAudioSoundRuntime)Field(output, "_runtime");

        // Native SH rejects NaN, so the replacement's prewarm render throws for this output only.
        private static void BreakCoefficients(SteamAudioSoundOutput output) => ((float[])Field(output, "_coefficients"))[0] = float.NaN;

        // Public constructor: a private runtime and an order-2 factory renderer (nine coefficients).
        private SteamAudioSoundOutput CreateOrderTwoOutput()
        {
            var source = new GameObject("Order-2 factory source " + _ownedSources.Count);
            _ownedSources.Add(source);
            var output = new SteamAudioSoundOutput(source.AddComponent<AudioSource>(), _cache,
                (rate, frame) => SteamAudioPathRenderer.CreateDefault(rate, frame, 2));
            _ownedOutputs.Add(output);
            return output;
        }

        private static int RealRate => AudioSettings.outputSampleRate;
        private static int RealFrame { get { AudioSettings.GetDSPBufferSize(out int frame, out _); return frame; } }
        private static int OtherRate => RealRate == 48000 ? 44100 : 48000;

        [SetUp]
        public void SetUp()
        {
            _clip = AudioClip.Create("Device change test", 4800, 1, 48000, false);
            _definition = ScriptableObject.CreateInstance<SoundDef>();
            _definition.Clips = new[] { _clip };
            _definition.Spatial = SpatialMode.Positional;
            _cache = new SteamAudioClipCache(100000);
            _cache.PrepareDefinition(_definition);
            _runtime = new SteamAudioSoundRuntime();
            _sources = new GameObject[2];
            _outputs = new SteamAudioSoundOutput[2];
            for (int i = 0; i < 2; i++)
            {
                _sources[i] = new GameObject("Device change source " + i);
                _outputs[i] = new SteamAudioSoundOutput(_sources[i].AddComponent<AudioSource>(), _cache, _runtime);
            }
            // Only the first output owns an active positional request.
            Assert.That(_outputs[0].TryStart(_definition, _clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
        }

        [TearDown]
        public void TearDown()
        {
            _heldReader?.ReleaseReader();
            _heldReader = null;
            foreach (var output in _outputs) output?.Dispose();
            foreach (var output in _ownedOutputs) output.Dispose();
            _runtime.Dispose();
            foreach (var source in _sources) Object.Destroy(source);
            foreach (var source in _ownedSources) Object.Destroy(source);
            _cache.Dispose();
            Object.Destroy(_definition);
            Object.Destroy(_clip);
        }

        [Test]
        public void SameFormat_IntactDriverClip_ChangesNothing()
        {
            var driver = _runtime.DriverClip;
            var hrtf = Renderer(_outputs[0]).HrtfHandleForTest;

            _runtime.HandleAudioConfigurationChanged(_runtime.SampleRate, _runtime.FrameSize);

            Assert.That(_runtime.DriverClip, Is.SameAs(driver));
            Assert.That(Renderer(_outputs[0]).HrtfHandleForTest, Is.EqualTo(hrtf));
            Assert.That(_outputs[0].IsComplete, Is.False);
        }

        [Test]
        public void SameFormat_LostDriverClip_RecreatesClipAndEndsActiveOutputs()
        {
            var lost = _runtime.DriverClip;
            var hrtf = Renderer(_outputs[0]).HrtfHandleForTest;
            lost.SetData(new float[lost.samples * lost.channels], 0);

            _runtime.HandleAudioConfigurationChanged(_runtime.SampleRate, _runtime.FrameSize);

            var driver = _runtime.DriverClip;
            var first = new float[2];
            Assert.That(driver, Is.Not.SameAs(lost));
            Assert.That(driver.GetData(first, 0), Is.True);
            Assert.That(first[0], Is.EqualTo(1f));
            Assert.That(_outputs[0].IsComplete, Is.True);
            Assert.That(_outputs[0].Failure, Is.EqualTo(SteamAudioSoundFailure.OutputFormatChanged));
            Assert.That(_outputs[1].Failure, Is.EqualTo(SteamAudioSoundFailure.None), "Idle outputs are left alone.");
            Assert.That(Renderer(_outputs[0]).HrtfHandleForTest, Is.EqualTo(hrtf));
        }

        [Test]
        public void FormatChange_SwapsHrtfAndRenderersOnly()
        {
            var sources = new[] { _sources[0].GetComponent<AudioSource>(), _sources[1].GetComponent<AudioSource>() };
            var hrtf = Renderer(_outputs[0]).HrtfHandleForTest;
            long decoded = _cache.DecodedBytes;

            _runtime.HandleAudioConfigurationChanged(OtherRate, _runtime.FrameSize);

            for (int i = 0; i < _outputs.Length; i++)
            {
                Assert.That(Renderer(_outputs[i]).SampleRate, Is.EqualTo(OtherRate));
                Assert.That(Renderer(_outputs[i]).HrtfHandleForTest, Is.Not.EqualTo(hrtf));
                Assert.That(_sources[i].GetComponent<AudioSource>(), Is.SameAs(sources[i]));
            }
            Assert.That(_outputs[0].Failure, Is.EqualTo(SteamAudioSoundFailure.OutputFormatChanged));
            Assert.That(_cache.DecodedBytes, Is.EqualTo(decoded));
        }

        [Test]
        public void FormatRestore_AllowsPositionalPlaybackAgain()
        {
            _runtime.HandleAudioConfigurationChanged(OtherRate, _runtime.FrameSize);
            Assert.That(_outputs[1].TryStart(_definition, _clip, 1), Is.EqualTo(VoiceOutputStartResult.Unavailable),
                "Renderers built for a format the device does not use cannot start.");

            _runtime.HandleAudioConfigurationChanged(RealRate, RealFrame);

            Assert.That(_outputs[1].TryStart(_definition, _clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
            Assert.That(Renderer(_outputs[1]).SampleRate, Is.EqualTo(RealRate));
        }

        [UnityTest]
        public IEnumerator ConsecutiveFormatChanges_EndOnLatestFormat()
        {
            Assert.That(_outputs[0].TryClaimReader(), Is.True);
            _heldReader = _outputs[0];
            var held = Renderer(_outputs[0]);
            int latestFrame = _runtime.FrameSize * 2;

            _runtime.HandleAudioConfigurationChanged(OtherRate, _runtime.FrameSize);
            _runtime.HandleAudioConfigurationChanged(RealRate, latestFrame);
            Assert.That(Renderer(_outputs[0]), Is.SameAs(held), "A held reader keeps its renderer.");
            _heldReader = null;
            _outputs[0].ReleaseReader();
            yield return null;

            foreach (var output in _outputs)
            {
                Assert.That(Renderer(output).SampleRate, Is.EqualTo(RealRate));
                Assert.That(Renderer(output).FrameSize, Is.EqualTo(latestFrame));
            }
        }

        [UnityTest]
        public IEnumerator FactoryRendererOrder_IsKeptAcrossFormatChange()
        {
            int hosts = HostCount;
            var idle = CreateOrderTwoOutput();
            var busy = CreateOrderTwoOutput();
            Assert.That(busy.TryStart(_definition, _clip, 1), Is.EqualTo(VoiceOutputStartResult.Started));
            Assert.That(busy.TryClaimReader(), Is.True);
            _heldReader = busy;
            var held = Renderer(busy);

            RuntimeOf(idle).HandleAudioConfigurationChanged(OtherRate, RealFrame);
            RuntimeOf(busy).HandleAudioConfigurationChanged(OtherRate, RealFrame);

            Assert.That(Renderer(idle).CoefficientCount, Is.EqualTo(9));
            Assert.That(Renderer(idle).SampleRate, Is.EqualTo(OtherRate));
            Assert.That(Renderer(busy), Is.SameAs(held), "A held reader keeps its renderer.");
            // Disposal waits for the reader, so finishing it requires the host's update to keep running.
            busy.Dispose();
            _heldReader = null;
            busy.ReleaseReader();
            yield return null;
            yield return null;

            Assert.That(Renderer(busy).CoefficientCount, Is.EqualTo(9), "The pending replacement keeps the factory order.");
            Assert.That(Renderer(busy).SampleRate, Is.EqualTo(OtherRate));
            Assert.That(HostCount - hosts, Is.EqualTo(1), "The disposed output finalized; only the idle output's host remains.");
        }

        [UnityTest]
        public IEnumerator FormatChange_FailedReplacement_KeepsOldRendererAndRecreatesClip()
        {
            var failing = Renderer(_outputs[0]);
            var driver = _runtime.DriverClip;
            BreakCoefficients(_outputs[0]);
            LogAssert.Expect(LogType.Exception, new Regex("ArgumentOutOfRangeException"));

            _runtime.HandleAudioConfigurationChanged(OtherRate, _runtime.FrameSize);

            Assert.That(Renderer(_outputs[0]), Is.SameAs(failing), "The failed output keeps its old renderer.");
            Assert.That(Renderer(_outputs[1]).SampleRate, Is.EqualTo(OtherRate), "Later outputs are still replaced.");
            Assert.That(_runtime.DriverClip, Is.Not.SameAs(driver), "The driver clip is recreated despite the failure.");
            // A repeated exception on the next frame would fail the test as an unexpected log.
            yield return null;
        }

        [UnityTest]
        public IEnumerator PendingReplacementFailure_IsLoggedOnceAndDropped()
        {
            Assert.That(_outputs[0].TryClaimReader(), Is.True);
            _heldReader = _outputs[0];
            var held = Renderer(_outputs[0]);
            BreakCoefficients(_outputs[0]);
            _runtime.HandleAudioConfigurationChanged(OtherRate, _runtime.FrameSize);
            LogAssert.Expect(LogType.Exception, new Regex("ArgumentOutOfRangeException"));

            _heldReader = null;
            _outputs[0].ReleaseReader();
            yield return null;
            yield return null;

            // The second frame would log again if the failed replacement stayed pending.
            Assert.That(Renderer(_outputs[0]), Is.SameAs(held), "The failed output keeps its old renderer.");
        }

        [Test]
        public void TryStart_WithoutLiveDriverClip_IsUnavailable()
        {
            Object.DestroyImmediate(_runtime.DriverClip);

            Assert.That(_outputs[1].TryStart(_definition, _clip, 1), Is.EqualTo(VoiceOutputStartResult.Unavailable));
            Assert.That(_outputs[1].Failure, Is.EqualTo(SteamAudioSoundFailure.OutputFormatChanged));
        }

        [UnityTest]
        public IEnumerator RealAudioReset_PositionalPlaybackWorksAfterwards()
        {
            var acoustic = new PlanarAcousticOutputs();
            var clip = AudioClip.Create("fx.3d reset clip", 48000, 1, 48000, false);
            var fx = ScriptableObject.CreateInstance<SoundDef>();
            fx.Clips = new[] { clip };
            fx.Spatial = SpatialMode.Positional;
            var catalog = ScriptableObject.CreateInstance<SoundCatalog>();
            catalog.SetEntries(new[] { new SoundCatalogEntry("fx.3d", fx) });
            try
            {
                SoundSystem.Initialize(catalog,
                    SteamAudioSoundSetup.ApplyNativeOutput(new SoundSystemConfig { SfxVoices = 2 }, 1024 * 1024, acoustic));
                SoundSystem.PlayAt("fx.3d", Vector3.one);
                yield return null;

                AudioSettings.Reset(AudioSettings.GetConfiguration());
                yield return null;
                yield return null;
                SoundSystem.PlayAt("fx.3d", Vector3.one);

                int playing = 0;
                for (int i = 0; i < 2; i++)
                {
                    var source = SoundSystem.Current.SourceForTest(i);
                    if (source.isPlaying && source.clip != null && source.clip.name == "Bun3 native SFX driver" && source.clip.samples > 0)
                        playing++;
                }
                Assert.That(playing, Is.EqualTo(1), "The positional voice plays a live driver clip after the reset.");
            }
            finally
            {
                SoundSystem.Shutdown();
                Object.Destroy(catalog);
                Object.Destroy(fx);
                Object.Destroy(clip);
            }
        }
    }
}
