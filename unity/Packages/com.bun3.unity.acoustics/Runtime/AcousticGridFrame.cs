using System;
using UnityEngine;

namespace Bun3.Unity.Acoustics
{
    /// <summary>Maps cell corners and height offsets into an orthogonal world frame.</summary>
    public readonly struct AcousticGridFrame
    {
        /// <summary>The world corner of cell zero.</summary>
        public Vector3 Origin { get; }
        /// <summary>One cell step along the first grid dimension.</summary>
        public Vector3 CellX { get; }
        /// <summary>One cell step along the second grid dimension.</summary>
        public Vector3 CellY { get; }
        /// <summary>The perpendicular unit height direction.</summary>
        public Vector3 HeightAxis { get; }

        /// <summary>Creates a finite orthogonal frame with positive cell lengths.</summary>
        public AcousticGridFrame(Vector3 origin, Vector3 cellX, Vector3 cellY, Vector3 heightAxis)
        {
            Origin = origin;
            CellX = cellX;
            CellY = cellY;
            HeightAxis = heightAxis;
            Validate();
        }

        internal void Validate()
        {
            if (!Finite(Origin) || !Finite(CellX) || !Finite(CellY) || !Finite(HeightAxis))
                throw new ArgumentException("Grid frame must be finite.");
            var x = CellX.magnitude;
            var y = CellY.magnitude;
            if (!Finite(x) || !Finite(y) || x <= 0 || y <= 0 ||
                Math.Abs(HeightAxis.sqrMagnitude - 1f) > .0001f ||
                Math.Abs(Vector3.Dot(CellX / x, CellY / y)) > .0001f ||
                Math.Abs(Vector3.Dot(CellX / x, HeightAxis)) > .0001f ||
                Math.Abs(Vector3.Dot(CellY / y, HeightAxis)) > .0001f)
                throw new ArgumentException("Grid axes must be nonzero and orthogonal; height must be a unit axis.");
        }

        internal Vector3 Point(float x, float y, float height)
        {
            return Origin + CellX * x + CellY * y + HeightAxis * height;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
