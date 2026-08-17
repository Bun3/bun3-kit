#nullable enable
using System;

namespace Bun3.Gameplay
{
    /// <summary>난수 생성기 인터페이스입니다.</summary>
    public interface IRng
    {
        /// <summary>다음 부호 없는 32비트 난수를 반환합니다.</summary>
        /// <returns>생성된 난수입니다.</returns>
        uint NextUInt32();
    }

    /// <summary>xorshift64* 알고리즘을 사용하는 난수 생성기입니다.</summary>
    public struct XorShiftRng : IRng
    {
        private ulong _state;

        /// <summary>주어진 시드로 난수 생성기를 초기화합니다.</summary>
        /// <param name="seed">초기 상태입니다. 0은 허용되지 않습니다.</param>
        /// <exception cref="ArgumentOutOfRangeException">시드가 0일 때 발생합니다.</exception>
        public XorShiftRng(ulong seed)
        {
            if (seed == 0)
                throw new ArgumentOutOfRangeException(nameof(seed), "시드는 0이 될 수 없습니다.");
            _state = seed;
        }

        /// <summary>다음 부호 없는 32비트 난수를 반환합니다.</summary>
        /// <returns>생성된 난수입니다.</returns>
        public uint NextUInt32()
        {
            ulong x = _state;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state = x;
            return (uint)((x * 0x2545F4914F6CDD1DUL) >> 32);
        }
    }
}
