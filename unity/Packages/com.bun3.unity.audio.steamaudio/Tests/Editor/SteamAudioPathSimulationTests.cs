using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using SA = SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public sealed class SteamAudioPathSimulationTests
    {
        static Type ScopeType()
        {
            var type = Type.GetType("Bun3.Unity.Audio.SteamAudio.SteamAudioPathSimulationScope, Bun3.Unity.Audio.SteamAudio");
            Assert.That(type, Is.Not.Null, "A bounded owner of copied native simulation results is required.");
            return type;
        }

        static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name).Invoke(target, args);
        static T Value<T>(object value, string property) => (T)value.GetType().GetProperty(property).GetValue(value);
        static object Register(object scope, SA.CoordinateSpace3 position, bool expected = true)
        {
            object[] args = { position, null };
            Assert.That(Call(scope, "TryRegisterSource", args), Is.EqualTo(expected));
            return args[1];
        }
        static object Result(object scope, object handle, float[] coefficients)
        {
            object[] args = { handle, coefficients, null };
            Assert.That(Call(scope, "TryCopyResult", args), Is.True);
            return args[2];
        }
        static void Status(object result, string expected) => Assert.That(Value<object>(result, "Status").ToString(), Is.EqualTo(expected));
        static SA.Vector3 V(float x, float y, float z) => new SA.Vector3 { x = x, y = y, z = z };
        static SA.CoordinateSpace3 Space(float x, float z) => new SA.CoordinateSpace3
        {
            origin = V(x, 0, z), right = V(1, 0, 0), up = V(0, 1, 0), ahead = V(0, 0, -1)
        };

        sealed class Fixture : IDisposable
        {
            static readonly SA.ProgressCallback Progress = (_, __) => { };
            internal SA.Context Context;
            internal SA.Scene Scene;
            internal SA.ProbeBatch Probes;
            SA.StaticMesh wall;
            SA.StaticMesh door;
            internal Fixture()
            {
                try
                {
                    Context = new SA.Context();
                    Scene = new SA.Scene(Context, SA.SceneType.Default, null, null, null, null);
                    wall = Panel(-10, 2); wall.AddToScene(Scene); Scene.Commit();
                    Probes = new SA.ProbeBatch(Context);
                    foreach (var point in new[] { V(-2, 0, 0), V(-2, 0, 3), V(0, 0, 3), V(2, 0, 3), V(2, 0, 0) })
                        Probes.AddProbe(new SA.Sphere { center = point, radius = 0.6f });
                    Probes.Commit();
                    var bake = new SA.PathBakeParams
                    {
                        scene = Scene.Get(), probeBatch = Probes.Get(),
                        identifier = new SA.BakedDataIdentifier { type = SA.BakedDataType.Pathing, variation = SA.BakedDataVariation.Dynamic },
                        numSamples = 1, radius = 0.05f, threshold = 0.99f, visRange = 4.5f, pathRange = 20, numThreads = 1
                    };
                    SA.API.iplPathBakerBake(Context.Get(), ref bake, Progress, IntPtr.Zero);
                }
                catch { Dispose(); throw; }
            }
            SA.StaticMesh Panel(float start, float end) => new SA.StaticMesh(Context, Scene,
                new[] { V(0, -3, start), V(0, 3, start), V(0, 3, end), V(0, -3, end) },
                new[] { new SA.Triangle { index0 = 0, index1 = 1, index2 = 2 }, new SA.Triangle { index0 = 0, index1 = 2, index2 = 3 } },
                new[] { 0, 0 }, new[] { new SA.Material { absorptionLow = 1, absorptionMid = 1, absorptionHigh = 1 } });
            internal IDisposable CreateScope(int capacity = 1) => (IDisposable)Activator.CreateInstance(ScopeType(),
                Context, Scene, Probes, capacity, 48000, 512, 1, 0.05f, 0.99f, 4.5f, 32);
            internal void CloseDoor() { door = Panel(2, 10); door.AddToScene(Scene); Scene.Commit(); }
            internal void OpenDoor() { door.RemoveFromScene(Scene); Scene.Commit(); }
            public void Dispose()
            {
                door?.Release(); door = null;
                wall?.Release(); wall = null;
                Probes?.Release(); Probes = null;
                Scene?.Release(); Scene = null;
                Context?.Release(); Context = null;
            }
        }

        [Test]
        public void AuthoredMinimumDistanceNormalizesNearAndFarNativePathCoefficients()
        {
            Assert.That(ScopeType().GetMethod("SetSourceMinimumDistance"), Is.Not.Null);
            using var fixture = new Fixture();
            using var scope = fixture.CreateScope();
            var handle = Register(scope, Space(-2, 0));
            var baseline = new float[4];
            var changed = new float[4];
            double time = 0;
            foreach (float distance in new[] { 2f, 6f })
            {
                Call(scope, "SetSourceMinimumDistance", handle, 1f);
                Call(scope, "SetListener", Space(-2, distance));
                Call(scope, "Simulate", ++time);
                var original = Result(scope, handle, baseline);
                Assert.That(Value<float>(original, "DirectDistanceAttenuation"), Is.EqualTo(1 / distance).Within(1e-5f));
                Assert.That(Call(scope, "SetSourceMinimumDistance", handle, 3f), Is.True);
                Status(Result(scope, handle, changed), "Pending");
                Call(scope, "Simulate", ++time);
                var adjusted = Result(scope, handle, changed);
                float expected = Math.Min(1, 3 / distance);
                Assert.That(Value<float>(adjusted, "DirectDistanceAttenuation"), Is.EqualTo(expected).Within(1e-5f));
                Assert.That(Value<bool>(adjusted, "HasPathSignal"), Is.True);
                for (int i = 0; i < 4; i++)
                    Assert.That(changed[i], Is.EqualTo(baseline[i] * expected * distance).Within(1e-5f));
                Call(scope, "SetSourceMinimumDistance", handle, 3f);
                Status(Result(scope, handle, changed), "Valid");
            }
            // Native indirect routing must apply the same source model inside its SH computation.
            Call(scope, "SetSourceMinimumDistance", handle, 1f);
            Call(scope, "SetListener", Space(2, 0));
            Call(scope, "Simulate", ++time);
            Assert.That(Value<bool>(Result(scope, handle, baseline), "HasPathSignal"), Is.True);
            Call(scope, "SetSourceMinimumDistance", handle, 3f);
            Call(scope, "Simulate", ++time);
            Result(scope, handle, changed);
            for (int i = 0; i < 4; i++) Assert.That(changed[i], Is.EqualTo(baseline[i] * 3).Within(1e-5f));
            Call(scope, "RemoveSource", handle);
            var replacement = Register(scope, Space(-2, 0));
            Assert.That(Call(scope, "SetSourceMinimumDistance", handle, 3f), Is.False);
            Call(scope, "SetListener", Space(-2, 2));
            Call(scope, "Simulate", ++time);
            Assert.That(Value<float>(Result(scope, replacement, changed), "DirectDistanceAttenuation"), Is.EqualTo(.5f).Within(1e-5f));
        }

        [Test]
        public void MinimumDistanceZeroAndInvalidValuesRemainFiniteAndGenerationSafe()
        {
            using var fixture = new Fixture();
            using var scope = (SteamAudioPathSimulationScope)fixture.CreateScope();
            scope.TryRegisterSource(Space(-2, 0), out var handle);
            scope.SetListener(Space(-2, 2));
            scope.SetSourceMinimumDistance(handle, 3);
            scope.Simulate(1);
            var coefficients = new float[4];
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => scope.SetSourceMinimumDistance(handle, invalid));
            scope.TryCopyResult(handle, coefficients, out var result);
            Assert.That(result.Status, Is.EqualTo(SteamAudioPathSimulationStatus.Valid));
            Assert.That(result.DirectDistanceAttenuation, Is.EqualTo(1));
            scope.SetSourceMinimumDistance(handle, -3);
            scope.Simulate(2);
            scope.TryCopyResult(handle, coefficients, out result);
            Assert.That(result.Status, Is.EqualTo(SteamAudioPathSimulationStatus.Valid));
            Assert.That(result.DirectDistanceAttenuation, Is.Zero);
            scope.SetSourceMinimumDistance(handle, 0);
            scope.TryCopyResult(handle, coefficients, out result);
            Assert.That(result.Status, Is.EqualTo(SteamAudioPathSimulationStatus.Valid));
            scope.SetListener(Space(-2, 0));
            scope.Simulate(3);
            scope.TryCopyResult(handle, coefficients, out result);
            Assert.That(result.Status, Is.EqualTo(SteamAudioPathSimulationStatus.Valid));
            Assert.That(result.DirectDistanceAttenuation, Is.EqualTo(1), "Coincident zero-minimum endpoints must not evaluate 0/0.");
        }

        [Test]
        public void NativeOutputLayoutMatchesSteamAudio481X64()
        {
            ScopeType();
            var type = Type.GetType("Bun3.Unity.Audio.SteamAudio.NativePathSimulation+Outputs, Bun3.Unity.Audio.SteamAudio");
            Assert.That(type, Is.Not.Null);
            Assert.That(IntPtr.Size, Is.EqualTo(8), "This native ABI fixture targets 64-bit players.");
            Assert.That(Marshal.SizeOf(type), Is.EqualTo(216));
            Assert.That(Marshal.OffsetOf(type, "Pathing").ToInt32(), Is.EqualTo(120));
            var path = type.GetField("Pathing").FieldType;
            Assert.That(Marshal.SizeOf(path), Is.EqualTo(96));
            Assert.That(Marshal.OffsetOf(path, "NormalizeEq").ToInt32(), Is.EqualTo(88));
        }

        [Test]
        public void SourceCapacityAndSlotReuseRejectStaleAndForeignHandles()
        {
            ScopeType();
            using var fixture = new Fixture();
            using var scope = fixture.CreateScope();
            var old = Register(scope, Space(-2, 0));
            Register(scope, Space(-2, 0), false);
            Assert.That(Call(scope, "RemoveSource", old), Is.True);
            var replacement = Register(scope, Space(-2, 0));
            Assert.That(Call(scope, "SetSource", old, Space(-2, 3)), Is.False);
            Assert.That(Call(scope, "RemoveSource", old), Is.False);
            using var other = fixture.CreateScope();
            var foreign = Register(other, Space(-2, 0));
            Assert.That(Call(scope, "RemoveSource", foreign), Is.False);
            Status(Result(scope, replacement, new float[4]), "Pending");
        }

        [Test]
        public void GeometryRevisionInvalidatesThenDoorSolverCopiesIndependentResults()
        {
            ScopeType();
            using var fixture = new Fixture();
            using var scope = fixture.CreateScope();
            var handle = Register(scope, Space(-2, 0));
            Call(scope, "SetListener", Space(2, 0));
            Call(scope, "Simulate", 1d);
            var coefficients = new float[4];
            var open = Result(scope, handle, coefficients);
            Status(open, "Valid");
            Assert.That(Value<bool>(open, "HasPathSignal"), Is.True);
            Assert.That(Value<float>(open, "DirectOcclusion"), Is.LessThan(0.01f));
            Assert.That(Value<float>(open, "DirectDistanceAttenuation"), Is.GreaterThan(0));
            var copied = (float[])coefficients.Clone();
            fixture.CloseDoor();
            Call(scope, "InvalidateGeometry", 1UL);
            var pending = Result(scope, handle, coefficients);
            Status(pending, "Pending");
            Assert.That(coefficients, Is.All.EqualTo(0));
            Call(scope, "Simulate", 2d);
            var closed = Result(scope, handle, coefficients);
            Status(closed, "Valid");
            Assert.That(Value<bool>(closed, "HasPathSignal"), Is.False);
            Assert.That(Value<ulong>(closed, "GeometryRevision"), Is.EqualTo(1));
            fixture.OpenDoor();
            Call(scope, "InvalidateGeometry", 2UL);
            Call(scope, "Simulate", 3d);
            var reopened = Result(scope, handle, coefficients);
            Assert.That(Value<bool>(reopened, "HasPathSignal"), Is.True);
            Assert.That(copied, Is.Not.All.EqualTo(0), "The caller's old copied coefficients must survive later native runs.");
            Assert.That(Value<ulong>(open, "GeometryRevision"), Is.Zero);
        }

        [Test]
        public void InvalidInputsPreserveLastResultAndCrossThreadCallsAreRejected()
        {
            ScopeType();
            using var fixture = new Fixture();
            using var scope = fixture.CreateScope();
            var handle = Register(scope, Space(-2, 0));
            Call(scope, "SetListener", Space(2, 0));
            Call(scope, "Simulate", 2d);
            var simulate = (Action<double>)Delegate.CreateDelegate(typeof(Action<double>), scope, scope.GetType().GetMethod("Simulate"));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulate(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulate(1));
            Task.Run(() => Assert.Throws<InvalidOperationException>(() => simulate(3))).GetAwaiter().GetResult();
            var result = Result(scope, handle, new float[4]);
            Status(result, "Valid");
            Assert.That(Value<double>(result, "ComputedAt"), Is.EqualTo(2));
        }

        [Test]
        public void VolumetricOcclusionCanReturnAFractionAtAnOpening()
        {
            ScopeType();
            using var fixture = new Fixture();
            using var scope = fixture.CreateScope();
            var handle = Register(scope, Space(-2, 2));
            Call(scope, "SetListener", Space(2, 2));
            Assert.That(Call(scope, "SetOcclusion", handle, SA.OcclusionType.Volumetric, 1f, 32), Is.True);
            Call(scope, "Simulate", 1d);
            var result = Result(scope, handle, new float[4]);
            Status(result, "Valid");
            Assert.That(Value<float>(result, "DirectOcclusion"), Is.GreaterThan(0).And.LessThan(1));
        }

        [Test]
        public void RetainedNativeInputsSurviveCallerWrapperReleaseAndDisposeIsIdempotent()
        {
            ScopeType();
            using var fixture = new Fixture();
            using var scope = fixture.CreateScope();
            var handle = Register(scope, Space(-2, 0));
            Call(scope, "SetListener", Space(2, 0));
            fixture.Dispose();
            Call(scope, "Simulate", 1d);
            Status(Result(scope, handle, new float[4]), "Valid");
            scope.Dispose();
            scope.Dispose();
            Assert.That(Value<bool>(handle, "IsValid"), Is.False);
        }

        [Test]
        public void WarmInputUpdatesAndCallerResultCopiesDoNotAllocateManagedMemory()
        {
            using var fixture = new Fixture();
            using var scope = (SteamAudioPathSimulationScope)fixture.CreateScope();
            var source = Space(-2, 0);
            var listener = Space(2, 0);
            Assert.That(scope.TryRegisterSource(source, out var handle), Is.True);
            var destination = new float[scope.CoefficientCount];
            for (int i = 0; i < 8; i++)
            {
                scope.SetSourceMinimumDistance(handle, 1 + i % 3);
                scope.SetSource(handle, source); scope.SetListener(listener); scope.Simulate(i);
                scope.TryCopyResult(handle, destination, out _);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 8; i < 40; i++)
            {
                scope.SetSourceMinimumDistance(handle, 1 + i % 3);
                scope.SetSource(handle, source); scope.SetListener(listener); scope.Simulate(i);
                scope.TryCopyResult(handle, destination, out _);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void WarmNativeSimulationAndInternalOutputCopyDoNotAllocateManagedMemory()
        {
            ScopeType();
            using var fixture = new Fixture();
            using var scope = fixture.CreateScope();
            Register(scope, Space(-2, 0));
            Call(scope, "SetListener", Space(2, 0));
            var simulate = (Action<double>)Delegate.CreateDelegate(typeof(Action<double>), scope, scope.GetType().GetMethod("Simulate"));
            for (int i = 0; i < 8; i++) simulate(i);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 8; i < 40; i++) simulate(i);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
    }
}
