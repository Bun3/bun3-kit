using UnityEngine;
using UnityEngine.Audio;

namespace Bun3.Unity.Audio
{
    /// <summary>How a played sound is positioned in the world.</summary>
    public enum SpatialMode
    {
        /// <summary>2D playback, no spatialization.</summary>
        None,

        /// <summary>3D playback at a fixed position.</summary>
        Positional,

        /// <summary>3D playback tracking a Transform every frame.</summary>
        Follow,
    }

    /// <summary>
    /// Designer-tuned sound definition. The asset reference itself is the runtime key —
    /// no string or enum IDs. Fields are read once at play time; live edits apply to
    /// subsequent plays.
    /// </summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Def", fileName = "SoundDef")]
    public sealed class SoundDef : ScriptableObject
    {
        /// <summary>Candidate clips; one is chosen per play, avoiding the previous pick.</summary>
        public AudioClip[] Clips;

        /// <summary>Base volume range rolled per play.</summary>
        public FloatRange Volume = new(1f, 1f);

        /// <summary>Pitch range rolled per play.</summary>
        public FloatRange Pitch = new(1f, 1f);

        /// <summary>Whether playback loops until stopped.</summary>
        public bool Loop;

        /// <summary>Target mixer group; null falls back to the system's SFX group.</summary>
        public AudioMixerGroup MixerGroup;

        /// <summary>Max simultaneous voices for this def; 0 = unlimited. Exceeding steals the oldest.</summary>
        public int MaxInstances;

        /// <summary>Minimum seconds between retriggers; 0 = none. Blocked plays return an invalid handle.</summary>
        public float Cooldown;

        /// <summary>Spatialization mode.</summary>
        public SpatialMode Spatial = SpatialMode.None;

        /// <summary>3D attenuation minimum distance (used when Spatial != None).</summary>
        public float MinDistance = 1f;

        /// <summary>3D attenuation maximum distance (used when Spatial != None).</summary>
        public float MaxDistance = 30f;

        /// <summary>Whether this sound participates in occlusion evaluation (3D sounds only).</summary>
        public bool Occlusion;

        /// <summary>Round-robin memory: index of the clip chosen on the previous play.</summary>
        [System.NonSerialized] internal int LastClipIndex = -1;
    }
}
