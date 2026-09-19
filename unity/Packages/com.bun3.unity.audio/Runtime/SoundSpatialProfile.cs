using UnityEngine;
using UnityEngine.Serialization;

namespace Bun3.Unity.Audio
{
    /// <summary>Shared spatial settings for sound definitions. Assigning this asset replaces the entire local group.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Spatial Profile", fileName = "SoundSpatialProfile")]
    public sealed class SoundSpatialProfile : ScriptableObject
    {
        private const int CurrentAcousticsVersion = 1;

        /// <summary>Spatialization mode.</summary>
        public SpatialMode Spatial = SpatialMode.None;

        /// <summary>Acoustic settings, upgraded from legacy serialized fields on first access.</summary>
        public SoundAcousticSelection Acoustics
        {
            get
            {
                UpgradeAcoustics();
                return _acoustics;
            }
        }

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
    }
}
