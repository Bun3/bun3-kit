using System;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Audio.Tests
{
    public sealed class SoundAcousticSettingsTests
    {
        [Test]
        public void Default_UsesLegacySfxValues()
        {
            var settings = SoundAcousticSettings.Default;

            Assert.That(settings.DistanceAttenuation, Is.True);
            Assert.That(settings.AttenuationProfile, Is.Null);
            Assert.That(settings.MinDistance, Is.EqualTo(1f));
            Assert.That(settings.MaxDistance, Is.EqualTo(30f));
            Assert.That(settings.InheritSpatialBlend, Is.True);
            Assert.That(settings.SpatialBlendProfile, Is.Null);
            Assert.That(settings.MonoDistance, Is.EqualTo(1f));
            Assert.That(settings.FullSpatialDistance, Is.EqualTo(3f));
        }

        [Test]
        public void Resolve_ProfileTakesPrecedence_AndClearingItRestoresLocalValues()
        {
            var selection = new SoundAcousticSelection { Local = SoundAcousticSettings.Default };
            var local = selection.Local;
            local.DistanceAttenuation = false;
            selection.Local = local;
            var profile = ScriptableObject.CreateInstance<SoundAcousticProfile>();
            try
            {
                selection.Profile = profile;
                Assert.That(selection.Resolve().DistanceAttenuation, Is.True);

                selection.Profile = null;
                Assert.That(selection.Resolve().DistanceAttenuation, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Resolve_DestroyedProfileReference_FallsBackToLocalValues()
        {
            var selection = new SoundAcousticSelection { Local = SoundAcousticSettings.Default };
            var local = selection.Local;
            local.MaxDistance = 73f;
            selection.Local = local;
            var profile = ScriptableObject.CreateInstance<SoundAcousticProfile>();
            try
            {
                selection.Profile = profile;
                UnityEngine.Object.DestroyImmediate(profile);

                Assert.That(selection.Resolve().MaxDistance, Is.EqualTo(73f));
            }
            finally
            {
                if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Resolve_WarmedLoop_AllocatesNoManagedMemory()
        {
            var selection = new SoundAcousticSelection { Local = SoundAcousticSettings.Default };
            var retained = selection.Resolve();
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (var i = 0; i < 1_000; i++)
            {
                retained = selection.Resolve();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(retained.MaxDistance, Is.EqualTo(30f));
            Assert.That(allocated, Is.Zero);
        }

    }
}
