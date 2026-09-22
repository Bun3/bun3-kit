using NUnit.Framework;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SharedAcousticSettingsTests
    {
        private sealed class ResolvedSettings : IResolvedSoundAcousticSettings
        {
            public bool IsAvailable { get; set; }
            public SoundAcousticSettings Acoustics { get; set; }
        }

        [Test]
        public void SharedResolvedSettingsExposeAvailabilityAndCompleteAcoustics()
        {
            var expected = new SoundAcousticSettings
            {
                DistanceAttenuation = false,
                MinDistance = 3,
                MaxDistance = 18,
                InheritSpatialBlend = false,
                MonoDistance = 0,
                FullSpatialDistance = 6,
            };
            IResolvedSoundAcousticSettings settings = new ResolvedSettings
            {
                IsAvailable = true,
                Acoustics = expected,
            };

            Assert.That(settings.IsAvailable, Is.True);
            Assert.That(settings.Acoustics.DistanceAttenuation, Is.False);
            Assert.That(settings.Acoustics.MinDistance, Is.EqualTo(3));
            Assert.That(settings.Acoustics.MaxDistance, Is.EqualTo(18));
            Assert.That(settings.Acoustics.InheritSpatialBlend, Is.False);
            Assert.That(settings.Acoustics.MonoDistance, Is.Zero);
            Assert.That(settings.Acoustics.FullSpatialDistance, Is.EqualTo(6));
        }
    }
}
