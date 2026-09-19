using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class ExternalAudioRegistryTests
    {
        private GameObject _object;
        private AudioSource _source;
        private ExternalAudioRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _object = new GameObject("External audio test");
            _source = _object.AddComponent<AudioSource>();
            _source.volume = 0.8f;
            _registry = new ExternalAudioRegistry(1);
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Dispose();
            Object.DestroyImmediate(_object);
        }

        [Test]
        public void GainAndRelease_RestoreOriginalWithoutChangingPlaybackConfiguration()
        {
            var clip = AudioClip.Create("External", 64, 1, 44100, false);
            try
            {
                _source.clip = clip;
                _source.loop = true;
                _source.pitch = 1.3f;
                _source.mute = true;
                _source.spatialBlend = 0.4f;
                _source.transform.position = Vector3.one;
                var handle = _registry.Register(_source, new ExternalAudioSettings(gain: 0.5f));
                Assert.That(_source.volume, Is.EqualTo(0.5f));
                handle.SetGain(0.25f);
                Assert.That(_source.volume, Is.EqualTo(0.25f));
                handle.Release();
                handle.Release();
                Assert.That(_source.volume, Is.EqualTo(0.8f));
                Assert.That(_source.clip, Is.SameAs(clip));
                Assert.That(_source.loop, Is.True);
                Assert.That(_source.pitch, Is.EqualTo(1.3f));
                Assert.That(_source.mute, Is.True);
                Assert.That(_source.spatialBlend, Is.EqualTo(0.4f));
                Assert.That(_source.transform.position, Is.EqualTo(Vector3.one));
                Assert.That(_source.isPlaying, Is.False);
            }
            finally { Object.DestroyImmediate(clip); }
        }

        [Test]
        public void ActiveSource_KeepsPlayingThroughRegistrationReleaseAndDisposal()
        {
            var clip = AudioClip.Create("Playing external", 44100, 1, 44100, false);
            try
            {
                _source.clip = clip;
                _source.loop = true;
                _source.Play();
                Assert.That(_source.isPlaying, Is.True, "The fixture must be actively playing.");
                var handle = _registry.Register(_source, new ExternalAudioSettings(gain: 0.5f));
                handle.SetGain(0.25f);
                Assert.That(_source.isPlaying, Is.True);
                handle.Release();
                Assert.That(_source.isPlaying, Is.True);
                _registry.Register(_source, new ExternalAudioSettings(gain: 0.5f));
                _registry.Dispose();
                Assert.That(_source.isPlaying, Is.True);
            }
            finally
            {
                _source.Stop();
                _source.clip = null;
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void DefaultSettings_DoNotOwnGainOrFilter()
        {
            var filter = _object.AddComponent<AudioLowPassFilter>();
            var handle = _registry.Register(_source, default);
            Assert.Throws<InvalidOperationException>(() => handle.SetGain(0.5f));
            Assert.Throws<InvalidOperationException>(() => handle.SetLowPassCutoff(500f));
            _source.volume = 0.7f;
            filter.cutoffFrequency = 800f;
            handle.Release();
            Assert.That(_source.volume, Is.EqualTo(0.7f));
            Assert.That(filter.cutoffFrequency, Is.EqualTo(800f));
        }

        [Test]
        public void DuplicateRegistration_IsRejectedWithoutLosingOriginalOwnership()
        {
            var handle = _registry.Register(_source, new ExternalAudioSettings(gain: 0.5f));
            Assert.Throws<InvalidOperationException>(() => _registry.Register(_source, default));
            Assert.That(handle.IsValid, Is.True);
            handle.Release();
            Assert.That(_source.volume, Is.EqualTo(0.8f));
        }

        [Test]
        public void StaleHandle_CannotChangeOrReleaseReusedSlot()
        {
            var stale = _registry.Register(_source, new ExternalAudioSettings(gain: 0.5f));
            stale.Release();
            var current = _registry.Register(_source, new ExternalAudioSettings(gain: 0.6f));
            stale.SetGain(0f);
            stale.Release();
            Assert.That(stale.IsValid, Is.False);
            Assert.That(current.IsValid, Is.True);
            Assert.That(_source.volume, Is.EqualTo(0.6f));
        }

        [Test]
        public void DestroyedSource_IsInvalidAndItsSlotCanBeReused()
        {
            var stale = _registry.Register(_source, new ExternalAudioSettings(gain: 0.5f));
            Object.DestroyImmediate(_source);
            Assert.That(stale.IsValid, Is.False);
            _source = _object.AddComponent<AudioSource>();
            var current = _registry.Register(_source, new ExternalAudioSettings(gain: 0.7f));
            stale.Release();
            stale.SetGain(0f);
            Assert.That(current.IsValid, Is.True);
            Assert.That(_source.volume, Is.EqualTo(0.7f));
        }

        [Test]
        public void Dispose_RestoresAndInvalidatesAndRejectsNewRegistrations()
        {
            var handle = _registry.Register(_source, new ExternalAudioSettings(gain: 0.5f));
            _registry.Dispose();
            _registry.Dispose();
            handle.SetGain(0f);
            handle.Release();
            Assert.That(handle.IsValid, Is.False);
            Assert.That(_source.volume, Is.EqualTo(0.8f));
            Assert.Throws<ObjectDisposedException>(() => _registry.Register(_source, default));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        public void InvalidGain_IsRejectedWithoutMutation(float gain)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _registry.Register(_source, new ExternalAudioSettings(gain: gain)));
            var handle = _registry.Register(_source, new ExternalAudioSettings(gain: 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => handle.SetGain(gain));
            Assert.That(_source.volume, Is.EqualTo(0.5f));
        }

        [Test]
        public void Capacity_DoesNotStealExistingRegistration()
        {
            var handle = _registry.Register(_source, default);
            var other = new GameObject("Other");
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    _registry.Register(other.AddComponent<AudioSource>(), default));
                Assert.That(handle.IsValid, Is.True);
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test]
        public void MixerRouting_IsExplicitAndRestored()
        {
            var mixer = Resources.Load<AudioMixer>("Bun3DefaultAudioMixer");
            Assert.That(mixer, Is.Not.Null);
            var group = mixer.FindMatchingGroups("SFX")[0];
            _source.outputAudioMixerGroup = group;
            var handle = _registry.Register(_source, new ExternalAudioSettings(overrideMixerGroup: true));
            Assert.That(_source.outputAudioMixerGroup, Is.Null);
            handle.Release();
            Assert.That(_source.outputAudioMixerGroup, Is.SameAs(group));
            handle = _registry.Register(_source, default);
            _source.outputAudioMixerGroup = null;
            handle.Release();
            Assert.That(_source.outputAudioMixerGroup, Is.Null);
        }

        [Test]
        public void LowPass_RestoresEnabledAndCutoffWithoutRemovingComponent()
        {
            var filter = _object.AddComponent<AudioLowPassFilter>();
            filter.enabled = false;
            filter.cutoffFrequency = 17000f;
            var handle = _registry.Register(_source,
                new ExternalAudioSettings(lowPassFilter: filter, lowPassCutoff: 1000f));
            Assert.That(filter.enabled, Is.True);
            Assert.That(filter.cutoffFrequency, Is.EqualTo(1000f));
            handle.SetLowPassCutoff(600f);
            Assert.That(filter.cutoffFrequency, Is.EqualTo(600f));
            handle.Release();
            Assert.That(filter.enabled, Is.False);
            Assert.That(filter.cutoffFrequency, Is.EqualTo(17000f));
            Assert.That(_object.GetComponent<AudioLowPassFilter>(), Is.SameAs(filter));
        }

        [Test]
        public void InvalidCapacityAndNullSource_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExternalAudioRegistry(0));
            Assert.Throws<ArgumentNullException>(() => _registry.Register(null, default));
        }

        [Test]
        public void ForeignFilter_IsRejectedBeforeGainChanges()
        {
            var other = new GameObject("Foreign filter");
            try
            {
                other.AddComponent<AudioSource>();
                var filter = other.AddComponent<AudioLowPassFilter>();
                Assert.Throws<ArgumentException>(() => _registry.Register(_source,
                    new ExternalAudioSettings(gain: 0.2f, lowPassFilter: filter)));
                Assert.That(_source.volume, Is.EqualTo(0.8f));
            }
            finally { Object.DestroyImmediate(other); }
        }

        [Test]
        public void SharedFilter_CannotBeOwnedByTwoSourceRegistrations()
        {
            var filter = _object.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = 17000f;
            var otherSource = _object.AddComponent<AudioSource>();
            using var registry = new ExternalAudioRegistry(2);
            var first = registry.Register(_source,
                new ExternalAudioSettings(lowPassFilter: filter, lowPassCutoff: 1000f));
            Assert.Throws<InvalidOperationException>(() => registry.Register(otherSource,
                new ExternalAudioSettings(lowPassFilter: filter, lowPassCutoff: 500f)));
            Assert.That(filter.cutoffFrequency, Is.EqualTo(1000f));
            first.Release();
            Assert.That(filter.cutoffFrequency, Is.EqualTo(17000f));
        }

        [Test]
        public void OwnedControls_AfterWarmupDoNotAllocate()
        {
            var filter = _object.AddComponent<AudioLowPassFilter>();
            var handle = _registry.Register(_source,
                new ExternalAudioSettings(gain: 0.5f, lowPassFilter: filter));
            for (var i = 0; i < 16; i++)
            {
                handle.SetGain(0.5f);
                handle.SetLowPassCutoff(1000f);
            }
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                for (var i = 0; i < 1024; i++)
                {
                    handle.SetGain(0.5f);
                    handle.SetLowPassCutoff(1000f);
                }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(9f)]
        [TestCase(22001f)]
        public void InvalidLowPass_IsRejectedWithoutMutation(float cutoff)
        {
            var filter = _object.AddComponent<AudioLowPassFilter>();
            var handle = _registry.Register(_source, new ExternalAudioSettings(lowPassFilter: filter, lowPassCutoff: 1000f));
            Assert.Throws<ArgumentOutOfRangeException>(() => handle.SetLowPassCutoff(cutoff));
            Assert.That(filter.cutoffFrequency, Is.EqualTo(1000f));
        }
    }
}
