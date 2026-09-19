using NUnit.Framework;
using UnityEngine;
using Bun3.Unity.Audio;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio.Tests
{
    public sealed class DissonanceSharedAcousticSettingsTests
    {
        private sealed class ResolvedSettings : IResolvedSoundAcousticSettings
        {
            public bool IsAvailable { get; set; }
            public SoundAcousticSettings Acoustics { get; set; }
        }

        private sealed class LegacySettings : IPlanarVoiceSettings, IPlanarVoiceSpatialSettings
        {
            public bool IsAvailable { get; set; } = true;
            public bool DistanceAttenuation { get; set; } = true;
            public DistanceAttenuationProfile AttenuationProfile { get; set; }
            public SpatialBlendProfile SpatialBlendProfile { get; set; }
        }

        private sealed class UnavailableSettings : IResolvedSoundAcousticSettings
        {
            public bool IsAvailable => false;
            public SoundAcousticSettings Acoustics => throw new System.InvalidOperationException(
                "Unavailable settings must not resolve game configuration.");
        }

        [Test]
        public void FromAcousticsAcceptsSharedSettings()
        {
            var owner = new GameObject("Shared voice acoustics fixture");
            try
            {
                var playback = owner.AddComponent<DissonanceSteamAudioPlayback>();
                using var output = DissonancePlanarAcousticOutput.FromAcoustics(playback, new ResolvedSettings
                {
                    IsAvailable = true,
                    Acoustics = new SoundAcousticSettings
                    {
                        DistanceAttenuation = false,
                        MinDistance = 3,
                        MaxDistance = 18,
                        InheritSpatialBlend = false,
                        MonoDistance = 0,
                        FullSpatialDistance = 6,
                    },
                });

                Assert.That(output.DistanceAttenuation, Is.False);
                Assert.That(output.MinimumDistance, Is.EqualTo(3));
                Assert.That(output.MaximumDistance, Is.EqualTo(18));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void SharedSettingsRemainLiveAndAllocationFree()
        {
            var owner = new GameObject("Live shared voice acoustics fixture");
            try
            {
                var settings = new ResolvedSettings
                {
                    IsAvailable = true,
                    Acoustics = new SoundAcousticSettings
                    {
                        DistanceAttenuation = false,
                        MinDistance = 3,
                        MaxDistance = 18,
                        InheritSpatialBlend = false,
                        MonoDistance = 0,
                        FullSpatialDistance = 6,
                    },
                };
                using var output = DissonancePlanarAcousticOutput.FromAcoustics(
                    owner.AddComponent<DissonanceSteamAudioPlayback>(), settings);
                var changed = settings.Acoustics;
                changed.MinDistance = 4;
                changed.MaxDistance = 22;
                settings.Acoustics = changed;

                Assert.That(output.MinimumDistance, Is.EqualTo(4));
                Assert.That(output.MaximumDistance, Is.EqualTo(22));
                float observed = 0;
                Assert.That(() =>
                {
                    for (int i = 0; i < 1000; i++) observed += output.MinimumDistance;
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
                Assert.That(observed, Is.EqualTo(4000));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void LegacySettingsPreserveDistanceDefaults()
        {
            var owner = new GameObject("Legacy voice defaults fixture");
            try
            {
                using var output = new DissonancePlanarAcousticOutput(
                    owner.AddComponent<DissonanceSteamAudioPlayback>(), new LegacySettings());

                Assert.That(output.MinimumDistance, Is.EqualTo(1));
                Assert.That(output.MaximumDistance, Is.EqualTo(15));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void UnavailableSharedSettingsRemainBlocked()
        {
            var owner = new GameObject("Unavailable voice acoustics fixture");
            try
            {
                using var output = DissonancePlanarAcousticOutput.FromAcoustics(
                    owner.AddComponent<DissonanceSteamAudioPlayback>(), new UnavailableSettings());

                output.Prepare(null);
                output.Gate(null, Vector2.zero);
                Assert.That(output.IsBlocked, Is.True);
                Assert.That(output.SourceHandle.IsValid, Is.False);
                Assert.That(output.DistanceAttenuation, Is.True);
                Assert.That(output.AttenuationProfile, Is.Null);
                Assert.That(output.MinimumDistance, Is.EqualTo(1));
                Assert.That(output.MaximumDistance, Is.EqualTo(30));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void LegacyConstructorStillAcceptsLiteralNull()
        {
            var owner = new GameObject("Legacy voice acoustics fixture");
            try
            {
                var playback = owner.AddComponent<DissonanceSteamAudioPlayback>();
                Assert.Throws<System.ArgumentNullException>(() =>
                    new DissonancePlanarAcousticOutput(playback, null));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
