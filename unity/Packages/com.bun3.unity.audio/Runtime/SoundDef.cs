using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;

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
        private const int CurrentAcousticsVersion = 1;

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

        /// <summary>Acoustic settings, upgraded from legacy serialized fields on first access.</summary>
        public SoundAcousticSelection Acoustics
        {
            get
            {
                UpgradeAcoustics();
                return _acoustics;
            }
        }

        /// <summary>Resolved acoustic settings from the selected spatial owner.</summary>
        public SoundAcousticSettings EffectiveAcoustics => SpatialProfile != null
            ? SpatialProfile.Acoustics.Resolve()
            : Acoustics.Resolve();

        /// <summary>Resolved MinDistance from the shared spatial profile or local settings.</summary>
        public float EffectiveMinDistance => EffectiveAcoustics.MinDistance;

        /// <summary>Resolved MaxDistance from the shared spatial profile or local settings.</summary>
        public float EffectiveMaxDistance => EffectiveAcoustics.MaxDistance;

        /// <summary>Resolved DistanceAttenuation from the shared spatial profile or local settings.</summary>
        public bool EffectiveDistanceAttenuation => EffectiveAcoustics.DistanceAttenuation;

        /// <summary>Resolved AttenuationProfile from the shared spatial profile or local settings.</summary>
        public DistanceAttenuationProfile EffectiveAttenuationProfile => EffectiveAcoustics.AttenuationProfile;

        /// <summary>Resolved Occlusion from the shared spatial profile or local settings.</summary>
        public bool EffectiveOcclusion => SpatialProfile != null ? SpatialProfile.Occlusion : Occlusion;

        /// <summary>Resolved OcclusionVolumeAtFull from the shared spatial profile or local settings.</summary>
        public float EffectiveOcclusionVolumeAtFull => SpatialProfile != null ? SpatialProfile.OcclusionVolumeAtFull : OcclusionVolumeAtFull;

        /// <summary>Resolved InheritSpatialBlend from the shared spatial profile or local settings.</summary>
        public bool EffectiveInheritSpatialBlend => EffectiveAcoustics.InheritSpatialBlend;

        /// <summary>Resolved SpatialBlendProfile from the shared spatial profile or local settings.</summary>
        public SpatialBlendProfile EffectiveSpatialBlendProfile => EffectiveAcoustics.SpatialBlendProfile;

        /// <summary>Resolved MonoDistance from the shared spatial profile or local settings.</summary>
        public float EffectiveMonoDistance => EffectiveAcoustics.MonoDistance;

        /// <summary>Resolved FullSpatialDistance from the shared spatial profile or local settings.</summary>
        public float EffectiveFullSpatialDistance => EffectiveAcoustics.FullSpatialDistance;

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

        /// <summary>3D attenuation minimum distance (used when Spatial != None).</summary>
        public float MinDistance
        {
            get => Acoustics.Local.MinDistance;
            set { var settings = Acoustics.Local; settings.MinDistance = value; Acoustics.Local = settings; }
        }

        /// <summary>3D attenuation maximum distance (used when Spatial != None).</summary>
        public float MaxDistance
        {
            get => Acoustics.Local.MaxDistance;
            set { var settings = Acoustics.Local; settings.MaxDistance = value; Acoustics.Local = settings; }
        }

        /// <summary>Enables distance attenuation independently of positioning and occlusion.</summary>
        public bool DistanceAttenuation
        {
            get => Acoustics.Local.DistanceAttenuation;
            set { var settings = Acoustics.Local; settings.DistanceAttenuation = value; Acoustics.Local = settings; }
        }

        /// <summary>Optional shared curve for native adapters; null uses the definition's minimum and maximum.</summary>
        public DistanceAttenuationProfile AttenuationProfile
        {
            get => Acoustics.Local.AttenuationProfile;
            set { var settings = Acoustics.Local; settings.AttenuationProfile = value; Acoustics.Local = settings; }
        }

        /// <summary>Whether this sound participates in occlusion evaluation (3D sounds only).</summary>
        public bool Occlusion;

        /// <summary>Optional full-obstruction gain override; a negative value uses system settings.</summary>
        public float OcclusionVolumeAtFull = -1f;

        /// <summary>Whether native adapters use their configured default near-field blend.</summary>
        public bool InheritSpatialBlend
        {
            get => Acoustics.Local.InheritSpatialBlend;
            set { var settings = Acoustics.Local; settings.InheritSpatialBlend = value; Acoustics.Local = settings; }
        }

        /// <summary>Optional shared near-field blend when inheritance is disabled.</summary>
        public SpatialBlendProfile SpatialBlendProfile
        {
            get => Acoustics.Local.SpatialBlendProfile;
            set { var settings = Acoustics.Local; settings.SpatialBlendProfile = value; Acoustics.Local = settings; }
        }

        /// <summary>Distance up to which native playback is centered when no blend profile is assigned.</summary>
        public float MonoDistance
        {
            get => Acoustics.Local.MonoDistance;
            set { var settings = Acoustics.Local; settings.MonoDistance = value; Acoustics.Local = settings; }
        }

        /// <summary>Distance at which native playback becomes fully spatial when no blend profile is assigned.</summary>
        public float FullSpatialDistance
        {
            get => Acoustics.Local.FullSpatialDistance;
            set { var settings = Acoustics.Local; settings.FullSpatialDistance = value; Acoustics.Local = settings; }
        }

        [SerializeField, HideInInspector]
        private SoundAcousticSelection _acoustics = new();

        [SerializeField, HideInInspector]
        private int _acousticsVersion;

        [SerializeField, HideInInspector, FormerlySerializedAs("MinDistance")]
        private float _legacyMinDistance = 1f;

        [SerializeField, HideInInspector, FormerlySerializedAs("MaxDistance")]
        private float _legacyMaxDistance = 30f;

        [SerializeField, HideInInspector, FormerlySerializedAs("DistanceAttenuation")]
        private bool _legacyDistanceAttenuation = true;

        [SerializeField, HideInInspector, FormerlySerializedAs("AttenuationProfile")]
        private DistanceAttenuationProfile _legacyAttenuationProfile;

        [SerializeField, HideInInspector, FormerlySerializedAs("InheritSpatialBlend")]
        private bool _legacyInheritSpatialBlend = true;

        [SerializeField, HideInInspector, FormerlySerializedAs("SpatialBlendProfile")]
        private SpatialBlendProfile _legacySpatialBlendProfile;

        [SerializeField, HideInInspector, FormerlySerializedAs("MonoDistance")]
        private float _legacyMonoDistance = 1f;

        [SerializeField, HideInInspector, FormerlySerializedAs("FullSpatialDistance")]
        private float _legacyFullSpatialDistance = 3f;

        /// <summary>Moves legacy serialized acoustic fields into the local selection once.</summary>
        /// <returns>True only when legacy data was converted.</returns>
        public bool UpgradeAcoustics()
        {
            _acoustics ??= new SoundAcousticSelection();
            if (_acousticsVersion >= CurrentAcousticsVersion) return false;

            _acoustics.Local = new SoundAcousticSettings
            {
                DistanceAttenuation = _legacyDistanceAttenuation,
                AttenuationProfile = _legacyAttenuationProfile,
                MinDistance = _legacyMinDistance,
                MaxDistance = _legacyMaxDistance,
                InheritSpatialBlend = _legacyInheritSpatialBlend,
                SpatialBlendProfile = _legacySpatialBlendProfile,
                MonoDistance = _legacyMonoDistance,
                FullSpatialDistance = _legacyFullSpatialDistance,
            };
            _acousticsVersion = CurrentAcousticsVersion;
            return true;
        }

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
