using UnityEngine;
using UnityEngine.Audio;

namespace Bun3.Unity.Audio
{
    /// <summary>Explicitly owned controls for an externally played source. Default owns nothing.</summary>
    public readonly struct ExternalAudioSettings
    {
        /// <summary>Owned source volume, or null to leave volume under external control.</summary>
        public float? Gain { get; }
        /// <summary>Whether the registration owns the source mixer route.</summary>
        public bool OverrideMixerGroup { get; }
        /// <summary>Owned mixer route; null explicitly clears routing when enabled.</summary>
        public AudioMixerGroup MixerGroup { get; }
        /// <summary>Existing filter whose enabled state and cutoff are owned, or null.</summary>
        public AudioLowPassFilter LowPassFilter { get; }
        /// <summary>Initial owned filter cutoff in hertz.</summary>
        public float LowPassCutoff { get; }

        /// <summary>Chooses source controls to own. The filter must belong to the source's GameObject.</summary>
        public ExternalAudioSettings(float? gain = null, bool overrideMixerGroup = false,
            AudioMixerGroup mixerGroup = null, AudioLowPassFilter lowPassFilter = null,
            float lowPassCutoff = 22000f)
        {
            Gain = gain;
            OverrideMixerGroup = overrideMixerGroup;
            MixerGroup = mixerGroup;
            LowPassFilter = lowPassFilter;
            LowPassCutoff = lowPassCutoff;
        }
    }
}
