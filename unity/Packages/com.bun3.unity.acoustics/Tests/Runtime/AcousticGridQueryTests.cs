using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Acoustics.Tests
{
    public sealed class AcousticGridQueryTests
    {
        private static Type Require(string name)
        {
            var type = Type.GetType("Bun3.Unity.Acoustics." + name + ", Bun3.Unity.Acoustics");
            Assert.That(type, Is.Not.Null, "Missing grid query contract: " + name);
            return type;
        }

        private static object Create(bool[,] blocked, Vector3[] centers = null, float radius = 2f,
            int limit = 8, Vector3? origin = null, Vector3? x = null, Vector3? y = null)
        {
            var type = Require("AcousticGridQuery");
            var probeType = Require("AcousticProbe");
            var probes = Array.CreateInstance(probeType, centers?.Length ?? 0);
            if (centers != null)
                for (var i = 0; i < centers.Length; i++)
                    probes.SetValue(Activator.CreateInstance(probeType, centers[i], radius), i);
            var frame = new AcousticGridFrame(origin ?? Vector3.zero, x ?? Vector3.right, y ?? Vector3.back, Vector3.up);
            return Activator.CreateInstance(type, blocked, frame, 0f, 3f, probes, limit);
        }

        private static object Query(object query, Vector3 source, Vector3 listener)
            => query.GetType().GetMethod("Query").Invoke(query, new object[] { source, listener });
        private static object Value(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
        private static object Set(object query, int x, int y, bool blocked)
            => query.GetType().GetMethod("SetBlocked").Invoke(query, new object[] { x, y, blocked });
        private static Vector3 Center(int x, int y) => new Vector3(x + .5f, 1.5f, -y - .5f);
        private static string Coverage(object result, string endpoint) => Value(Value(result, endpoint + "Coverage"), "Status").ToString();

        [Test]
        public void DebugRouteWitnessFollowsLiveDoorOccupancy()
        {
            var query = (AcousticGridQuery)Create(new bool[3, 2]);
            var source = Center(0, 0); var target = Center(2, 0);
            var direct = query.CreateDebugRoute(source, target);
            query.SetBlocked(1, 0, true);
            var detour = query.CreateDebugRoute(source, target);
            Assert.That(detour.Length, Is.GreaterThan(direct.Length));
            Assert.That(detour[0], Is.EqualTo(source));
            Assert.That(detour[detour.Length - 1], Is.EqualTo(target));
            Assert.That(detour, Has.No.Member(Center(1, 0)));
            Assert.That(detour, Has.Member(Center(1, 1)));
            Assert.That(query.Query(source, target).Reachability, Is.EqualTo(AcousticGridReachability.Connected));
            query.SetBlocked(1, 1, true);
            Assert.That(query.CreateDebugRoute(source, target), Is.Empty);
            Assert.That(query.Query(source, target).Reachability, Is.EqualTo(AcousticGridReachability.Disconnected));
            Assert.That(query.CreateDebugRoute(Center(1, 0), target), Is.Empty);
        }

        [Test]
        public void DoorCloseAndReopenChangesConnectivityWithoutChangingOldResult()
        {
            var query = Create(new bool[3, 1]);
            var old = Query(query, Center(0, 0), Center(2, 0));
            Assert.That(Value(old, "Reachability").ToString(), Is.EqualTo("Connected"));
            Assert.That(Set(query, 1, 0, true), Is.True);
            var closed = Query(query, Center(0, 0), Center(2, 0));
            Assert.That(Value(closed, "Reachability").ToString(), Is.EqualTo("Disconnected"));
            Assert.That(Value(closed, "Revision"), Is.EqualTo(1UL));
            Assert.That(Set(query, 1, 0, true), Is.False);
            Assert.That(Value(query, "Revision"), Is.EqualTo(1UL));
            Set(query, 1, 0, false);
            Assert.That(Value(Query(query, Center(0, 0), Center(2, 0)), "Reachability").ToString(), Is.EqualTo("Connected"));
            Assert.That(Value(old, "Revision"), Is.EqualTo(0UL));
            Assert.That(Value(old, "Reachability").ToString(), Is.EqualTo("Connected"));
        }

        [Test]
        public void ConnectivityAndProbeCoverageAreIndependent()
        {
            var query = Create(new bool[4, 1], new[] { Center(0, 0) }, .6f);
            var result = Query(query, Center(0, 0), Center(3, 0));
            Assert.That(Value(result, "Reachability").ToString(), Is.EqualTo("Connected"));
            Assert.That(Coverage(result, "Source"), Is.EqualTo("GridVisible"));
            Assert.That(Coverage(result, "Listener"), Is.EqualTo("None"));
        }

        [Test]
        public void WorldFrameMappingUsesActualNoncentralPositionAndRejectsOutsideHeights()
        {
            var origin = new Vector3(7, 4, 11);
            var x = new Vector3(0, 0, 2);
            var y = new Vector3(3, 0, 0);
            var probe = origin + x * .2f + y * .8f + Vector3.up;
            var query = Create(new bool[2, 2], new[] { probe }, .1f, origin: origin, x: x, y: y);
            Assert.That(Coverage(Query(query, probe, probe), "Source"), Is.EqualTo("GridVisible"));
            var invalid = Query(query, probe - Vector3.up * 2, probe);
            Assert.That(Value(invalid, "Reachability").ToString(), Is.EqualTo("InvalidEndpoint"));
            Assert.That(Coverage(invalid, "Source"), Is.EqualTo("InvalidEndpoint"));
            Assert.That(Coverage(Query(query, origin - x, probe), "Source"), Is.EqualTo("InvalidEndpoint"));
        }

        [Test]
        public void ProbeInfluenceUsesThreeDimensionalSphereIncludingBoundary()
        {
            var probe = Center(0, 0);
            var query = Create(new bool[2, 2], new[] { probe }, .5f);
            Assert.That(Coverage(Query(query, probe + Vector3.up * .5f, probe), "Source"), Is.EqualTo("GridVisible"));
            Assert.That(Coverage(Query(query, probe + Vector3.up * .6f, probe), "Source"), Is.EqualTo("None"));
        }

        [Test]
        public void ProbeLosCannotCrossBlockedCornerOrRunAlongBlockedEdge()
        {
            var blocked = new bool[3, 3];
            blocked[1, 0] = true;
            var query = Create(blocked, new[] { Center(2, 2) }, 10);
            Assert.That(Coverage(Query(query, Center(0, 0), Center(2, 2)), "Source"), Is.EqualTo("None"));
            var edgeQuery = Create(blocked, new[] { new Vector3(2.5f, 1.5f, -1f) }, 10);
            Assert.That(Coverage(Query(edgeQuery, new Vector3(.5f, 1.5f, -1f), Center(2, 2)), "Source"), Is.EqualTo("None"));
        }

        [Test]
        public void DoorBlockingUpdatesProbeAttachmentEvenWhileAlternativeGridRouteExists()
        {
            var query = Create(new bool[3, 2], new[] { Center(2, 0) }, 10);
            Assert.That(Coverage(Query(query, Center(0, 0), Center(2, 0)), "Source"), Is.EqualTo("GridVisible"));
            Set(query, 1, 0, true);
            var result = Query(query, Center(0, 0), Center(2, 0));
            Assert.That(Value(result, "Reachability").ToString(), Is.EqualTo("Connected"));
            Assert.That(Coverage(result, "Source"), Is.EqualTo("None"));
        }

        [Test]
        public void CandidateLimitCannotClaimCoverageFromAnUnselectedVisibleProbe()
        {
            var blocked = new bool[3, 1];
            blocked[1, 0] = true;
            var centers = new Vector3[9];
            for (var i = 0; i < 8; i++) centers[i] = Center(2, 0);
            centers[8] = Center(0, 0);
            var query = Create(blocked, centers, 10);
            var result = Query(query, Center(0, 0), Center(0, 0));
            Assert.That(Coverage(result, "Source"), Is.EqualTo("SelectionUncertain"));
            Assert.That(Value(Value(result, "SourceCoverage"), "ContainingProbeCount"), Is.EqualTo(9));
            Assert.That(Value(Value(result, "SourceCoverage"), "VisibleProbeCount"), Is.EqualTo(1));
            centers[7] = Center(0, 0);
            var safe = Create(blocked, centers, 10);
            Assert.That(Coverage(Query(safe, Center(0, 0), Center(0, 0)), "Source"), Is.EqualTo("GridVisible"));
        }

        [Test]
        public void InputMaskIsCopiedAndBlockedOrNonfiniteEndpointsAreInvalid()
        {
            var blocked = new bool[2, 1];
            var query = Create(blocked);
            blocked[1, 0] = true;
            Assert.That(Value(Query(query, Center(0, 0), Center(1, 0)), "Reachability").ToString(), Is.EqualTo("Connected"));
            Set(query, 1, 0, true);
            Assert.That(Coverage(Query(query, Center(1, 0), Center(0, 0)), "Source"), Is.EqualTo("InvalidEndpoint"));
            Assert.That(Coverage(Query(query, new Vector3(float.NaN, 0, 0), Center(0, 0)), "Source"), Is.EqualTo("InvalidEndpoint"));
        }

        [Test]
        public void InvalidProbeRadiusAndSelectionLimitAreRejected()
        {
            Require("AcousticGridQuery");
            var error = Assert.Throws<TargetInvocationException>(() => Create(new bool[1, 1], new[] { Center(0, 0) }, -1));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
            error = Assert.Throws<TargetInvocationException>(() => Create(new bool[1, 1], limit: 0));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void WarmQueriesAndDoorComponentRebuildAllocateNoManagedMemory()
        {
            var query = Create(new bool[7, 3], new[] { Center(0, 0), Center(6, 0) }, 10);
            var call = Expression.Call(Expression.Constant(query), query.GetType().GetMethod("Query"),
                Expression.Constant(Center(0, 0)), Expression.Constant(Center(6, 0)));
            var run = Expression.Lambda<Action>(Expression.Block(call, Expression.Empty())).Compile();
            var closed = Expression.Parameter(typeof(bool));
            var set = Expression.Call(Expression.Constant(query), query.GetType().GetMethod("SetBlocked"),
                Expression.Constant(3), Expression.Constant(0), closed);
            var edit = Expression.Lambda<Action<bool>>(Expression.Block(set, Expression.Empty()), closed).Compile();
            for (var i = 0; i < 8; i++) { edit((i & 1) == 0); run(); }
            Assert.That(() => System.GC.KeepAlive(new byte[1024]),
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(),
                "GC allocation recorder must detect a known allocation before measuring this path.");
            Assert.That(() =>
            {
                for (var i = 0; i < 64; i++) { edit((i & 1) == 0); run(); }
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void CornerEndpointWithOppositeAxisSignsTerminatesWithoutOvershooting()
        {
            var work = Task.Run(() =>
            {
                var query = Create(new bool[3, 3], new[] { new Vector3(1, 1.5f, -1) }, 10);
                return Coverage(Query(query, new Vector3(.5f, 1.5f, -1.5f), Center(2, 2)), "Source");
            });
            Assert.That(work.Wait(2000), Is.True, "Finite grid query exceeded its traversal bound.");
            Assert.That(work.Result, Is.EqualTo("GridVisible"));
        }
    }
}
