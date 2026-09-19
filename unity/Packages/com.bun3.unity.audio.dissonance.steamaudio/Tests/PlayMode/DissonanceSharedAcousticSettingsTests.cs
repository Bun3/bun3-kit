using NUnit.Framework;
using UnityEngine;
using Bun3.Unity.Audio;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System.Reflection;
using Bun3.Unity.Acoustics;
using Bun3.Unity.Audio.SteamAudio;
using SA = global::SteamAudio;

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

        private sealed class WorldSettings : PlanarAcousticSettings
        {
            public override int SourceCapacity => 2;
            public override float OcclusionRadius => 0;
            public override int OcclusionSamples => 1;
            public override float ObstructedPathGain => 1;
            public float Near { get; set; }
            public float Far { get; set; }
            public override float MonoDistance => Near;
            public override float FullSpatialDistance => Far;
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
        public void WidthRoutingDistinguishesLegacyInheritanceFromExplicitMonoZero()
        {
            var legacyOwner = new GameObject("Legacy inherited width fixture");
            var sharedOwner = new GameObject("Shared explicit width fixture");
            SteamAudioAcousticAsset asset = null;
            PlanarAcousticMap map = null;
            try
            {
                legacyOwner.transform.position = new Vector3(-2, 0, 0);
                sharedOwner.transform.position = new Vector3(-2, 0, 0);
                var legacy = new LegacySettings();
                var shared = new ResolvedSettings
                {
                    IsAvailable = true,
                    Acoustics = new SoundAcousticSettings
                    {
                        DistanceAttenuation = true,
                        MinDistance = 3,
                        MaxDistance = 18,
                        InheritSpatialBlend = false,
                        MonoDistance = 0,
                        FullSpatialDistance = 0,
                    },
                };
                using var legacyOutput = new DissonancePlanarAcousticOutput(
                    legacyOwner.AddComponent<DissonanceSteamAudioPlayback>(), legacy);
                using var sharedOutput = DissonancePlanarAcousticOutput.FromAcoustics(
                    sharedOwner.AddComponent<DissonanceSteamAudioPlayback>(), shared);
                var context = new SA.Context();
                try
                {
                    asset = CreateAcousticAsset(context);
                    map = CreateAcousticMap(asset);
                    var worldSettings = new WorldSettings { Near = 1, Far = 3 };
                    using var world = new PlanarAcousticWorld(map, worldSettings);
                    AttachForWidthTest(legacyOutput, world, new Vector2(-2, 0));
                    AttachForWidthTest(sharedOutput, world, new Vector2(-2, 0));
                    world.Tick(new Vector2(2, 0), 0);
                    Assert.That(world.TryGetNativePathDistances(legacyOutput.SourceHandle,
                        out float distance, out _, out _, out _), Is.True);
                    worldSettings.Near = distance + 1;
                    worldSettings.Far = distance + 2;

                    Assert.That(legacyOutput.GetSpatialBlend(world), Is.Zero,
                        "A legacy null width profile must inherit the world's mono range.");
                    Assert.That(sharedOutput.GetSpatialBlend(world), Is.EqualTo(1),
                        "Explicit mono and full-spatial distances of zero must disable the mono range.");
                }
                finally
                {
                    if (map != null) Object.DestroyImmediate(map);
                    if (asset != null) Object.DestroyImmediate(asset);
                    context.Release();
                }
            }
            finally
            {
                Object.DestroyImmediate(legacyOwner);
                Object.DestroyImmediate(sharedOwner);
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

        static void AttachForWidthTest(DissonancePlanarAcousticOutput output,
            PlanarAcousticWorld world, Vector2 position)
        {
            var field = typeof(DissonancePlanarAcousticOutput).GetField("binding",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            ((PlanarAcousticBinding)field.GetValue(output)).Prepare(world, position, true);
        }

        static SteamAudioAcousticAsset CreateAcousticAsset(SA.Context context) => SteamAudioAcousticBaker.Bake(context,
            new[] { V(-5, -2, -10), V(-5, 2, -10), V(5, 2, -10), V(5, -2, -10) },
            new[] { new SA.Triangle { index0 = 0, index1 = 1, index2 = 2 },
                new SA.Triangle { index0 = 0, index1 = 2, index2 = 3 } },
            new[] { 0, 0 }, new[] { new SA.Material() },
            new[] { Sphere(-2), Sphere(0), Sphere(2) }, SteamAudioPathBakeSettings.Default, "voice-width-routing");

        static PlanarAcousticMap CreateAcousticMap(SteamAudioAcousticAsset asset)
        {
            var map = ScriptableObject.CreateInstance<PlanarAcousticMap>();
            map.Initialize(asset, new bool[5, 1],
                new AcousticGridFrame(new Vector3(-2.5f, 0, .5f), Vector3.right, Vector3.back, Vector3.up), -1, 1, 0);
            return map;
        }

        static SA.Vector3 V(float x, float y, float z) => new() { x = x, y = y, z = z };
        static SA.Sphere Sphere(float x) => new() { center = V(x, 0, 0), radius = .6f };

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
