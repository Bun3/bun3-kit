using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    /// <summary>Verifies the native pooled SFX assembly through the static SoundSystem entry point.</summary>
    public sealed class SteamAudioNativeOutputSetupTests
    {
        private const int Voices = 3;
        private const long Budget = 1024 * 1024;
        private PlanarAcousticOutputs _acoustic;
        private AudioClip _clip3d, _clip2d;
        private SoundDef _fx3d, _fx2d;
        private SoundCatalog _catalog;
        private SteamAudioSoundOutput _heldReader;

        private static int HostCount => Resources.FindObjectsOfTypeAll<SteamAudioSoundRetirementHost>().Length;

        private static bool IsDriver(AudioClip clip) => clip != null && clip.name == "Bun3 native SFX driver";

        [SetUp]
        public void SetUp()
        {
            _acoustic = new PlanarAcousticOutputs();
            // One second keeps the positional voice active for a few frames.
            _clip3d = AudioClip.Create("fx.3d clip", 48000, 1, 48000, false);
            _clip2d = AudioClip.Create("fx.2d clip", 4800, 1, 48000, false);
            _fx3d = Def(_clip3d, SpatialMode.Positional);
            _fx2d = Def(_clip2d, SpatialMode.None);
            _catalog = ScriptableObject.CreateInstance<SoundCatalog>();
            _catalog.SetEntries(new[] { new SoundCatalogEntry("fx.3d", _fx3d), new SoundCatalogEntry("fx.2d", _fx2d) });
        }

        [TearDown]
        public void TearDown()
        {
            _heldReader?.ReleaseReader();
            _heldReader = null;
            SoundSystem.Shutdown();
            Object.Destroy(_catalog);
            Object.Destroy(_fx3d);
            Object.Destroy(_fx2d);
            Object.Destroy(_clip3d);
            Object.Destroy(_clip2d);
        }

        private static SoundDef Def(AudioClip clip, SpatialMode spatial)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { clip };
            def.Spatial = spatial;
            return def;
        }

        private SoundSystemConfig Config() =>
            SteamAudioSoundSetup.ApplyNativeOutput(new SoundSystemConfig { SfxVoices = Voices }, Budget, _acoustic);

        private static List<AudioClip> SourceClips()
        {
            var clips = new List<AudioClip>(Voices);
            for (int i = 0; i < Voices; i++) clips.Add(SoundSystem.Current.SourceForTest(i).clip);
            return clips;
        }

        [Test]
        public void ApplyNativeOutput_WiresConfig()
        {
            var config = new SoundSystemConfig { PrepareDefinition = _ => { }, OnDisposed = () => { } };
            Assert.That(SteamAudioSoundSetup.ApplyNativeOutput(config, Budget, _acoustic), Is.SameAs(config));
            Assert.That(config.OcclusionChecksPerFrame, Is.EqualTo(0));
            Assert.That(config.CreateVoiceOutput, Is.Not.Null);
            Assert.That(config.PrepareDefinition.GetInvocationList(), Has.Length.EqualTo(2), "The existing prepare hook is kept.");
            Assert.That(config.OnDisposed.GetInvocationList(), Has.Length.EqualTo(2), "The existing disposal hook is kept.");

            Assert.Throws<InvalidOperationException>(() => SteamAudioSoundSetup.ApplyNativeOutput(config, Budget, _acoustic));
            Assert.That(config.PrepareDefinition.GetInvocationList(), Has.Length.EqualTo(2), "A rejected call changes nothing.");
            Assert.Throws<ArgumentNullException>(() => SteamAudioSoundSetup.ApplyNativeOutput(null, Budget, _acoustic));
            Assert.Throws<ArgumentNullException>(() => SteamAudioSoundSetup.ApplyNativeOutput(new SoundSystemConfig(), Budget, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => SteamAudioSoundSetup.ApplyNativeOutput(new SoundSystemConfig(), 0, _acoustic));
        }

        [Test]
        public void InitializedSystem_RegistersOneBindingPerVoice()
        {
            SoundSystem.Initialize(_catalog, Config());
            Assert.That(_acoustic.Items.Count, Is.EqualTo(Voices));
            Assert.That(_acoustic.Items, Has.All.InstanceOf<PlanarAcousticSfxBinding>());
        }

        [Test]
        public void PlayAt_UsesNativeDriver_Play_UsesAuthoredClip()
        {
            SoundSystem.Initialize(_catalog, Config());
            SoundSystem.PlayAt("fx.3d", new Vector3(2, 0, 0));
            Assert.That(SourceClips().FindAll(IsDriver), Has.Count.EqualTo(1), "The positional voice plays the native driver clip.");
            SoundSystem.Play("fx.2d");
            Assert.That(SourceClips(), Has.Member(_clip2d), "The 2D voice plays its authored clip.");
        }

        [UnityTest]
        public IEnumerator Shutdown_UnregistersBindingsAndReleasesRuntime()
        {
            yield return null;
            int before = HostCount;
            SoundSystem.Initialize(_catalog, Config());
            Assert.That(HostCount - before, Is.EqualTo(1), "All voices share one runtime.");
            SoundSystem.Shutdown();
            Assert.That(_acoustic.Items.Count, Is.EqualTo(0));
            yield return null;
            Assert.That(HostCount, Is.EqualTo(before), "Shutdown releases the shared runtime.");
        }

        [UnityTest]
        public IEnumerator ShutdownWhileReaderHeld_KeepsHostUntilRelease()
        {
            yield return null;
            int before = HostCount;
            var outputs = new List<SteamAudioSoundOutput>(Voices);
            var config = Config();
            var create = config.CreateVoiceOutput;
            config.CreateVoiceOutput = source =>
            {
                var output = (SteamAudioSoundOutput)create(source);
                outputs.Add(output);
                return output;
            };
            SoundSystem.Initialize(_catalog, config);
            SoundSystem.PlayAt("fx.3d", Vector3.one);
            var started = outputs.Find(output => output.CurrentParameters.IsValid);
            Assert.That(started, Is.Not.Null);
            // The audio thread can hold the claim for one callback.
            Assert.That(SpinWait.SpinUntil(started.TryClaimReader, 1000), Is.True);
            _heldReader = started;

            SoundSystem.Shutdown();
            Assert.That(_acoustic.Items.Count, Is.EqualTo(0));
            yield return null;
            Assert.That(HostCount - before, Is.EqualTo(1), "A held reader keeps the retirement host alive.");

            started.ReleaseReader();
            _heldReader = null;
            yield return null;
            yield return null;
            Assert.That(HostCount, Is.EqualTo(before), "The host is destroyed once the last output finalizes.");
        }

        [Test]
        public void ReinitializeWithSameConfig_BuildsFreshRuntime()
        {
            var config = Config();
            SoundSystem.Initialize(_catalog, config);
            SoundSystem.PlayAt("fx.3d", Vector3.one);
            var oldDriver = SourceClips().Find(IsDriver);
            SoundSystem.Shutdown();

            SoundSystem.Initialize(_catalog, config);
            Assert.That(_acoustic.Items.Count, Is.EqualTo(Voices));
            SoundSystem.PlayAt("fx.3d", Vector3.one);
            var newDriver = SourceClips().Find(IsDriver);
            Assert.That(newDriver, Is.Not.Null, "PlayAt works after reinitializing.");
            Assert.That(newDriver, Is.Not.SameAs(oldDriver), "Reinitializing builds a fresh runtime.");
        }
    }
}
