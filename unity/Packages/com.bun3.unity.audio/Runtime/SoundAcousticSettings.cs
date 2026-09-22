using System;

namespace Bun3.Unity.Audio
{
    /// <summary>Provides live resolved acoustic settings to native playback adapters.</summary>
    public interface IResolvedSoundAcousticSettings
    {
        /// <summary>Whether the settings owner is currently available for playback.</summary>
        bool IsAvailable { get; }

        /// <summary>Gets the currently resolved acoustic settings.</summary>
        SoundAcousticSettings Acoustics { get; }
    }

    /// <summary>Distance attenuation and stereo-width settings shared by sound and voice playback.</summary>
    [Serializable]
    public struct SoundAcousticSettings
    {
        /// <summary>Whether distance attenuation is applied.</summary>
        public bool DistanceAttenuation;

        /// <summary>Optional authored distance attenuation curve.</summary>
        public DistanceAttenuationProfile AttenuationProfile;

        /// <summary>Fallback minimum attenuation distance when no profile is assigned.</summary>
        public float MinDistance;

        /// <summary>Fallback maximum attenuation distance when no profile is assigned.</summary>
        public float MaxDistance;

        /// <summary>Whether stereo-width settings are inherited from the playback owner.</summary>
        public bool InheritSpatialBlend;

        /// <summary>Optional shared stereo-width profile.</summary>
        public SpatialBlendProfile SpatialBlendProfile;

        /// <summary>Fallback distance at or below which output is dual mono.</summary>
        public float MonoDistance;

        /// <summary>Fallback distance at or above which output has full spatial width.</summary>
        public float FullSpatialDistance;

        /// <summary>Gets the legacy SFX acoustic defaults.</summary>
        public static SoundAcousticSettings Default => new()
        {
            DistanceAttenuation = true,
            AttenuationProfile = null,
            MinDistance = 1f,
            MaxDistance = 30f,
            InheritSpatialBlend = true,
            SpatialBlendProfile = null,
            MonoDistance = 1f,
            FullSpatialDistance = 3f,
        };
    }
}
