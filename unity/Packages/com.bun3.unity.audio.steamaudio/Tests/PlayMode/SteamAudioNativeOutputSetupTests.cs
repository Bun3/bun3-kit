using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    /// <summary>Verifies the native pooled SFX assembly through the static SoundSystem entry point.</summary>
    public sealed class SteamAudioNativeOutputSetupTests
    {
        private const int Voices = 3;
        private PlanarAcousticOutputs _acoustic;
        private AudioClip _clip3d, _clip2d;
        private SoundDef _fx3d, _fx2d;
        private SoundCatalog _catalog;

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
            SteamAudioSoundSetup.ApplyNativeOutput(new SoundSystemConfig { SfxVoices = Voices }, _acoustic);

        private static AudioSource FindPlaying(AudioClip clip)
        {
            for (int i = 0; i < Voices; i++)
            {
                var source = SoundSystem.Current.SourceForTest(i);
                if (source.clip == clip && source.isPlaying) return source;
            }
            return null;
        }

        [Test]
        public void ApplyNativeOutput_WiresConfig()
        {
            var config = new SoundSystemConfig { OnDisposed = () => { } };
            Assert.That(SteamAudioSoundSetup.ApplyNativeOutput(config, _acoustic), Is.SameAs(config));
            Assert.That(config.OcclusionChecksPerFrame, Is.EqualTo(0));
            Assert.That(config.CreateVoiceOutput, Is.Not.Null);
            Assert.That(config.PrepareDefinition, Is.Null, "Native output no longer decodes clips.");
            Assert.That(config.OnDisposed.GetInvocationList(), Has.Length.EqualTo(2), "The existing disposal hook is kept.");

            Assert.Throws<InvalidOperationException>(() => SteamAudioSoundSetup.ApplyNativeOutput(config, _acoustic));
            Assert.That(config.OnDisposed.GetInvocationList(), Has.Length.EqualTo(2), "A rejected call changes nothing.");
            Assert.Throws<ArgumentNullException>(() => SteamAudioSoundSetup.ApplyNativeOutput(null, _acoustic));
            Assert.Throws<ArgumentNullException>(() => SteamAudioSoundSetup.ApplyNativeOutput(new SoundSystemConfig(), null));
        }

        [Test]
        public void InitializedSystem_RegistersOneBindingPerVoice()
        {
            SoundSystem.Initialize(_catalog, Config());
            Assert.That(_acoustic.Items.Count, Is.EqualTo(Voices));
            Assert.That(_acoustic.Items, Has.All.InstanceOf<PlanarAcousticSfxBinding>());
        }

        [Test]
        public void PlayAt_SpatializesAuthoredClip_Play_StaysPlain()
        {
            SoundSystem.Initialize(_catalog, Config());
            SoundSystem.PlayAt("fx.3d", new Vector3(2, 0, 0));
            var positional = FindPlaying(_clip3d);
            Assert.That(positional, Is.Not.Null, "The positional voice plays its authored clip.");
            Assert.That(positional.spatialize, Is.True);
            SoundSystem.Play("fx.2d");
            var plain = FindPlaying(_clip2d);
            Assert.That(plain, Is.Not.Null, "The 2D voice plays its authored clip.");
            Assert.That(plain.spatialize, Is.False);
        }

        [Test]
        public void Shutdown_UnregistersBindings()
        {
            SoundSystem.Initialize(_catalog, Config());
            SoundSystem.Shutdown();
            Assert.That(_acoustic.Items.Count, Is.EqualTo(0));
        }

        [Test]
        public void ReinitializeWithSameConfig_RegistersFreshBindings()
        {
            var config = Config();
            SoundSystem.Initialize(_catalog, config);
            SoundSystem.Shutdown();
            SoundSystem.Initialize(_catalog, config);
            Assert.That(_acoustic.Items.Count, Is.EqualTo(Voices));
            SoundSystem.PlayAt("fx.3d", Vector3.one);
            Assert.That(FindPlaying(_clip3d), Is.Not.Null, "PlayAt works after reinitializing.");
        }
    }
}
