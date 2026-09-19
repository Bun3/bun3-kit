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

        /// <summary>Returns shared settings when available, otherwise the retained local settings.</summary>
        public SoundAcousticSettings Resolve()
        {
            return Profile != null ? Profile.Settings : Local;
        }
    }
}
