using System;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 보상 추첨용 난수 시섬 — 서버 권위 RNG든 결정론 시뮬레이션 RNG든 게임이 주입한다
    /// (idlez의 crypto/IRng 이중 경로가 함수 2벌 복제로 이어진 교훈: 시섬 하나로 통일).
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>[0, <paramref name="maxExclusive"/>) 균등 난수. maxExclusive는 양수.</summary>
        long Next(long maxExclusive);
    }

    /// <summary><see cref="Random"/> 어댑터 — 개발·비결정론 경로용 기본 구현.</summary>
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _random;

        /// <summary>지정한 Random으로 만든다.</summary>
        public SystemRandomSource(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>시간 시드 Random으로 만든다.</summary>
        public SystemRandomSource() : this(new Random())
        {
        }

        /// <inheritdoc />
        public long Next(long maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "양수여야 합니다.");
            }

            if (maxExclusive <= int.MaxValue)
            {
                return _random.Next((int)maxExclusive);
            }

            // netstandard2.1 Random에는 NextInt64가 없다 — 63비트 조합 + 기각 샘플링으로 균등 보장.
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
