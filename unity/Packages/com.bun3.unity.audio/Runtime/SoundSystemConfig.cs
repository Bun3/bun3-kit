using UnityEngine;
using UnityEngine.Audio;

namespace Bun3.Unity.Audio
{
    /// <summary>Construction-time settings for <see cref="SoundSystem"/>. Validated once; not live-tunable.</summary>
    public sealed class SoundSystemConfig
    {
        /// <summary>Mixer used for channel volumes; null skips mixer integration until the bundled asset ships.</summary>
        public AudioMixer Mixer;

        /// <summary>
        /// Fallback group for defs without an explicit MixerGroup.
        /// May be populated in place by SoundSystem's constructor from the bundled mixer when left null.
        /// </summary>
        public AudioMixerGroup SfxGroup;

        /// <summary>
        /// Mixer group music routes to; null leaves music unrouted.
        /// May be populated in place by SoundSystem's constructor from the bundled mixer when left null.
        /// </summary>
        public AudioMixerGroup MusicGroup;

        /// <summary>Occlusion evaluation strategy; null uses the built-in single-linecast provider.</summary>
        public IOcclusionProvider OcclusionProvider;

        /// <summary>Obstruction layers for the built-in raycast provider (ignored with a custom provider).</summary>
        public LayerMask OcclusionMask = ~0;

        /// <summary>
        /// Occlusion-enabled voices evaluated per frame (round-robin). 0 (or negative)
        /// disables occlusion entirely — no filters are attached and no evaluation runs;
        /// the intended off switch for external spatializer adapters.
        /// </summary>
        public int OcclusionChecksPerFrame = 4;

        /// <summary>Low-pass cutoff (Hz) at full occlusion; 22000 = open.</summary>
        public float OcclusionMuffledCutoffHz = 1200f;

        /// <summary>Volume multiplier at full occlusion (1 = no attenuation).</summary>
        public float OcclusionVolumeAtFull = 0.35f;

        /// <summary>Seconds for the occlusion factor to travel 0→1 (click-free transitions).</summary>
        public float OcclusionSmoothingSeconds = 0.15f;

        /// <summary>Listener transform for occlusion rays; null finds the scene AudioListener.</summary>
        public Transform Listener;

        /// <summary>Number of prewarmed SFX voices. Fixed for the system's lifetime.</summary>
        public int SfxVoices = 24;

        /// <summary>
        /// Seed for the system's private variation stream (clip pick, volume/pitch rolls).
        /// Null (default) seeds from time; set for deterministic tests or replays.
        /// The stream is isolated — it never touches UnityEngine.Random state.
        /// </summary>
        public int? RandomSeed;

        /// <summary>
        /// When true, SFX voice pitch is multiplied by Time.timeScale (slow-motion effect).
        /// Music is unaffected. Non-loop voices still complete on real time regardless of
        /// pitch, so at a low timeScale a one-shot's audio is cut off before it finishes
        /// (backlog: pitch-scaled completion). Setting <c>Time.timeScale = 0</c> is not the
        /// supported way to pause sound — use <see cref="AudioListener.pause"/> or the
        /// bundled Paused snapshot instead; a voice played while timeScale is 0 starts
        /// inaudible (pitch 0) and still expires on schedule.
        /// </summary>
        public bool PitchWithTimescale;
    }
}
