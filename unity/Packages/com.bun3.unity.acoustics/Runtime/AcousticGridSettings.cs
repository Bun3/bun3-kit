using System;

namespace Bun3.Unity.Acoustics
{
    /// <summary>Explicit height offsets and conservative probe coverage distance.</summary>
    public readonly struct AcousticGridSettings
    {
        /// <summary>Floor offset along the frame height axis.</summary>
        public float FloorHeight { get; }
        /// <summary>Wall top and optional ceiling offset.</summary>
        public float CeilingHeight { get; }
        /// <summary>Probe height offset strictly between floor and ceiling.</summary>
        public float EarHeight { get; }
        /// <summary>Maximum walkable path distance for candidate coverage, not minimum probe separation.</summary>
        public float ProbeSpacing { get; }

        /// <summary>Creates finite settings with ordered heights and a positive coverage distance.</summary>
        public AcousticGridSettings(float floorHeight, float ceilingHeight, float earHeight, float probeSpacing)
        {
            FloorHeight = floorHeight;
            CeilingHeight = ceilingHeight;
            EarHeight = earHeight;
            ProbeSpacing = probeSpacing;
            Validate();
        }

        internal void Validate()
        {
            if (!AcousticGridFrame.Finite(FloorHeight) || !AcousticGridFrame.Finite(CeilingHeight) ||
                !AcousticGridFrame.Finite(EarHeight) || !AcousticGridFrame.Finite(ProbeSpacing) ||
                !AcousticGridFrame.Finite(CeilingHeight - FloorHeight) ||
                !(FloorHeight < EarHeight && EarHeight < CeilingHeight) || ProbeSpacing <= 0)
                throw new ArgumentException("Heights must satisfy floor < ear < ceiling and probe spacing must be positive and finite.");
        }
    }
}
