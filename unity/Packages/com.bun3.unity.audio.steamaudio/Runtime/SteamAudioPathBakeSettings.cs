using System;
using UnityEngine;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Copied, serializable settings for a cold native pathing bake.</summary>
    [Serializable]
    public struct SteamAudioPathBakeSettings
    {
        [SerializeField] int visibilitySamples;
        [SerializeField] float visibilityRadius, visibilityThreshold, visibilityRange, pathRange;
        [SerializeField] int threads;

        /// <summary>Creates validated pathing bake settings.</summary>
        public SteamAudioPathBakeSettings(int visibilitySamples, float visibilityRadius, float visibilityThreshold,
            float visibilityRange, float pathRange, int threads)
        {
            this.visibilitySamples = visibilitySamples; this.visibilityRadius = visibilityRadius;
            this.visibilityThreshold = visibilityThreshold; this.visibilityRange = visibilityRange;
            this.pathRange = pathRange; this.threads = threads;
            Validate();
        }
        /// <summary>Gets deterministic small-scene defaults; callers must tune coverage and range for their geometry.</summary>
        public static SteamAudioPathBakeSettings Default => new SteamAudioPathBakeSettings(1, 0.05f, 0.99f, 4.5f, 100, 1);
        /// <summary>Gets visibility samples per probe pair.</summary>
        public int VisibilitySamples => visibilitySamples;
        /// <summary>Gets the probe-to-probe visibility sampling radius in meters.</summary>
        public float VisibilityRadius => visibilityRadius;
        /// <summary>Gets the required unoccluded visibility fraction.</summary>
        public float VisibilityThreshold => visibilityThreshold;
        /// <summary>Gets the maximum candidate probe-link distance in meters.</summary>
        public float VisibilityRange => visibilityRange;
        /// <summary>Gets the maximum baked path range in meters.</summary>
        public float PathRange => pathRange;
        /// <summary>Gets the native bake worker count.</summary>
        public int Threads => threads;

        internal void Validate()
        {
            if (visibilitySamples <= 0) throw new ArgumentOutOfRangeException(nameof(VisibilitySamples));
            if (!Finite(visibilityRadius) || visibilityRadius < 0) throw new ArgumentOutOfRangeException(nameof(VisibilityRadius));
            if (!Finite(visibilityThreshold) || visibilityThreshold < 0 || visibilityThreshold > 1) throw new ArgumentOutOfRangeException(nameof(VisibilityThreshold));
            if (!Finite(visibilityRange) || visibilityRange <= 0) throw new ArgumentOutOfRangeException(nameof(VisibilityRange));
            if (!Finite(pathRange) || pathRange <= 0) throw new ArgumentOutOfRangeException(nameof(PathRange));
            if (threads <= 0) throw new ArgumentOutOfRangeException(nameof(Threads));
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
