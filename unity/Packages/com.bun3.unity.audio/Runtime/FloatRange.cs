using System;

namespace Bun3.Unity.Audio
{
    /// <summary>Inclusive [Min, Max] range rolled per play for volume/pitch variation.</summary>
    [Serializable]
    public struct FloatRange
    {
        /// <summary>Lower bound (inclusive).</summary>
        public float Min;

        /// <summary>Upper bound (inclusive).</summary>
        public float Max;

        /// <summary>Creates a range with the given bounds.</summary>
        public FloatRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Returns a uniformly random value in [Min, Max] using the caller's stream.
        /// Takes an explicit <see cref="Random"/> so cosmetic audio variation never
        /// consumes the engine's global random stream (seeded gameplay stays unperturbed).
        /// </summary>
        public float Roll(Random rng) => Min + (float)rng.NextDouble() * (Max - Min);
    }
}
