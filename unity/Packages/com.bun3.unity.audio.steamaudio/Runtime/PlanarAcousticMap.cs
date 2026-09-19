using System;
using Bun3.Unity.Acoustics;
using Bun3.Unity.Audio.SteamAudio;
using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Persisted map topology paired with the exact native acoustic bake.</summary>
    public class PlanarAcousticMap : ScriptableObject
    {
        [SerializeField] SteamAudioAcousticAsset baked;
        [SerializeField] int width, height;
        [SerializeField] bool[] blocked;
        [SerializeField] Vector3 origin, cellX, cellY;
        [SerializeField] float floorHeight, ceilingHeight, earHeight;
        /// <summary>Native scene and probe bake paired with this topology.</summary>
        public SteamAudioAcousticAsset Baked => baked;
        /// <summary>Grid origin and basis in native world coordinates.</summary>
        public AcousticGridFrame Frame => new AcousticGridFrame(origin, cellX, cellY, Vector3.up);
        /// <summary>Floor offset along the frame height axis.</summary>
        public float FloorHeight => floorHeight;
        /// <summary>Ceiling offset along the frame height axis.</summary>
        public float CeilingHeight => ceilingHeight;
        /// <summary>Listener/source height above the frame origin.</summary>
        public float EarHeight => earHeight;
        /// <summary>Number of grid columns.</summary>
        public int Width => width;
        /// <summary>Number of grid rows.</summary>
        public int Height => height;

        /// <summary>Copies validated topology and stores the corresponding native bake.</summary>
        public void Initialize(SteamAudioAcousticAsset asset, bool[,] mask, AcousticGridFrame frame, float floor, float ceiling, float ear)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (mask == null || mask.GetLength(0) == 0 || mask.GetLength(1) == 0) throw new ArgumentException("A nonempty mask is required.", nameof(mask));
            _ = new AcousticGridFrame(frame.Origin, frame.CellX, frame.CellY, frame.HeightAxis);
            if (frame.HeightAxis != Vector3.up || !Finite(floor) || !Finite(ceiling) || !Finite(ear) || !(floor < ear && ear < ceiling))
                throw new ArgumentException("Map heights must be finite, ordered, and vertical.");
            var copy = new bool[mask.Length];
            int w = mask.GetLength(0), h = mask.GetLength(1);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) copy[y * w + x] = mask[x, y];
            baked = asset; width = w; height = h; blocked = copy;
            origin = frame.Origin; cellX = frame.CellX; cellY = frame.CellY;
            floorHeight = floor; ceilingHeight = ceiling; earHeight = ear;
        }

        /// <summary>Reads the immutable baked occupancy of a grid cell.</summary>
        public bool IsBlocked(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) throw new ArgumentOutOfRangeException(nameof(x));
            return blocked[y * width + x];
        }

        /// <summary>Creates independent connectivity and probe coverage query state.</summary>
        public AcousticGridQuery CreateGridQuery()
        {
            if (baked == null || width <= 0 || height <= 0 || blocked == null || blocked.Length != checked(width * height))
                throw new InvalidOperationException("Acoustic map data is incomplete.");
            if (!Finite(earHeight) || !(floorHeight < earHeight && earHeight < ceilingHeight)) throw new InvalidOperationException("Acoustic ear height is invalid.");
            var mask = new bool[width, height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) mask[x, y] = blocked[y * width + x];
            var probes = new AcousticProbe[baked.ProbeCount];
            for (int i = 0; i < probes.Length; i++)
            {
                var p = baked.GetProbe(i);
                probes[i] = new AcousticProbe(new Vector3(p.center.x, p.center.y, p.center.z), p.radius);
            }
            return new AcousticGridQuery(mask, Frame, floorHeight, ceilingHeight, probes, 8);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
