using System;

namespace Bun3.Unity.Audio
{
    /// <summary>Per-field flags marking which <see cref="SoundDef"/> values override its shared profiles.</summary>
    [Flags]
    public enum SoundDefField
    {
        /// <summary>No field is overridden.</summary>
        None = 0,

        /// <summary>The local Volume range.</summary>
        Volume = 1 << 0,

        /// <summary>The local Pitch range.</summary>
        Pitch = 1 << 1,

        /// <summary>The local Loop flag.</summary>
        Loop = 1 << 2,

        /// <summary>The local MixerGroup.</summary>
        MixerGroup = 1 << 3,

        /// <summary>The local VolumeGroup.</summary>
        VolumeGroup = 1 << 4,

        /// <summary>The local MaxInstances.</summary>
        MaxInstances = 1 << 5,

        /// <summary>The local Cooldown.</summary>
        Cooldown = 1 << 6,

        /// <summary>The local Priority.</summary>
        Priority = 1 << 7,

        /// <summary>The local Spatial mode.</summary>
        Spatial = 1 << 8,

        /// <summary>The local Occlusion flag.</summary>
        Occlusion = 1 << 9,

        /// <summary>The local OcclusionVolumeAtFull.</summary>
        OcclusionVolumeAtFull = 1 << 10,

        /// <summary>The def's own acoustic selection instead of the spatial profile's.</summary>
        Acoustics = 1 << 11,
    }
}
