using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Acoustics.Tests
{
    public sealed class AcousticGridTests
    {
        private static Type Require(string name)
        {
            var type = Type.GetType("Bun3.Unity.Acoustics." + name + ", Bun3.Unity.Acoustics");
            Assert.That(type, Is.Not.Null, "Missing acoustic grid contract: " + name);
            return type;
        }

        private static object Build(bool[,] blocked, bool[,] roofed, float spacing = 2f,
            Vector3? origin = null, Vector3? x = null, Vector3? y = null)
        {
            var builder = Require("AcousticGridBuilder");
            var frame = Activator.CreateInstance(Require("AcousticGridFrame"),
                origin ?? Vector3.zero, x ?? Vector3.right, y ?? Vector3.back, Vector3.up);
            var settings = Activator.CreateInstance(Require("AcousticGridSettings"), 0f, 3f, 1.5f, spacing);
            return builder.GetMethod("Build").Invoke(null, new[] { blocked, roofed, frame, settings });
        }

        private static T[] Values<T>(object data, string name)
        {
            return (T[])data.GetType().GetProperty(name).GetValue(data);
        }

        [Test]
        public void SingleOpenCellHasFloorAndCenteredProbe()
        {
            var data = Build(new bool[1, 1], new bool[1, 1]);
            Assert.That(Values<Vector3>(data, "Vertices").Length, Is.EqualTo(4));
            Assert.That(Values<int>(data, "Triangles").Length, Is.EqualTo(6));
            Assert.That(Values<Vector3>(data, "Probes"), Is.EqualTo(new[] { new Vector3(.5f, 1.5f, -.5f) }));
        }

        [Test]
        public void RoofIsGeneratedOnlyForFlaggedWalkableCells()
        {
            var blocked = new bool[3, 1];
            blocked[2, 0] = true;
            var roof = new bool[3, 1];
            roof[0, 0] = roof[2, 0] = true;
            var data = Build(blocked, roof);
            // Two floors, one ceiling and one wall against the occupied cell.
            Assert.That(Values<int>(data, "Triangles").Length, Is.EqualTo(24));
        }

        [Test]
        public void SealedRoomHasFourWallsAndGateRemovesOneWall()
        {
            var blocked = new bool[3, 3];
            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 3; x++) blocked[x, y] = x != 1 || y != 1;
            var roof = new bool[3, 3];
            roof[1, 1] = true;
            var sealedRoom = Build(blocked, roof);
            Assert.That(Values<int>(sealedRoom, "Triangles").Length, Is.EqualTo(36));
            blocked[1, 0] = false;
            var open = Build(blocked, roof);
            // Two floors, one ceiling, five exposed walls around both walkable cells.
            Assert.That(Values<int>(open, "Triangles").Length, Is.EqualTo(48));
            var vertices = Values<Vector3>(open, "Vertices");
            var triangles = Values<int>(open, "Triangles");
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                Assert.That(a.z == -1 && b.z == -1 && c.z == -1 &&
                    a.x >= 1 && a.x <= 2 && b.x >= 1 && b.x <= 2 && c.x >= 1 && c.x <= 2,
                    Is.False, "Static geometry must leave the gate throat open.");
            }
        }

        [Test]
        public void NearbyDisconnectedCellsEachReceiveProbe()
        {
            var blocked = new bool[3, 1];
            blocked[1, 0] = true;
            Assert.That(Values<Vector3>(Build(blocked, new bool[3, 1], 10), "Probes").Length, Is.EqualTo(2));
        }

        [Test]
        public void CoverageCannotTurnBlindCornerOrCutBlockedDiagonal()
        {
            var blocked = new bool[3, 3];
            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 3; x++) blocked[x, y] = y != 0 && x != 2;
            var probes = Values<Vector3>(Build(blocked, new bool[3, 3], 10), "Probes");
            Assert.That(probes.Length, Is.GreaterThanOrEqualTo(2));
            var diagonal = new bool[,] { { false, true }, { true, false } };
            Assert.That(Values<Vector3>(Build(diagonal, new bool[2, 2], 10), "Probes").Length, Is.EqualTo(2));
        }

        [Test]
        public void FrameRetainsTranslationRotationScaleAndHeight()
        {
            var data = Build(new bool[1, 1], new bool[1, 1], 2,
                new Vector3(4, 7, 9), new Vector3(0, 0, 2), new Vector3(3, 0, 0));
            Assert.That(Values<Vector3>(data, "Probes")[0], Is.EqualTo(new Vector3(5.5f, 8.5f, 10)));
        }

        [Test]
        public void RepeatedBuildsAreDeterministicAndAllBlockedIsEmpty()
        {
            var first = Build(new bool[7, 3], new bool[7, 3]);
            var second = Build(new bool[7, 3], new bool[7, 3]);
            Assert.That(Values<Vector3>(first, "Vertices"), Is.EqualTo(Values<Vector3>(second, "Vertices")));
            Assert.That(Values<int>(first, "Triangles"), Is.EqualTo(Values<int>(second, "Triangles")));
            Assert.That(Values<Vector3>(first, "Probes"), Is.EqualTo(Values<Vector3>(second, "Probes")));
            var empty = Build(new bool[,] { { true } }, new bool[1, 1]);
            Assert.That(Values<Vector3>(empty, "Vertices"), Is.Empty);
            Assert.That(Values<Vector3>(empty, "Probes"), Is.Empty);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidSpacingIsRejected(float spacing)
        {
            Require("AcousticGridBuilder");
            var error = Assert.Throws<TargetInvocationException>(() => Build(new bool[1, 1], new bool[1, 1], spacing));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void MismatchedMasksAreRejected()
        {
            Require("AcousticGridBuilder");
            var error = Assert.Throws<TargetInvocationException>(() => Build(new bool[2, 1], new bool[1, 1]));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void InvalidHeightOrderAndDegenerateFrameAreRejected()
        {
            var settings = Require("AcousticGridSettings");
            var heightError = Assert.Throws<TargetInvocationException>(() =>
                Activator.CreateInstance(settings, 3f, 2f, 1.5f, 2f));
            Assert.That(heightError.InnerException, Is.InstanceOf<ArgumentException>());
            var frame = Require("AcousticGridFrame");
            var frameError = Assert.Throws<TargetInvocationException>(() =>
                Activator.CreateInstance(frame, Vector3.zero, Vector3.right, Vector3.right, Vector3.up));
            Assert.That(frameError.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void FiniteInputsCannotProduceOverflowedGeometry()
        {
            var settings = Require("AcousticGridSettings");
            var error = Assert.Throws<TargetInvocationException>(() =>
                Activator.CreateInstance(settings, -float.MaxValue, float.MaxValue, 0f, 2f));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [Test]
        public void FloorAndCeilingWindingFacesInteriorForEitherFrameHandedness()
        {
            foreach (var y in new[] { Vector3.back, Vector3.forward })
            {
                var data = Build(new bool[1, 1], new bool[,] { { true } }, y: y);
                var vertices = Values<Vector3>(data, "Vertices");
                var indices = Values<int>(data, "Triangles");
                for (var i = 0; i < indices.Length; i += 3)
                {
                    var a = vertices[indices[i]];
                    var normal = Vector3.Cross(vertices[indices[i + 1]] - a, vertices[indices[i + 2]] - a);
                    Assert.That(Vector3.Dot(normal, a.y == 0 ? Vector3.up : Vector3.down), Is.GreaterThan(0));
                }
            }
        }
    }
}
