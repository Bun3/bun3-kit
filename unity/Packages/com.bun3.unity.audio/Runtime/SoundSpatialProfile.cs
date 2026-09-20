using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Shared spatial settings for sound definitions. Assigning this asset replaces the entire local group.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Sound Spatial Profile", fileName = "SoundSpatialProfile")]
    public sealed class SoundSpatialProfile : ScriptableObject
    {
        /// <summary>Spatialization mode.</summary>
        public SpatialMode Spatial = SpatialMode.None;

        /// <summary>Local or shared acoustic settings.</summary>
        public SoundAcousticSelection Acoustics => _acoustics;

        /// <summary>Whether this sound participates in occlusion evaluation (3D sounds only).</summary>
        public bool Occlusion;

        /// <summary>Optional full-obstruction gain override; a negative value uses system settings.</summary>
        public float OcclusionVolumeAtFull = -1f;

        [SerializeField, HideInInspector]
        private SoundAcousticSelection _acoustics = new();
    }
}
