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

    /// <summary>
    /// xorshift64* 알고리즘을 사용하는 난수 생성기입니다. 가변 상태를 가진 sealed class입니다 — struct였다면
    /// IRng로 박싱되거나 값으로 전달·복사될 때 스트림이 조용히 분기하는 결정론 함정이 있었습니다. 기동 시
    /// 1회 생성이 일반적이라 클래스 할당은 무해합니다.
    /// </summary>
    public sealed class XorShiftRng : IRng
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

        /// <summary>현재 내부 상태를 그대로 가진 새 인스턴스를 만듭니다. 클래스라 대입은 참조 공유이므로,
        /// 특정 시점의 스트림 상태를 독립적으로 보존해야 할 때(예: 스냅샷) 명시적으로 호출해야 합니다.</summary>
        /// <returns>같은 상태를 가진 새 인스턴스입니다.</returns>
        public XorShiftRng Clone() => new XorShiftRng(_state);
    }
}
