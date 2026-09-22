using System;
using UnityEngine;

namespace Bun3.Unity.Acoustics
{
    /// <summary>A caller-supplied baked probe influence sphere, independent of a simulation SDK.</summary>
    public readonly struct AcousticProbe
    {
        /// <summary>World-space probe center.</summary>
        public Vector3 Center { get; }
        /// <summary>Positive world-space influence radius.</summary>
        public float Radius { get; }

        /// <summary>Creates a finite probe sphere.</summary>
        public AcousticProbe(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
            Validate();
        }

        internal void Validate()
        {
            if (!AcousticGridFrame.Finite(Center) || !AcousticGridFrame.Finite(Radius) || Radius <= 0)
                throw new ArgumentException("Probe center must be finite and radius must be positive and finite.");
        }
    }
}
