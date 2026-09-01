using UnityEngine.Audio;

namespace Bun3.Unity.Audio
{
    /// <summary>Construction-time settings for <see cref="SoundSystem"/>. Validated once; not live-tunable.</summary>
    public sealed class SoundSystemConfig
    {
        /// <summary>Mixer used for channel volumes; null skips mixer integration until the bundled asset ships.</summary>
        public AudioMixer Mixer;

        /// <summary>Fallback group for defs without an explicit MixerGroup.</summary>
        public AudioMixerGroup SfxGroup;

        /// <summary>Number of prewarmed SFX voices. Fixed for the system's lifetime.</summary>
        public int SfxVoices = 24;

        /// <summary>
        /// Seed for the system's private variation stream (clip pick, volume/pitch rolls).
        /// Null (default) seeds from time; set for deterministic tests or replays.
        /// The stream is isolated — it never touches UnityEngine.Random state.
        /// </summary>
        public int? RandomSeed;
    }
}
