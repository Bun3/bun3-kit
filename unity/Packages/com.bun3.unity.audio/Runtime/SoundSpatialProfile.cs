using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Shared spatial settings for sound definitions. Assigning this asset replaces the entire local group.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Spatial Profile", fileName = "SoundSpatialProfile")]
    public sealed class SoundSpatialProfile : ScriptableObject
    {
        /// <summary>Spatialization mode.</summary>
        public SpatialMode Spatial = SpatialMode.None;

        /// <summary>3D attenuation minimum distance (used when Spatial != None).</summary>
        public float MinDistance = 1f;

        /// <summary>3D attenuation maximum distance (used when Spatial != None).</summary>
        public float MaxDistance = 30f;

        /// <summary>Enables distance attenuation independently of positioning and occlusion.</summary>
        [Tooltip("Disable distance gain while keeping spatial direction and occlusion.")]
        public bool DistanceAttenuation = true;

        /// <summary>Optional shared curve for native adapters; null uses the definition's minimum and maximum.</summary>
        [Tooltip("Native distance curve. Null uses MinDistance and MaxDistance with a 20% edge fade.")]
        public DistanceAttenuationProfile AttenuationProfile;

        /// <summary>Whether this sound participates in occlusion evaluation (3D sounds only).</summary>
        public bool Occlusion;

        /// <summary>Optional full-obstruction gain override; a negative value uses system settings.</summary>
        public float OcclusionVolumeAtFull = -1f;

        /// <summary>Whether native adapters use their configured default near-field blend.</summary>
        public bool InheritSpatialBlend = true;

        /// <summary>Optional shared near-field blend when inheritance is disabled.</summary>
        public SpatialBlendProfile SpatialBlendProfile;

        /// <summary>Distance up to which native playback is centered when no blend profile is assigned.</summary>
        public float MonoDistance = 1f;

        /// <summary>Distance at which native playback becomes fully spatial when no blend profile is assigned.</summary>
        public float FullSpatialDistance = 3f;
    }
}
