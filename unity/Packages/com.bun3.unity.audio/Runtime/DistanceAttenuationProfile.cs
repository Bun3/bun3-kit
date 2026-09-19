using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Immutable copy of an authored Unity curve, safe from later profile edits.</summary>
    public sealed class DistanceAttenuationCurve
    {
        readonly AnimationCurve curve;
        /// <summary>Gets the last key distance; distances at or beyond it are silent.</summary>
        public float MaximumDistance { get; }
        /// <summary>Copies all keys, tangents and weights. Construction is a cold operation.</summary>
        public DistanceAttenuationCurve(AnimationCurve source)
        {
            curve = source == null ? new AnimationCurve() : new AnimationCurve(source.keys);
            if (source != null) { curve.preWrapMode = source.preWrapMode; curve.postWrapMode = source.postWrapMode; }
            MaximumDistance = curve.length == 0 ? 0 : curve[curve.length - 1].time;
        }
        internal bool Matches(AnimationCurve source) => curve.Equals(source);
        /// <summary>Evaluates distance with nonfinite input rejected and output clamped to [0,1]. Empty curves are silent.</summary>
        public float Evaluate(float distance)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0 || curve.length == 0 ||
                float.IsNaN(MaximumDistance) || float.IsInfinity(MaximumDistance) || distance >= MaximumDistance) return 0;
            float gain = curve.Evaluate(distance);
            return float.IsNaN(gain) || float.IsInfinity(gain) ? 0 : Math.Max(0, Math.Min(1, gain));
        }
    }

    /// <summary>Reusable distance curve consumed by spatial audio adapters.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Distance Attenuation Profile")]
    public sealed class DistanceAttenuationProfile : ScriptableObject
    {
        [SerializeField, Tooltip("X: native path distance in metres. Y: volume multiplier (0 to 1). At and beyond the last key distance, output is zero. Put the last key at volume zero for a smooth end.")]
        AnimationCurve volumeByDistance;
        [SerializeField, HideInInspector] bool curveInitialized;
        DistanceAttenuationCurve snapshot;

        /// <summary>Gets or replaces the editable distance-in-metres/volume curve. In-place key edits are detected on snapshot access.</summary>
        public AnimationCurve VolumeByDistance
        {
            get
            {
                if (!curveInitialized)
                {
                    if (volumeByDistance == null || volumeByDistance.length == 0)
                        volumeByDistance = CreateLegacyCurve(MinimumDistance, MaximumDistance, FadeFraction);
                    curveInitialized = true;
                }
                volumeByDistance ??= new AnimationCurve();
                return volumeByDistance;
            }
            set { volumeByDistance = value ?? new AnimationCurve(); curveInitialized = true; snapshot = null; }
        }

        /// <summary>Returns an immutable native-callback snapshot, allocating only when the authored curve changes. Control thread only.</summary>
        public DistanceAttenuationCurve GetSnapshot()
        {
            var authored = VolumeByDistance;
            if (snapshot == null || !snapshot.Matches(authored)) snapshot = new DistanceAttenuationCurve(authored);
            return snapshot;
        }

        void OnEnable() { _ = VolumeByDistance; }

        /// <summary>Converts a legacy inverse-distance profile to editable Hermite keys, including both transition points.</summary>
        public static AnimationCurve CreateLegacyCurve(float minimum, float maximum, float fadeFraction)
        {
            if (float.IsNaN(maximum) || float.IsInfinity(maximum) || maximum <= 0) maximum = 15;
            var times = new List<float>(24) { 0, maximum };
            float start = Math.Min(maximum, Math.Max(.01f, minimum));
            for (int i = 1; i < 20; i++) times.Add(start * (float)Math.Pow(maximum / start, i / 20d));
            if (minimum > 0 && minimum < maximum) times.Add(minimum);
            float edge = maximum * (1 - fadeFraction);
            if (edge > 0 && edge < maximum) times.Add(edge);
            times.Sort();
            var keys = new List<Keyframe>(times.Count);
            for (int i = 0; i < times.Count; i++)
            {
                float d = times[i];
                if (i > 0 && d == times[i - 1]) continue;
                float epsilon = Math.Max(.000001f, maximum * .000001f);
                float gain = Evaluate(d, minimum, maximum, fadeFraction);
                float incoming = LegacySlope(Math.Max(0, d - epsilon), minimum, maximum, fadeFraction);
                float outgoing = LegacySlope(d + epsilon, minimum, maximum, fadeFraction);
                keys.Add(new Keyframe(d, gain, incoming, outgoing));
            }
            return new AnimationCurve(keys.ToArray());
        }

        static float LegacySlope(float distance, float minimum, float maximum, float fade)
        {
            if (distance >= maximum || fade <= 0) return 0;
            float inverse = distance <= minimum ? 1 : minimum / distance;
            float slope = distance <= minimum ? 0 : -minimum / (distance * distance);
            float edge = maximum * (1 - fade);
            return distance < edge ? slope : slope * (maximum - distance) / (maximum * fade) - inverse / (maximum * fade);
        }

        /// <summary>Legacy migration input. Edit VolumeByDistance for current profiles.</summary>
        [HideInInspector]
        public float MinimumDistance = 1;
        /// <summary>Legacy migration input for the last key distance.</summary>
        [HideInInspector]
        public float MaximumDistance = 15;
        /// <summary>Legacy migration input for the edge fade.</summary>
        [HideInInspector]
        public float FadeFraction = .2f;

        /// <summary>Evaluates the adapter curve using scalar snapshots, without Unity object access.</summary>
        public static float Evaluate(float distance, float minimum, float maximum, float fadeFraction, bool enabled = true)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0) return 0;
            if (!enabled) return 1;
            if (float.IsNaN(minimum) || float.IsInfinity(minimum) || minimum < 0 ||
                float.IsNaN(maximum) || maximum <= 0 || float.IsNaN(fadeFraction) ||
                float.IsInfinity(fadeFraction) || fadeFraction <= 0 || fadeFraction > 1) return 0;
            float gain = distance <= minimum ? 1 : minimum / distance;
            return float.IsPositiveInfinity(maximum) ? gain : gain * System.Math.Max(0,
                System.Math.Min(1, (maximum - distance) / (maximum * fadeFraction)));
        }
    }
}
