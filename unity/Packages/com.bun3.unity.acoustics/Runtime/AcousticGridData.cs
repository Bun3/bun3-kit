using UnityEngine;

namespace Bun3.Unity.Acoustics
{
    /// <summary>Owned build arrays independent of the input masks.</summary>
    public sealed class AcousticGridData
    {
        /// <summary>World-space vertices, four per generated face.</summary>
        public Vector3[] Vertices { get; }
        /// <summary>Triangle indices with normals facing walkable space.</summary>
        public int[] Triangles { get; }
        /// <summary>Deterministic world-space probe candidates, not a native bake result.</summary>
        public Vector3[] Probes { get; }

        internal AcousticGridData(Vector3[] vertices, int[] triangles, Vector3[] probes)
        {
            Vertices = vertices;
            Triangles = triangles;
            Probes = probes;
        }
    }
}
