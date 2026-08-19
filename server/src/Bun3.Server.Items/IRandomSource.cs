using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// Random-number seam for reward draws — the game injects either a server-authoritative
    /// RNG or a deterministic simulation RNG through this single interface.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>Uniform random in [0, <paramref name="maxExclusive"/>). maxExclusive must be positive.</summary>
        long Next(long maxExclusive);
    }

    /// <summary><see cref="Random"/> adapter — default implementation for development and non-deterministic paths.</summary>
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _random;

        /// <summary>Creates one from the given Random.</summary>
        public SystemRandomSource(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>Creates one with a time-seeded Random.</summary>
        public SystemRandomSource() : this(new Random())
        {
        }

        /// <inheritdoc />
        public long Next(long maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Must be positive.");
            }

            if (maxExclusive <= int.MaxValue)
            {
                return _random.Next((int)maxExclusive);
            }

            // netstandard2.1 Random has no NextInt64 — combine 63 bits + rejection sampling for uniformity.
            var bound = long.MaxValue - (long.MaxValue % maxExclusive);
            while (true)
            {
                var value = ((long)(uint)_random.Next() << 32 | (uint)_random.Next()) & long.MaxValue;
                if (value < bound)
                {
                    return value % maxExclusive;
                }
            }
        }
    }
}
