using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Colliders.Editor.Tests
{
    public sealed class ColliderPointsTests
    {
        readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created) Object.DestroyImmediate(go);
            created.Clear();
        }

        T Add<T>() where T : Collider2D
        {
            var go = new GameObject(typeof(T).Name);
            created.Add(go);
            return go.AddComponent<T>();
        }

        static Vector2[] Square(float x) => new[] { new Vector2(x, 0), new Vector2(x + 1, 0), new Vector2(x + 1, 1), new Vector2(x, 1) };

        static HashSet<ColliderPoint> Points(params (int path, int index)[] points)
        {
            var set = new HashSet<ColliderPoint>();
            foreach (var (path, index) in points) set.Add(new ColliderPoint(path, index));
            return set;
        }

        [Test]
        public void Delete_RemovesSelectedPolygonPointsAndKeepsTheOrder()
        {
            var polygon = Add<PolygonCollider2D>();
            polygon.pathCount = 1;
            polygon.SetPath(0, new[] { new Vector2(0, 0), new Vector2(.5f, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(.5f, 1), new Vector2(0, 1) });

            Assert.That(ColliderPoints.Delete(polygon, Points((0, 1), (0, 4))), Is.True);

            Assert.That(polygon.GetPath(0), Is.EqualTo(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) }));
        }

        [Test]
        public void Delete_DropsAPolygonPathLeftWithFewerThanThreePoints()
        {
            var polygon = Add<PolygonCollider2D>();
            polygon.pathCount = 2;
            polygon.SetPath(0, Square(0));
            polygon.SetPath(1, Square(5));

            Assert.That(ColliderPoints.Delete(polygon, Points((0, 0), (0, 1))), Is.True);

            Assert.That(polygon.pathCount, Is.EqualTo(1));
            Assert.That(polygon.GetPath(0), Is.EqualTo(Square(5)));
        }

        [Test]
        public void Delete_RefusesToLeaveNoPolygonPath()
        {
            var polygon = Add<PolygonCollider2D>();
            polygon.pathCount = 1;
            polygon.SetPath(0, new[] { Vector2.zero, Vector2.right, Vector2.up });

            Assert.That(ColliderPoints.Delete(polygon, Points((0, 2))), Is.False);

            Assert.That(polygon.pathCount, Is.EqualTo(1));
            Assert.That(polygon.GetPath(0), Has.Length.EqualTo(3));
        }

        [Test]
        public void Delete_RemovesEdgePointsButKeepsAtLeastTwo()
        {
            var edge = Add<EdgeCollider2D>();
            edge.points = new[] { Vector2.zero, Vector2.right, new Vector2(2, 0), new Vector2(3, 0) };

            Assert.That(ColliderPoints.Delete(edge, Points((0, 1), (0, 2), (0, 3))), Is.False);
            Assert.That(edge.points, Has.Length.EqualTo(4));

            Assert.That(ColliderPoints.Delete(edge, Points((0, 1), (0, 2))), Is.True);
            Assert.That(edge.points, Is.EqualTo(new[] { Vector2.zero, new Vector2(3, 0) }));
        }

        [Test]
        public void Delete_IsUndoable()
        {
            var polygon = Add<PolygonCollider2D>();
            polygon.pathCount = 1;
            polygon.SetPath(0, Square(0));
            Undo.IncrementCurrentGroup();

            Assert.That(ColliderPoints.Delete(polygon, Points((0, 3))), Is.True);
            Undo.PerformUndo();

            Assert.That(polygon.GetPath(0), Is.EqualTo(Square(0)));
        }
    }
}
