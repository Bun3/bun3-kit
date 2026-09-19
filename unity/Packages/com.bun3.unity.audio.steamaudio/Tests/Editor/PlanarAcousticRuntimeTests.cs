using System;
using System.Collections.Generic;
using Bun3.Unity.Acoustics;
using Bun3.Unity.Audio.SteamAudio.Editor;
using NUnit.Framework;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class PlanarAcousticRuntimeTests
    {
        private SA.Context context;
        private SteamAudioAcousticAsset asset;
        private PlanarAcousticMap map;

        private sealed class Settings : PlanarAcousticSettings
        {
            private readonly int capacity;
            internal Settings(int capacity) { this.capacity = capacity; }
            public override int SourceCapacity => capacity;
            public override float OcclusionRadius => 0;
            public override int OcclusionSamples => 1;
            public override float ObstructedPathGain => 1;
            internal float Near, Far;
            public override float MonoDistance => Near;
            public override float FullSpatialDistance => Far;
        }

        private sealed class FakeOutput : IPlanarAcousticOutput, IPlanarAcousticDiagnostics
        {
            internal PlanarAcousticSourceHandle Handle;
            internal int Detaches;
            public bool RequiresSimulation => false;
            public PlanarAcousticSourceHandle SourceHandle => Handle;
            public string Label => "fixture";
            public Vector2 Position => new Vector2(-2, 0);
            public bool IsBlocked => false;
            public bool DistanceAttenuation => true;
            public DistanceAttenuationProfile AttenuationProfile => null;
            public float MinimumDistance => 1;
            public float MaximumDistance => 10;
            public void Prepare(PlanarAcousticWorld world) { }
            public void Publish(PlanarAcousticWorld world) { }
            public void Gate(PlanarAcousticWorld world, Vector2 listener) { }
            public void Block() { }
            public void BlockAndDetach() { Detaches++; }
        }

        [OneTimeSetUp]
        public void CreateFixture()
        {
            context = new SA.Context();
            asset = SteamAudioAcousticBaker.Bake(context,
                new[] { V(-5, -2, -10), V(-5, 2, -10), V(5, 2, -10), V(5, -2, -10) },
                new[] { new SA.Triangle { index0 = 0, index1 = 1, index2 = 2 },
                    new SA.Triangle { index0 = 0, index1 = 2, index2 = 3 } },
                new[] { 0, 0 }, new[] { new SA.Material() },
                new[] { Sphere(-2), Sphere(0), Sphere(2) }, SteamAudioPathBakeSettings.Default,
                "planar-runtime-tests");
            map = ScriptableObject.CreateInstance<PlanarAcousticMap>();
            map.Initialize(asset, new bool[5, 1],
                new AcousticGridFrame(new Vector3(-2.5f, 0, .5f), Vector3.right, Vector3.back, Vector3.up),
                -1, 1, 0);
        }

        [OneTimeTearDown]
        public void DestroyFixture()
        {
            if (map != null) UnityEngine.Object.DestroyImmediate(map);
            if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            context?.Release();
        }

        [Test]
        public void NearFieldWidthUsesNativeDistanceAndRefreshesLiveSettings()
        {
            var settings = new Settings(1) { Near = 1, Far = 3 };
            using var world = new PlanarAcousticWorld(map, settings);
            Assert.That(world.GetSpatialBlend(default), Is.EqualTo(1));
            Assert.That(world.TryRegisterSource(new Vector2(-2, 0), out var source), Is.True);
            Assert.That(world.GetSpatialBlend(source), Is.EqualTo(1), "No native measurements means no invented distance.");
            world.Tick(new Vector2(2, 0), 0);
            Assert.That(world.TryGetNativePathDistances(source, out float distance, out _, out _, out _), Is.True);
            Assert.That(world.GetSpatialBlend(source), Is.EqualTo(settings.EvaluateSpatialBlend(distance)));
            settings.Near = distance + 1; settings.Far = distance + 2;
            Assert.That(world.GetSpatialBlend(source), Is.Zero);
            settings.Far = 0;
            Assert.That(world.GetSpatialBlend(source), Is.EqualTo(1));
            var profile = ScriptableObject.CreateInstance<SpatialBlendProfile>();
            try
            {
                profile.MonoDistance = distance + 1; profile.FullSpatialDistance = distance + 2;
                Assert.That(world.GetSpatialBlend(source, profile, 0, 0), Is.Zero);
                Assert.That(world.GetSpatialBlend(source, null, distance + 1, distance + 2), Is.Zero);
                Assert.That(world.GetSpatialBlend(source, null, 0, 0), Is.EqualTo(1));
                profile.FullSpatialDistance = 0;
                Assert.That(world.GetSpatialBlend(source, profile, 20, 30), Is.EqualTo(1), "Selected disabled profile wins over fallback scalars.");
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void RegistrationIsBoundedAndRecycledGenerationRejectsStaleHandle()
        {
            using var world = new PlanarAcousticWorld(map, new Settings(1));
            Assert.That(world.TryRegisterSource(new Vector2(-2, 0), out var first), Is.True);
            Assert.That(world.TryRegisterSource(Vector2.zero, out _), Is.False);
            Assert.That(world.RemoveSource(first), Is.True);
            Assert.That(first.IsValid, Is.False);
            Assert.That(world.TryRegisterSource(Vector2.zero, out var replacement), Is.True);
            Assert.That(replacement.IsValid, Is.True);
            Assert.That(world.SetSourcePosition(first, Vector2.one), Is.False);
            Assert.That(world.RemoveSource(first), Is.False);
        }

        [Test]
        public void BindingAppliesAuthoredCurveAndDetachesWhenWorldChanges()
        {
            using var first = new PlanarAcousticWorld(map, new Settings(1));
            using var replacement = new PlanarAcousticWorld(map, new Settings(1));
            var binding = new PlanarAcousticBinding();
            var profile = ScriptableObject.CreateInstance<DistanceAttenuationProfile>();
            try
            {
                binding.Prepare(first, new Vector2(-2, 0), true);
                var oldHandle = binding.Handle;
                first.SetSourceDistanceAttenuationEnabled(oldHandle, false);
                first.Tick(new Vector2(2, 0), 0);
                Assert.That(binding.TryGet(first, out _), Is.True);
                float baseline = binding.Coefficients[0];
                Assert.That(Mathf.Abs(baseline), Is.GreaterThan(.000001f));

                profile.VolumeByDistance = AnimationCurve.Linear(0, 1, 10, 0);
                binding.ApplyDistanceProfile(true, profile);
                first.Tick(new Vector2(2, 0), 1);
                Assert.That(binding.TryGet(first, out _), Is.True);
                Assert.That(first.TryGetNativePathDistances(oldHandle, out float distance, out _, out _, out _), Is.True);
                Assert.That(binding.Coefficients[0], Is.EqualTo(baseline * profile.GetSnapshot().Evaluate(distance)).Within(.00001f));

                binding.Prepare(replacement, Vector2.zero, true);
                Assert.That(oldHandle.IsValid, Is.False);
                Assert.That(binding.Handle.IsValid, Is.True);
                Assert.That(binding.TryGet(first, out _), Is.False);
            }
            finally
            {
                binding.Detach();
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void OutputRegistryDetachesAtOwnershipBoundaries()
        {
            var outputs = new PlanarAcousticOutputs();
            var output = new FakeOutput();
            outputs.Register(output);
            outputs.Register(output);
            Assert.That(outputs.Items.Count, Is.EqualTo(1));
            Assert.That(output.Detaches, Is.EqualTo(1));
            outputs.Detach();
            Assert.That(output.Detaches, Is.EqualTo(2));
            outputs.Unregister(output);
            Assert.That(output.Detaches, Is.EqualTo(3));
            Assert.That(outputs.Items, Is.Empty);
        }

        [Test]
        public void DiagnosticsCaptureSkipsInvalidSourcesAndCopiesSafePublicState()
        {
            using var world = new PlanarAcousticWorld(map, new Settings(1));
            Assert.That(world.TryRegisterSource(new Vector2(-2, 0), out var handle), Is.True);
            world.Tick(new Vector2(2, 0), 0);
            var valid = new FakeOutput { Handle = handle };
            var invalid = new FakeOutput();
            IReadOnlyList<IPlanarAcousticOutput> outputs = new IPlanarAcousticOutput[] { invalid, valid };
            var snapshot = PlanarAcousticDiagnostics.Capture(world, outputs, "fixture-scene");
            Assert.That(snapshot.scene, Is.EqualTo("fixture-scene"));
            Assert.That(snapshot.listener, Is.EqualTo(new Vector3(2, 0, 0)));
            Assert.That(snapshot.voices, Has.Length.EqualTo(1));
            Assert.That(snapshot.voices[0].name, Is.EqualTo("fixture"));
            Assert.That(snapshot.voices[0].position, Is.EqualTo(new Vector3(-2, 0, 0)));
            Assert.That(snapshot.voices[0].routeCovered, Is.True);
            Assert.That(snapshot.voices[0].curve, Is.Not.Null);
            Assert.That(snapshot.voices[0].maximum, Is.EqualTo(10));
            Assert.Throws<ArgumentException>(() => PlanarAcousticDiagnostics.Capture(null, outputs, "fixture-scene"));
        }

        [Test]
        public void SessionRetainsInitialFailureAndPublicRetryRecoversUntilDisposed()
        {
            int attempts = 0;
            var failure = new InvalidOperationException("initial factory failure");
            PlanarAcousticWorld created = null;
            var session = new PlanarAcousticSessionScope(new PlanarAcousticOutputs(), () =>
            {
                attempts++;
                if (attempts == 1) throw failure;
                return created = new PlanarAcousticWorld(map, new Settings(1));
            }, null, null);
            try
            {
                Assert.That(session.World, Is.Null);
                Assert.That(session.LastInitializationError, Is.SameAs(failure));
                Assert.That(session.TryReinitialize(), Is.True);
                Assert.That(session.World, Is.SameAs(created));
                Assert.That(session.LastInitializationError, Is.Null);
                Assert.That(attempts, Is.EqualTo(2));
            }
            finally { session.Dispose(); }
            Assert.That(created.IsDisposed, Is.True);
            Assert.That(session.World, Is.Null);
            Assert.That(session.TryReinitialize(), Is.False);
            Assert.That(attempts, Is.EqualTo(2));
        }

        private static SA.Vector3 V(float x, float y, float z) => new SA.Vector3 { x = x, y = y, z = z };

        private static SA.Sphere Sphere(float x) => new SA.Sphere { center = V(x, 0, 0), radius = .6f };
    }
}
