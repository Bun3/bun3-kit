namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Planar simulation settings. Override getters to supply live application tuning.</summary>
    public class PlanarAcousticSettings
    {
        /// <summary>Maximum simultaneously registered sources, fixed at construction.</summary>
        public virtual int SourceCapacity => 64;
        /// <summary>Volumetric occlusion sampling radius, applied when a source registers.</summary>
        public virtual float OcclusionRadius => 0.5f;
        /// <summary>Number of occlusion samples per source, from one through 32.</summary>
        public virtual int OcclusionSamples => 8;
        /// <summary>Live gain at full obstruction, interpolated with direct occlusion.</summary>
        public virtual float ObstructedPathGain => 0.3f;
        /// <summary>Optional shared world-default stereo-width profile; null uses the inline range.</summary>
        public virtual Bun3.Unity.Audio.SpatialBlendProfile SpatialBlendProfile => null;
        /// <summary>Native path distance at or below which output has identical left/right channels.</summary>
        public virtual float MonoDistance => 0;
        /// <summary>Native path distance at which full stereo width returns. Zero disables near-field collapse.</summary>
        public virtual float FullSpatialDistance => 0;

        /// <summary>Maps a native path distance to smooth stereo width. Invalid or disabled ranges retain full spatial output.</summary>
        public float EvaluateSpatialBlend(float distance)
        {
            var profile = SpatialBlendProfile;
            return profile != null ? profile.Evaluate(distance) :
                Bun3.Unity.Audio.SpatialBlendProfile.Evaluate(distance, MonoDistance, FullSpatialDistance);
        }
    }
}
