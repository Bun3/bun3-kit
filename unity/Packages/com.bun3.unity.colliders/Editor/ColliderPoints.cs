using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Bun3.Unity.Colliders.Editor
{
    /// <summary>One point of a 2D collider: its path and its index within that path. Edge colliders use path 0.</summary>
    public readonly struct ColliderPoint : IEquatable<ColliderPoint>
    {
        /// <summary>Path index; always 0 for an <see cref="EdgeCollider2D"/>.</summary>
        public readonly int Path;

        /// <summary>Point index within the path.</summary>
        public readonly int Index;

        /// <summary>Creates a point reference.</summary>
        public ColliderPoint(int path, int index)
        {
            Path = path;
            Index = index;
        }

        /// <inheritdoc/>
        public bool Equals(ColliderPoint other) => Path == other.Path && Index == other.Index;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ColliderPoint other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Path, Index);
    }

    /// <summary>Reads and deletes the points of <see cref="PolygonCollider2D"/> and <see cref="EdgeCollider2D"/>.</summary>
    public static class ColliderPoints
    {
        /// <summary>Undo name recorded by <see cref="Delete"/>.</summary>
        public const string DeleteUndoName = "Delete Collider Points";

        /// <summary>Number of point paths: the polygon's paths, 1 for an edge, 0 for other colliders.</summary>
        public static int PathCount(Collider2D collider) => collider switch
        {
            PolygonCollider2D polygon => polygon.pathCount,
            EdgeCollider2D _ => 1,
            _ => 0,
        };

        /// <summary>Local points of a path, before the collider offset.</summary>
        public static Vector2[] GetPath(Collider2D collider, int path) => collider switch
        {
            PolygonCollider2D polygon => polygon.GetPath(path),
            EdgeCollider2D edge => edge.points,
            _ => Array.Empty<Vector2>(),
        };

        /// <summary>World position of a local point of the collider.</summary>
        public static Vector3 ToWorld(Collider2D collider, Vector2 point) => collider.transform.TransformPoint(point + collider.offset);

        /// <summary>
        /// Deletes the points with Undo. A polygon path left with fewer than 3 points is removed with them. Nothing changes
        /// and false is returned when no polygon path, or fewer than 2 edge points, would remain.
        /// </summary>
        public static bool Delete(Collider2D collider, ICollection<ColliderPoint> points)
        {
            if (points.Count == 0) return false;
            switch (collider)
            {
                case PolygonCollider2D polygon:
                    var paths = new List<Vector2[]>();
                    for (int p = 0; p < polygon.pathCount; p++)
                    {
                        var kept = Keep(polygon.GetPath(p), p, points);
                        if (kept.Length >= 3) paths.Add(kept);
                    }
                    if (paths.Count == 0) return false;
                    Undo.RecordObject(polygon, DeleteUndoName);
                    polygon.pathCount = paths.Count;
                    for (int p = 0; p < paths.Count; p++) polygon.SetPath(p, paths[p]);
                    return true;
                case EdgeCollider2D edge:
                    var left = Keep(edge.points, 0, points);
                    if (left.Length < 2) return false;
                    Undo.RecordObject(edge, DeleteUndoName);
                    edge.points = left;
                    return true;
                default:
                    return false;
            }
        }

        static Vector2[] Keep(Vector2[] path, int pathIndex, ICollection<ColliderPoint> deleted) =>
            path.Where((_, i) => !deleted.Contains(new ColliderPoint(pathIndex, i))).ToArray();
    }
}
