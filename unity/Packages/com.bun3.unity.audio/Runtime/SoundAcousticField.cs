using System;

namespace Bun3.Unity.Audio
{
    /// <summary>Per-field flags marking which <see cref="SoundAcousticSettings"/> values come from the local copy instead of the shared profile.</summary>
    [Flags]
    public enum SoundAcousticField
    {
        /// <summary>No field is overridden.</summary>
        None = 0,

        /// <summary>The local DistanceAttenuation flag.</summary>
        DistanceAttenuation = 1 << 0,

        /// <summary>The local AttenuationProfile.</summary>
        AttenuationProfile = 1 << 1,

        /// <summary>The local MinDistance.</summary>
        MinDistance = 1 << 2,

        /// <summary>The local MaxDistance.</summary>
        MaxDistance = 1 << 3,

        /// <summary>The local InheritSpatialBlend flag.</summary>
        InheritSpatialBlend = 1 << 4,

        /// <summary>The local SpatialBlendProfile.</summary>
        SpatialBlendProfile = 1 << 5,

        /// <summary>The local MonoDistance.</summary>
        MonoDistance = 1 << 6,

        /// <summary>The local FullSpatialDistance.</summary>
        FullSpatialDistance = 1 << 7,
    }
}
