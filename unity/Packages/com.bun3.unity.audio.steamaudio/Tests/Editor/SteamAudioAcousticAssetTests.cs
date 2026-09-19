using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SA = SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioAcousticAssetTests
    {
        const string AssetPath = "Assets/SteamAudioAcousticAssetTest.asset";
        SA.Context context;
        ScriptableObject asset;
        bool ownsPersistedAsset;
        static Type Find(string name)
        {
            var type = Type.GetType("Bun3.Unity.Audio.SteamAudio." + name + ", Bun3.Unity.Audio.SteamAudio");
            Assert.That(type, Is.Not.Null, "A reusable persisted acoustic bake/load contract is required.");
            return type;
        }
        static T Get<T>(object owner, string name) => (T)owner.GetType().GetProperty(name).GetValue(owner);
        static SA.Vector3 V(float x, float y, float z) => new SA.Vector3 { x = x, y = y, z = z };
        static SA.CoordinateSpace3 Space(float x) => new SA.CoordinateSpace3
        { origin = V(x, 0, 0), right = V(1, 0, 0), up = V(0, 1, 0), ahead = V(0, 0, -1) };
        static SA.Sphere[] Probes() => new[]
        {
            new SA.Sphere { center = V(-2, 0, 0), radius = 0.6f },
            new SA.Sphere { center = V(-2, 0, 3), radius = 0.6f },
            new SA.Sphere { center = V(0, 0, 3), radius = 0.6f },
            new SA.Sphere { center = V(2, 0, 3), radius = 0.6f },
            new SA.Sphere { center = V(2, 0, 0), radius = 0.6f }
        };
        void Bake(SA.Sphere[] probes = null, string fingerprint = "generated-wall-fixture")
        {
            var baker = Find("SteamAudioAcousticBaker");
            var settings = Find("SteamAudioPathBakeSettings").GetProperty("Default").GetValue(null);
            context = new SA.Context();
            asset = (ScriptableObject)baker.GetMethod("Bake").Invoke(null, new object[]
            {
                context,
                new[] { V(0, -3, -10), V(0, 3, -10), V(0, 3, 2), V(0, -3, 2) },
                new[] { new SA.Triangle { index0 = 0, index1 = 1, index2 = 2 }, new SA.Triangle { index0 = 0, index1 = 2, index2 = 3 } },
                new[] { 0, 0 }, new[] { new SA.Material { absorptionLow = 1, absorptionMid = 1, absorptionHigh = 1 } },
                probes ?? Probes(), settings, fingerprint
            });
        }
        IDisposable Load() => (IDisposable)Activator.CreateInstance(Find("SteamAudioAcousticSceneScope"), context, asset);
        SteamAudioPathSimulationScope Simulator(object loaded)
        {
            var scope = new SteamAudioPathSimulationScope(Get<SA.Context>(loaded, "Context"),
                Get<SA.Scene>(loaded, "Scene"), Get<SA.ProbeBatch>(loaded, "Probes"), 1, 48000, 512);
            try
            {
                scope.TryRegisterSource(Space(-2), out var source);
                scope.SetListener(Space(2));
                scope.Simulate(1);
                Assert.That(scope.TryCopyResult(source, new float[4], out var output), Is.True);
                Assert.That(output.Status, Is.EqualTo(SteamAudioPathSimulationStatus.Valid));
                Assert.That(output.DirectOcclusion, Is.LessThan(0.01f));
                Assert.That(output.HasPathSignal, Is.True);
                return scope;
            }
            catch { scope.Dispose(); throw; }
        }
        [TearDown]
        public void Cleanup()
        {
            if (ownsPersistedAsset) AssetDatabase.DeleteAsset(AssetPath);
            else if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            context?.Release(); context = null; asset = null;
            ownsPersistedAsset = false;
        }

        [Test]
        public void NativeDetourDistanceCurveFadesToZeroBeforePathSummationAndResetsOnReuse()
        {
            Bake();
            using var loaded = Load();
            using var simulation = new SteamAudioPathSimulationScope(Get<SA.Context>(loaded, "Context"),
                Get<SA.Scene>(loaded, "Scene"), Get<SA.ProbeBatch>(loaded, "Probes"), 1, 48000, 512);
            simulation.TryRegisterSource(Space(-2), out var source);
            simulation.SetListener(Space(2));
            var coefficients = new float[4];
            simulation.Simulate(1);
            simulation.TryCopyResult(source, coefficients, out var baseline);
            Assert.That(baseline.HasPathSignal, Is.True);
            float amplitude = coefficients[0];
            Assert.That(simulation.TryGetPathDistanceRange(source, out var minimum, out var maximum, out var count, out _), Is.True);
            Assert.That(minimum, Is.GreaterThan(4), "Pathing callbacks must measure the wall detour, not direct distance.");
            Assert.That(maximum, Is.EqualTo(minimum).Within(.0001f));
            Assert.That(count, Is.GreaterThan(0));
            simulation.SetSourceDistanceRange(source, minimum / .8f, .4f);
            Assert.That(simulation.TryGetPathDistanceRange(source, out _, out _, out _, out _), Is.False, "Changed curves invalidate diagnostics.");
            simulation.Simulate(2);
            simulation.TryCopyResult(source, coefficients, out _);
            Assert.That(coefficients[0], Is.EqualTo(amplitude * .5f).Within(.00001f));
            simulation.SetSourceDistanceRange(source, (minimum + 4) * .5f, .2f);
            simulation.Simulate(3);
            simulation.TryCopyResult(source, coefficients, out var cutoff);
            Assert.That(cutoff.DirectDistanceAttenuation, Is.GreaterThan(0), "The close direct distance still has nonzero gain.");
            Assert.That(cutoff.HasPathSignal, Is.False, "All native detour contributions must be zero before SH summation.");
            Assert.That(coefficients, Is.All.EqualTo(0));
            simulation.TryGetPathDistanceRange(source, out _, out _, out _, out var nonzero);
            Assert.That(nonzero, Is.Zero);
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                for (int i = 4; i < 8; i++) simulation.Simulate(i);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            simulation.RemoveSource(source);
            simulation.TryRegisterSource(Space(-2), out source);
            simulation.Simulate(8);
            simulation.TryCopyResult(source, coefficients, out var recycled);
            Assert.That(recycled.HasPathSignal, Is.True, "Reused sources must not inherit voice range limits.");
            Assert.That(coefficients[0], Is.EqualTo(amplitude).Within(.00001f));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetSourceDistanceRange(source, float.NaN, .2f));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetSourceDistanceRange(source, 10, 0));
        }

        [Test]
        public void AuthoredCurveUsesNativeDetourDistanceAndChangesWithoutDoubleAttenuation()
        {
            Bake();
            using var loaded = Load();
            using var simulation = new SteamAudioPathSimulationScope(Get<SA.Context>(loaded, "Context"),
                Get<SA.Scene>(loaded, "Scene"), Get<SA.ProbeBatch>(loaded, "Probes"), 1, 48000, 512);
            simulation.TryRegisterSource(Space(-2), out var source);
            simulation.SetListener(Space(2));
            simulation.SetSourceDistanceAttenuationEnabled(source, false);
            simulation.Simulate(1);
            var baseline = new float[4]; var changed = new float[4];
            simulation.TryCopyResult(source, baseline, out _);
            simulation.TryGetPathDistanceRange(source, out float distance, out _, out _, out _);
            var authored = AnimationCurve.Linear(0, 1, distance * 2, 0);
            simulation.SetSourceDistanceCurve(source, new DistanceAttenuationCurve(authored));
            simulation.SetSourceDistanceAttenuationEnabled(source, true);
            simulation.Simulate(2);
            simulation.TryCopyResult(source, changed, out _);
            Assert.That(changed[0], Is.EqualTo(baseline[0] * .5f).Within(.00001f));
            simulation.SetSourceDistanceCurve(source, new DistanceAttenuationCurve(AnimationCurve.Linear(0, 1, 5, 0)));
            simulation.Simulate(3);
            simulation.TryCopyResult(source, changed, out var silent);
            Assert.That(silent.DirectDistanceAttenuation, Is.GreaterThan(0));
            Assert.That(silent.HasPathSignal, Is.False);
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                for (int i = 4; i < 10; i++) simulation.Simulate(i);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void FinalPathDiagnosticsAreOptionalBoundedAndDoNotChangeAudio()
        {
            Bake();
            using var loaded = Load();
            using var simulation = new SteamAudioPathSimulationScope(Get<SA.Context>(loaded, "Context"),
                Get<SA.Scene>(loaded, "Scene"), Get<SA.ProbeBatch>(loaded, "Probes"), 1, 48000, 512);
            simulation.TryRegisterSource(Space(-2), out var source);
            simulation.SetListener(Space(2));
            simulation.Simulate(1);
            var baseline = new float[4]; var captured = new float[4];
            simulation.TryCopyResult(source, baseline, out _);
            var headers = new SteamAudioPathDiagnostic[64];
            var points = new SA.Vector3[4096];
            Assert.That(simulation.CopyPathDiagnostics(source, headers, out _), Is.Zero);
            bool supported = simulation.SupportsPathDiagnostics;
            Assert.That(simulation.SetPathDiagnosticsEnabled(true), Is.EqualTo(supported));
            simulation.Simulate(2);
            simulation.TryCopyResult(source, captured, out _);
            Assert.That(captured, Is.EqualTo(baseline));
            int count = simulation.CopyPathDiagnostics(source, headers, out int dropped);
            Assert.That(dropped, Is.Zero);
            if (supported)
            {
                Assert.That(count, Is.GreaterThan(0));
                Assert.That(headers[0].Distance, Is.GreaterThan(4));
                Assert.That(headers[0].DistanceGain, Is.EqualTo(1 / headers[0].Distance).Within(.00001f));
                int vertices = simulation.CopyPathDiagnosticPoints(source, 0, points, out bool truncated);
                Assert.That(vertices, Is.GreaterThanOrEqualTo(3));
                Assert.That(truncated, Is.False);
                Assert.That(points[0].x, Is.EqualTo(-2));
                Assert.That(points[vertices - 1].x, Is.EqualTo(2));
                bool goesAroundWall = false;
                for (int i = 0; i < vertices; i++) goesAroundWall |= points[i].z > 2;
                Assert.That(goesAroundWall, Is.True);
                simulation.CopyPathDiagnosticPoints(source, 0, points.AsSpan(0, 1), out truncated);
                Assert.That(truncated, Is.True);
                simulation.CopyPathDiagnostics(source, headers.AsSpan(0, 0), out dropped);
                Assert.That(dropped, Is.EqualTo(count));
                simulation.Simulate(3);
                Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                    UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                    "GC allocation recorder must detect a known allocation before measuring this path.");
                Assert.That(() =>
                {
                    for (int i = 4; i < 8; i++)
                    {
                        simulation.Simulate(i);
                        simulation.CopyPathDiagnostics(source, headers, out _);
                        simulation.CopyPathDiagnosticPoints(source, 0, points, out _);
                    }
                }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
                simulation.SetSourceDistanceRange(source, 5, .2f);
                Assert.That(simulation.CopyPathDiagnostics(source, headers, out _), Is.Zero);
                simulation.Simulate(8);
                simulation.CopyPathDiagnostics(source, headers, out _);
                Assert.That(headers[0].DistanceGain, Is.Zero);
                simulation.SetSourceDistanceAttenuationEnabled(source, false);
                simulation.Simulate(9);
                simulation.CopyPathDiagnostics(source, headers, out _);
                Assert.That(headers[0].DistanceGain, Is.EqualTo(1));
            }
            else Assert.That(count, Is.Zero, "The stock SDK must remain usable without the extension.");
            simulation.RemoveSource(source);
            simulation.TryRegisterSource(Space(-2), out var replacement);
            Assert.That(simulation.CopyPathDiagnostics(source, headers, out _), Is.Zero);
            Assert.That(simulation.CopyPathDiagnostics(replacement, headers, out _), Is.Zero);
            simulation.SetPathDiagnosticsEnabled(false);
            simulation.Simulate(10);
            Assert.That(simulation.CopyPathDiagnostics(replacement, headers, out _), Is.Zero);
        }

        [Test]
        public void NativeValidationSegmentsAreOptInAndClearWhenDisabled()
        {
            Bake();
            using var loaded = Load();
            using var simulation = Simulator(loaded);
            var segments = new SteamAudioPathDebugSegment[4096];
            Assert.That(simulation.CopyPathDebugSegments(segments, out _), Is.Zero);
            simulation.SetPathDebugEnabled(true);
            simulation.Simulate(2);
            int count = simulation.CopyPathDebugSegments(segments, out int dropped);
            Assert.That(count, Is.GreaterThan(0), "The native wall-detour fixture must report actual probe validation segments.");
            Assert.That(dropped, Is.Zero);
            for (int i = 0; i < count; i++)
                Assert.That(float.IsNaN(segments[i].From.x) || float.IsNaN(segments[i].To.x), Is.False);
            simulation.SetPathDebugEnabled(false);
            simulation.Simulate(3);
            Assert.That(simulation.CopyPathDebugSegments(segments, out _), Is.Zero);
        }

        [Test]
        public void BakedAssetSurvivesUnityPersistenceAndNativeLoad()
        {
            Bake();
            Assert.That(Get<int>(asset, "FormatVersion"), Is.EqualTo(1));
            Assert.That(Get<string>(asset, "SdkVersion"), Is.EqualTo("4.8.1"));
            Assert.That(Get<string>(asset, "GeometryFingerprint"), Is.EqualTo("generated-wall-fixture"));
            Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetPath), Is.Null, "Never overwrite an unrelated existing fixture asset.");
            AssetDatabase.CreateAsset(asset, AssetPath);
            ownsPersistedAsset = true;
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(asset);
            asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetPath);
            Assert.That(asset, Is.Not.Null);
            using var loaded = Load();
            var pathing = new SA.BakedDataIdentifier { type = SA.BakedDataType.Pathing, variation = SA.BakedDataVariation.Dynamic };
            Assert.That(Get<SA.ProbeBatch>(loaded, "Probes").GetDataSize(pathing).ToUInt64(), Is.GreaterThan(0));
            using var simulation = Simulator(loaded);
        }

        [Test]
        public void ProbeMetadataIsAnIndependentExactCopyOfTheBakedSpheres()
        {
            var probes = Probes();
            Bake(probes);
            probes[0] = new SA.Sphere { center = V(999, 999, 999), radius = 999 };
            Assert.That(Get<int>(asset, "ProbeCount"), Is.EqualTo(5));
            var sphere = (SA.Sphere)asset.GetType().GetMethod("GetProbe").Invoke(asset, new object[] { 0 });
            Assert.That(sphere.center.x, Is.EqualTo(-2));
            Assert.That(sphere.center.y, Is.Zero);
            Assert.That(sphere.center.z, Is.Zero);
            Assert.That(sphere.radius, Is.EqualTo(0.6f));
        }

        [Test]
        public void BlobChecksumRejectsAccidentalCorruptionBeforeNativeLoading()
        {
            Bake();
            var data = new SerializedObject(asset);
            var firstByte = data.FindProperty("sceneData").GetArrayElementAtIndex(0);
            firstByte.intValue ^= 1;
            data.ApplyModifiedPropertiesWithoutUndo();
            var error = Assert.Throws<TargetInvocationException>(() => Load());
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void UnsupportedMetadataAndBlankFingerprintAreRejected()
        {
            Bake();
            var data = new SerializedObject(asset);
            data.FindProperty("formatVersion").intValue = 2;
            data.ApplyModifiedPropertiesWithoutUndo();
            var error = Assert.Throws<TargetInvocationException>(() => Load());
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            UnityEngine.Object.DestroyImmediate(asset); asset = null;
            context.Release(); context = null;
            error = Assert.Throws<TargetInvocationException>(() => Bake(fingerprint: " "));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
        }

        [Test]
        public void RetainedSimulationSurvivesLoaderAndCallerContextRelease()
        {
            Bake();
            using var loaded = Load();
            using var simulation = Simulator(loaded);
            loaded.Dispose(); loaded.Dispose();
            context.Release();
            simulation.Simulate(2);
            Assert.That(simulation.SourceCount, Is.EqualTo(1));
        }
    }
}
