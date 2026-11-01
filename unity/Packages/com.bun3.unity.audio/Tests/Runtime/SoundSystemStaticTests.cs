using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is =UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundSystemStaticTests
    {
        private static readonly Regex UninitializedWarning = new("before SoundSystem.Initialize");

        private sealed class RecordingOutput : ISpatialSoundVoiceOutput
        {
            private readonly AudioSource _source;
            private readonly List<(SoundDef Def, SpatialMode Spatial, Vector3 Position)> _records;

            internal RecordingOutput(AudioSource source, List<(SoundDef, SpatialMode, Vector3)> records)
            {
                _source = source;
                _records = records;
            }

            public VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch, SpatialMode spatial)
            {
                _records.Add((definition, spatial, _source.transform.position));
                return VoiceOutputStartResult.Unsupported;
            }

            public VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch)
                => VoiceOutputStartResult.Unsupported;

            public bool IsComplete => true;
            public void SetPitch(float logicalPitch) { }
            public void Retire() { }
            public void Dispose() { }
        }

        private readonly List<(SoundDef Def, SpatialMode Spatial, Vector3 Position)> _records = new(16);
        private AudioClip _clip;
        private SoundDef _fx2d, _fx3d, _bgm;
        private SoundCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _records.Clear();
            _clip = AudioClip.Create("t", 4410, 1, 44100, false);
            _fx2d = Def(SpatialMode.None);
            _fx3d = Def(SpatialMode.Positional);
            _bgm = Def(SpatialMode.None);
            _catalog = ScriptableObject.CreateInstance<SoundCatalog>();
            _catalog.SetEntries(new[]
            {
                new SoundCatalogEntry("fx.2d", _fx2d),
                new SoundCatalogEntry("fx.3d", _fx3d),
                new SoundCatalogEntry("bgm", _bgm),
            });
        }

        [TearDown]
        public void TearDown()
        {
            SoundSystem.Shutdown();
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_fx2d);
            Object.DestroyImmediate(_fx3d);
            Object.DestroyImmediate(_bgm);
            Object.DestroyImmediate(_clip);
        }

        private SoundDef Def(SpatialMode spatial)
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            def.Clips = new[] { _clip };
            def.Spatial = spatial;
            return def;
        }

        private SoundSystemConfig Config() => new()
        {
            SfxVoices = 4,
            CreateVoiceOutput = source => new RecordingOutput(source, _records),
        };

        [Test]
        public void StaticCallsBeforeInitialize_AreIgnoredWithOneEditorWarning()
        {
            LogAssert.Expect(LogType.Warning, UninitializedWarning);
            SoundSystem.Play("fx.2d");
            SoundSystem.PlayAt("fx.3d", Vector3.one);
            SoundSystem.PlayMusic("bgm");
            SoundSystem.StopMusic();
            SoundSystem.Play("fx.2d");
            LogAssert.NoUnexpectedReceived();
            Assert.That(SoundSystem.Current, Is.Null);
        }

        [Test]
        public void UnknownKey_IsIgnoredWithoutLog()
        {
            SoundSystem.Initialize(_catalog, Config());
            SoundSystem.Play("missing");
            Assert.That(_records, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Play_IsAlwaysTwoD_PlayAtFollowsDefinition()
        {
            SoundSystem.Initialize(_catalog, Config());
            SoundSystem.Play("fx.3d");
            SoundSystem.PlayAt("fx.3d", new Vector3(1, 2, 0));
            SoundSystem.PlayAt("fx.2d", Vector3.one);
            Assert.That(_records.Count, Is.EqualTo(3));
            Assert.That(_records[0].Def, Is.SameAs(_fx3d));
            Assert.That(_records[0].Spatial, Is.EqualTo(SpatialMode.None));
            Assert.That(_records[1].Def, Is.SameAs(_fx3d));
            Assert.That(_records[1].Spatial, Is.EqualTo(SpatialMode.Positional));
            Assert.That(_records[1].Position, Is.EqualTo(new Vector3(1, 2, 0)));
            Assert.That(_records[2].Def, Is.SameAs(_fx2d));
            Assert.That(_records[2].Spatial, Is.EqualTo(SpatialMode.None));
        }

        [Test]
        public void PlayMusic_SameKeyKeepsChannel_UnknownKeyStops()
        {
            SoundSystem.Initialize(_catalog, Config());
            var current = SoundSystem.Current;
            SoundSystem.PlayMusic("bgm");
            SoundSystem.PlayMusic("bgm");
            Assert.That(current.ActiveMusic, Is.EqualTo(0), "Same key must not crossfade to the other channel.");
            SoundSystem.PlayMusic("missing");
            Assert.That(current.IsMusicPlaying, Is.False);
            SoundSystem.PlayMusic("bgm");
            Assert.That(current.IsMusicPlaying, Is.True);
            SoundSystem.StopMusic();
            Assert.That(current.IsMusicPlaying, Is.False);
        }

        [Test]
        public void PlayMusic_UsesConfiguredFade()
        {
            var config = Config();
            config.MusicFadeSeconds = 0.5f;
            SoundSystem.Initialize(_catalog, config);
            var current = SoundSystem.Current;
            SoundSystem.PlayMusic("bgm");
            Assert.That(current.MusicChannels[0].State, Is.EqualTo(MusicState.FadingIn));
            current.TickMusic(0.4f);
            Assert.That(current.MusicChannels[0].State, Is.EqualTo(MusicState.FadingIn));
            current.TickMusic(0.2f);
            Assert.That(current.MusicChannels[0].State, Is.EqualTo(MusicState.Playing));
        }

        [Test]
        public void InitializeTwice_Throws_ShutdownAllowsReinitialize()
        {
            SoundSystem.Initialize(_catalog, Config());
            var first = SoundSystem.Current;
            Assert.That(() => SoundSystem.Initialize(_catalog, Config()), Throws.InvalidOperationException);
            Assert.That(SoundSystem.Current, Is.SameAs(first));
            SoundSystem.Shutdown();
            SoundSystem.Initialize(_catalog, Config());
            Assert.That(SoundSystem.Current, Is.Not.Null.And.Not.SameAs(first));
        }

        [Test]
        public void InitializeWithInvalidCatalog_ThrowsAndStaysUninitialized()
        {
            typeof(SoundCatalog).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_catalog, new[] { new SoundCatalogEntry("broken", null) });
            Assert.That(() => SoundSystem.Initialize(_catalog, Config()), Throws.ArgumentException);
            Assert.That(SoundSystem.Current, Is.Null);
        }

        [Test]
        public void Initialize_RunsPrepareHookPerDefinition_LogsFailureAndContinues()
        {
            var prepared = new List<SoundDef>();
            var config = Config();
            config.PrepareDefinition = def =>
            {
                prepared.Add(def);
                if (def == _fx3d) throw new ArgumentException("unpreparable");
            };
            LogAssert.Expect(LogType.Exception, new Regex("ArgumentException"));
            SoundSystem.Initialize(_catalog, config);
            Assert.That(prepared, Is.EqualTo(new[] { _fx2d, _fx3d, _bgm }));
            Assert.That(SoundSystem.Current, Is.Not.Null);
        }

        [Test]
        public void OnDisposed_RunsOnceOnShutdown()
        {
            var disposed = 0;
            var config = Config();
            config.OnDisposed = () => disposed++;
            SoundSystem.Initialize(_catalog, config);
            SoundSystem.Shutdown();
            Assert.That(disposed, Is.EqualTo(1));
            SoundSystem.Shutdown();
            Assert.That(disposed, Is.EqualTo(1));
        }

        [Test]
        public void OnDisposed_RunsOnceWhenConstructionFails()
        {
            var disposed = 0;
            var config = Config();
            config.OnSourceCreated = _ => throw new InvalidOperationException("source setup failed");
            config.OnDisposed = () => disposed++;
            Assert.That(() => SoundSystem.Initialize(_catalog, config), Throws.InvalidOperationException);
            Assert.That(disposed, Is.EqualTo(1));
            Assert.That(SoundSystem.Current, Is.Null);
        }

        [Test]
        public void DisposingCurrent_ClearsStaticState()
        {
            SoundSystem.Initialize(_catalog, Config());
            SoundSystem.Current.Dispose();
            Assert.That(SoundSystem.Current, Is.Null);
            LogAssert.Expect(LogType.Warning, UninitializedWarning);
            Assert.That(() => SoundSystem.Play("fx.2d"), Throws.Nothing);
        }

        [Test]
        public void StaticKeyPlayback_DoesNotAllocate()
        {
            SoundSystem.Initialize(_catalog, new SoundSystemConfig { SfxVoices = 8 });
            var position = new Vector3(1, 2, 0);
            TestDelegate play = () =>
            {
                SoundSystem.Play("fx.2d");
                SoundSystem.PlayAt("fx.3d", position);
            };
            // Warm the same delegate: Mono interns a lambda's string literals on its first run.
            play();

            Assert.That(() => GC.KeepAlive(new byte[1024]), Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(play, Is.Not.AllocatingGCMemory());

            var active = 0;
            foreach (var slot in SoundSystem.Current.Table.Slots)
            {
                if (slot.State != VoiceState.Idle) active++;
            }
            Assert.That(active, Is.EqualTo(4), "Measured calls must actually start voices.");
        }
    
        private Dictionary<SoundDef, int> CountHookCalls(out SoundSystemConfig config)
        {
            var counts = new Dictionary<SoundDef, int>();
            config = Config();
            config.PrepareDefinition = def => counts[def] = counts.TryGetValue(def, out var n) ? n + 1 : 1;
            return counts;
        }

        [Test]
        public void EditedClips_AreRepreparedOnNextTick()
        {
            var counts = CountHookCalls(out var config);
            SoundSystem.Initialize(_catalog, config);
            Assert.That(counts[_fx3d], Is.EqualTo(1));
            var other = AudioClip.Create("t2", 4410, 1, 44100, false);
            try
            {
                _fx3d.Clips = new[] { other };
                SoundSystem.Current.Tick(0.016f);
                Assert.That(counts[_fx3d], Is.EqualTo(2));
                Assert.That(counts[_fx2d], Is.EqualTo(1));
                SoundSystem.Current.Tick(0.016f);
                Assert.That(counts[_fx3d], Is.EqualTo(2));
            }
            finally { SoundSystem.Shutdown(); Object.DestroyImmediate(other); }
        }

        [Test]
        public void EditedSpatialMode_IsRepreparedOnNextTick()
        {
            var counts = CountHookCalls(out var config);
            SoundSystem.Initialize(_catalog, config);
            _fx3d.Spatial = SpatialMode.None;
            SoundSystem.Current.Tick(0.016f);
            Assert.That(counts[_fx3d], Is.EqualTo(2));
        }

        [Test]
        public void UnchangedDefinitionsCheck_DoesNotAllocate()
        {
            SoundSystem.Initialize(_catalog, Config());
            SoundSystem.Current.Tick(0.016f);
            Assert.That(() => SoundSystem.Current.Tick(0.016f), Is.Not.AllocatingGCMemory());
        }
}
}
