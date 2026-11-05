using System;

namespace Bun3.Unity.Audio
{
    /// <summary>Selects shared acoustic settings while preserving local values for later use.</summary>
    [Serializable]
    public sealed class SoundAcousticSelection
    {
        /// <summary>Shared settings used when assigned.</summary>
        public SoundAcousticProfile Profile;

        /// <summary>Local settings retained even while a shared profile is selected.</summary>
        public SoundAcousticSettings Local = SoundAcousticSettings.Default;

        /// <summary>Fields taken from <see cref="Local"/> instead of <see cref="Profile"/> while a profile is selected.</summary>
        public SoundAcousticField Overrides;

        /// <summary>Returns the profile settings with overridden fields taken from local settings, or the local settings when no profile is assigned.</summary>
        public SoundAcousticSettings Resolve()
        {
            if (Profile == null) return Local;
            var s = Profile.Settings;
            var o = Overrides;
            if ((o & SoundAcousticField.DistanceAttenuation) != 0) s.DistanceAttenuation = Local.DistanceAttenuation;
            if ((o & SoundAcousticField.AttenuationProfile) != 0) s.AttenuationProfile = Local.AttenuationProfile;
            if ((o & SoundAcousticField.MinDistance) != 0) s.MinDistance = Local.MinDistance;
            if ((o & SoundAcousticField.MaxDistance) != 0) s.MaxDistance = Local.MaxDistance;
            if ((o & SoundAcousticField.InheritSpatialBlend) != 0) s.InheritSpatialBlend = Local.InheritSpatialBlend;
            if ((o & SoundAcousticField.SpatialBlendProfile) != 0) s.SpatialBlendProfile = Local.SpatialBlendProfile;
            if ((o & SoundAcousticField.MonoDistance) != 0) s.MonoDistance = Local.MonoDistance;
            if ((o & SoundAcousticField.FullSpatialDistance) != 0) s.FullSpatialDistance = Local.FullSpatialDistance;
            return s;
        }
    }
}
