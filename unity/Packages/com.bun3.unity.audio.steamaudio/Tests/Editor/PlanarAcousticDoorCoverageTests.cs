using System.Collections.Generic;
using Bun3.Unity.Acoustics;
using NUnit.Framework;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    // Wall at y 0..1 with a door at x 0..1 and a detour gap at x 5..6. The listener stands just south of the door,
    // inside eight probe spheres north of the door and three visible south spheres, filling the eight-probe selection limit.
    public sealed class PlanarAcousticDoorCoverageTests
    {
        static readonly Vector2 Source = new Vector2(.5f, 2.5f);
        static readonly Vector2 Listener = new Vector2(.5f, -.2f);
        SA.Context context;
        SteamAudioAcousticAsset asset;
        PlanarAcousticMap map;
        readonly List<GameObject> doors = new List<GameObject>();

        sealed class Settings : PlanarAcousticSettings
        {
            public override int SourceCapacity => 1;
            public override float OcclusionRadius => 0;
            public override int OcclusionSamples => 1;
            public override float ObstructedPathGain => 1;
        }

        [OneTimeSetUp]
        public void CreateFixture()
        {
            context = new SA.Context();
            var vertices = new List<SA.Vector3>();
            var triangles = new List<SA.Triangle>();
            Panel(-6, 0); Panel(1, 5);
            var probes = new List<SA.Sphere>();
            foreach (float x in new[] { -.5f, 0, .5f, 1, 1.5f }) probes.Add(Probe(x, 1.5f));
            foreach (float x in new[] { 0, .5f, 1 }) probes.Add(Probe(x, 1.9f));
            foreach (var p in new[] { new Vector2(-1, -1), new Vector2(.5f, -1.5f), new Vector2(2, -1), new Vector2(3.5f, -1),
                new Vector2(5, -1), new Vector2(5.5f, .5f), new Vector2(5.5f, 2), new Vector2(4, 2), new Vector2(2.5f, 2) })
                probes.Add(Probe(p.x, p.y));
            asset = SteamAudioAcousticBaker.Bake(context, vertices.ToArray(), triangles.ToArray(), new int[triangles.Count],
                new[] { new SA.Material { absorptionLow = 1, absorptionMid = 1, absorptionHigh = 1 } }, probes.ToArray(),
                SteamAudioPathBakeSettings.Default, "planar-door-coverage-tests");
            var mask = new bool[12, 6];
            for (int x = 0; x < 12; x++) mask[x, 3] = x != 6 && x != 11;
            map = ScriptableObject.CreateInstance<PlanarAcousticMap>();
            map.Initialize(asset, mask, new AcousticGridFrame(new Vector3(-6, 0, 3), Vector3.right, Vector3.back, Vector3.up), -1, 1, 0);

            void Panel(float start, float end)
            {
                int i = vertices.Count;
                vertices.AddRange(new[] { V(start, -3, -.5f), V(start, 3, -.5f), V(end, 3, -.5f), V(end, -3, -.5f) });
                triangles.Add(new SA.Triangle { index0 = i, index1 = i + 1, index2 = i + 2 });
                triangles.Add(new SA.Triangle { index0 = i, index1 = i + 2, index2 = i + 3 });
            }
        }

        [OneTimeTearDown]
        public void DestroyFixture()
        {
            if (map != null) Object.DestroyImmediate(map);
            if (asset != null) Object.DestroyImmediate(asset);
            context?.Release();
        }

        [TearDown]
        public void DestroyDoors()
        {
            foreach (var door in doors) Object.DestroyImmediate(door);
            doors.Clear();
        }

        [Test]
        public void ListenerAtClosedDoorHearsDetourWhenHiddenProbesFillSelection()
        {
            var query = map.CreateGridQuery();
            query.SetBlocked(6, 3, true);
            Assert.That(query.Query(Point(Source), Point(Listener)).ListenerCoverage.Status,
                Is.EqualTo(AcousticEndpointCoverageStatus.SelectionUncertain), "Fixture must reproduce the selection limit case.");

            using var world = new PlanarAcousticWorld(map, new Settings());
            Close(world, 1, new Vector2(.5f, .5f));
            Assert.That(world.TryRegisterSource(Source, out var source), Is.True);
            world.Tick(Listener, 0);

            Assert.That(world.IsRouteCovered(Source, Listener), Is.True);
            Assert.That(world.TryGetPath(source, new float[4], out _), Is.True);
            Assert.That(world.TryGetNativePathDistances(source, out float shortest, out _, out _, out _), Is.True);
            Assert.That(shortest, Is.GreaterThan(8f), "The closed door must route sound through the detour gap.");
        }

        [Test]
        public void ListenerAtClosedDoorStaysSilentWithoutDetour()
        {
            using var world = new PlanarAcousticWorld(map, new Settings());
            Close(world, 1, new Vector2(.5f, .5f));
            Close(world, 2, new Vector2(5.5f, .5f));
            Assert.That(world.TryRegisterSource(Source, out var source), Is.True);
            world.Tick(Listener, 0);

            Assert.That(world.IsRouteCovered(Source, Listener), Is.False);
            Assert.That(world.TryGetPath(source, new float[4], out _), Is.False, "Probes behind the closed door must not leak sound.");
        }

        void Close(PlanarAcousticWorld world, int id, Vector2 center)
        {
            var door = new GameObject("door", typeof(BoxCollider2D));
            doors.Add(door);
            var box = door.GetComponent<BoxCollider2D>();
            box.offset = center; box.size = Vector2.one;
            world.SetObstacle(id, box, true);
        }

        static Vector3 Point(Vector2 value) => new Vector3(value.x, 0, -value.y);
        static SA.Sphere Probe(float x, float y) => new SA.Sphere { center = V(x, 0, -y), radius = 2.2f };
        static SA.Vector3 V(float x, float y, float z) => new SA.Vector3 { x = x, y = y, z = z };
    }
}
