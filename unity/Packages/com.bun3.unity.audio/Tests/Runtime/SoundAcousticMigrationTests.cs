using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundAcousticMigrationTests
    {
        [Test]
        public void SoundDef_SpatialProfileWinsWhileInactiveLocalSettingsArePreserved()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var spatial = ScriptableObject.CreateInstance<SoundSpatialProfile>();
            var shared = ScriptableObject.CreateInstance<SoundAcousticProfile>();
            try
            {
                def.MinDistance = 3f;
                def.MaxDistance = 18f;
                def.DistanceAttenuation = false;
                def.MonoDistance = 2f;
                def.FullSpatialDistance = 6f;
                spatial.Acoustics.Profile = shared;
                shared.Settings = SoundAcousticSettings.Default;
                var sharedSettings = shared.Settings;
                sharedSettings.MinDistance = 7f;
                sharedSettings.MaxDistance = 70f;
                shared.Settings = sharedSettings;
                def.SpatialProfile = spatial;

                Assert.That(def.EffectiveAcoustics.MinDistance, Is.EqualTo(7f));
                Assert.That(def.EffectiveAcoustics.MaxDistance, Is.EqualTo(70f));
                Assert.That(def.Acoustics.Local.MinDistance, Is.EqualTo(3f));
                Assert.That(def.Acoustics.Local.MaxDistance, Is.EqualTo(18f));

                def.SpatialProfile = null;
                Assert.That(def.EffectiveAcoustics.MinDistance, Is.EqualTo(3f));
                Assert.That(def.EffectiveAcoustics.MaxDistance, Is.EqualTo(18f));
                Assert.That(def.EffectiveAcoustics.DistanceAttenuation, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(def);
                UnityEngine.Object.DestroyImmediate(spatial);
                UnityEngine.Object.DestroyImmediate(shared);
            }
        }

        [Test]
        public void LegacyPropertyWritesAfterUpgradeUpdateLocalWithoutDetachingSharedProfile()
        {
            var def = ScriptableObject.CreateInstance<SoundDef>();
            var profile = ScriptableObject.CreateInstance<SoundAcousticProfile>();
            try
            {
                _ = def.Acoustics;
                def.Acoustics.Profile = profile;

                def.MinDistance = 9f;
                def.DistanceAttenuation = false;

                Assert.That(def.Acoustics.Profile, Is.SameAs(profile));
                Assert.That(def.Acoustics.Local.MinDistance, Is.EqualTo(9f));
                Assert.That(def.Acoustics.Local.DistanceAttenuation, Is.False);
                Assert.That(def.EffectiveMinDistance, Is.EqualTo(1f));
                Assert.That(def.EffectiveDistanceAttenuation, Is.True);

                def.Acoustics.Profile = null;
                Assert.That(def.EffectiveMinDistance, Is.EqualTo(9f));
                Assert.That(def.EffectiveDistanceAttenuation, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(def);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

    }
}
