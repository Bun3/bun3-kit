using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>Reusable native-path-distance range controlling processed stereo width.</summary>
    [CreateAssetMenu(menuName = "Bun3/Audio/Spatial Blend Profile")]
    public sealed class SpatialBlendProfile : ScriptableObject
    {
        /// <summary>At or below this native path distance, both channels carry the processed mid signal.</summary>
        [Min(0), Tooltip("Native path distance in metres at or below which output is dual mono. Path occlusion and attenuation remain active.")]
        public float MonoDistance = 1;
        /// <summary>At or above this distance, output has full native binaural width. Zero disables mono collapse.</summary>
        [Min(0), Tooltip("Full spatial width distance in metres. Smooth transition from Mono Distance. Zero or a value at/below Mono Distance disables mono collapse.")]
        public float FullSpatialDistance = 3;
        /// <summary>Evaluates the shared live range on the control thread.</summary>
        public float Evaluate(float nativePathDistance) => Evaluate(nativePathDistance, MonoDistance, FullSpatialDistance);
        /// <summary>Evaluates a scalar snapshot without Unity object access; invalid or disabled inputs retain full width.</summary>
        public static float Evaluate(float distance, float near, float far)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0 ||
                float.IsNaN(near) || float.IsInfinity(near) || near < 0 ||
                float.IsNaN(far) || float.IsInfinity(far) || far <= near) return 1;
            if (distance <= near) return 0;
            if (distance >= far) return 1;
            float t = (distance - near) / (far - near);
            return t * t * (3 - 2 * t);
        }
    }
}
