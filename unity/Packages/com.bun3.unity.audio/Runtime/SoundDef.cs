using System.Collections.Generic;
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
        /// <summary>Optional shared playback settings; null preserves the local group.</summary>
        public SoundPlaybackProfile PlaybackProfile;

        /// <summary>Optional shared routing settings; null preserves the local group.</summary>
        public SoundRoutingProfile RoutingProfile;

        /// <summary>Optional shared concurrency settings; null preserves the local group.</summary>
        public SoundConcurrencyProfile ConcurrencyProfile;

        /// <summary>Optional shared spatial settings; null preserves the local group.</summary>
        public SoundSpatialProfile SpatialProfile;

        /// <summary>Resolved Volume from the shared playback profile or local settings.</summary>
        public FloatRange EffectiveVolume => PlaybackProfile != null ? PlaybackProfile.Volume : Volume;

        /// <summary>Resolved Pitch from the shared playback profile or local settings.</summary>
        public FloatRange EffectivePitch => PlaybackProfile != null ? PlaybackProfile.Pitch : Pitch;

        /// <summary>Resolved Loop from the shared playback profile or local settings.</summary>
        public bool EffectiveLoop => PlaybackProfile != null ? PlaybackProfile.Loop : Loop;

        /// <summary>Resolved MixerGroup from the shared routing profile or local settings.</summary>
        public AudioMixerGroup EffectiveMixerGroup => RoutingProfile != null ? RoutingProfile.MixerGroup : MixerGroup;

        /// <summary>Resolved VolumeGroup from the shared routing profile or local settings.</summary>
        public string EffectiveVolumeGroup => RoutingProfile != null ? RoutingProfile.VolumeGroup : VolumeGroup;

        /// <summary>Resolved MaxInstances from the shared concurrency profile or local settings.</summary>
        public int EffectiveMaxInstances => ConcurrencyProfile != null ? ConcurrencyProfile.MaxInstances : MaxInstances;

        /// <summary>Resolved Cooldown from the shared concurrency profile or local settings.</summary>
        public float EffectiveCooldown => ConcurrencyProfile != null ? ConcurrencyProfile.Cooldown : Cooldown;

        /// <summary>Resolved Spatial from the shared spatial profile or local settings.</summary>
        public SpatialMode EffectiveSpatial => SpatialProfile != null ? SpatialProfile.Spatial : Spatial;

        /// <summary>Local or shared acoustic settings.</summary>
        public SoundAcousticSelection Acoustics => _acoustics;

        /// <summary>Resolved acoustic settings from the selected spatial owner.</summary>
        public SoundAcousticSettings EffectiveAcoustics => SpatialProfile != null
            ? SpatialProfile.Acoustics.Resolve()
            : Acoustics.Resolve();

        /// <summary>Resolved Occlusion from the shared spatial profile or local settings.</summary>
        public bool EffectiveOcclusion => SpatialProfile != null ? SpatialProfile.Occlusion : Occlusion;

        /// <summary>Resolved OcclusionVolumeAtFull from the shared spatial profile or local settings.</summary>
        public float EffectiveOcclusionVolumeAtFull => SpatialProfile != null ? SpatialProfile.OcclusionVolumeAtFull : OcclusionVolumeAtFull;

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

        /// <summary>Optional logical group passed to the system gain resolver; independent of mixer routing.</summary>
        [Tooltip("Logical volume group interpreted by the game's gain resolver, e.g. sfx or ui.")]
        public string VolumeGroup = "sfx";

        /// <summary>Max simultaneous voices for this def; 0 = unlimited. Exceeding steals the oldest.</summary>
        public int MaxInstances;

        /// <summary>Minimum seconds between retriggers; 0 = none. Blocked plays return an invalid handle.</summary>
        public float Cooldown;

        /// <summary>Spatialization mode.</summary>
        public SpatialMode Spatial = SpatialMode.None;

        /// <summary>Whether this sound participates in occlusion evaluation (3D sounds only).</summary>
        public bool Occlusion;

        /// <summary>Optional full-obstruction gain override; a negative value uses system settings.</summary>
        public float OcclusionVolumeAtFull = -1f;

        [SerializeField, HideInInspector]
        private SoundAcousticSelection _acoustics = new();

        /// <summary>Round-robin memory: index of the clip chosen on the previous play.</summary>
        [System.NonSerialized] internal int LastClipIndex = -1;

#if BUN3_ADDRESSABLES
        /// <summary>
        /// Addressable clip alternative to <see cref="Clips"/>. Load with
        /// SoundSystem.PreloadAsync before playing; unpreloaded defs play nothing.
        /// </summary>
        public UnityEngine.AddressableAssets.AssetReferenceT<AudioClip>[] AddressableClips;
#endif

        /// <summary>
        /// Runtime clip cache; when set it takes precedence over <see cref="Clips"/>.
        /// Populated by preloading (or manually for runtime-created defs).
        /// </summary>
        [System.NonSerialized] internal AudioClip[] RuntimeClips;

        /// <summary>
        /// Gets the effective playback clips without copying: loaded runtime clips take precedence
        /// over authored clips. Returns null when neither source is assigned.
        /// </summary>
        public IReadOnlyList<AudioClip> PlaybackClips => EffectiveClips;

        internal AudioClip[] EffectiveClips => RuntimeClips ?? Clips;
    }
}
