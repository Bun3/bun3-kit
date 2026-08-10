using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>BigNum 지수가 표현 한계를 넘었을 때. 정당한 게임플레이로는 도달 불가능한
    /// 규모이므로, 밸런스 공식 폭주 버그를 숨기지 않기 위해 클램프 대신 던진다(스펙 §6).</summary>
    public sealed class BigNumOverflowException : OverflowException
    {
        /// <summary>지수 값과 함께 예외를 생성한다.</summary>
        public BigNumOverflowException(long exponent)
            : base($"BigNum 지수 {exponent}가 한계(±{BigNum.MaxExponent})를 넘었다 — 공식 폭주를 의심할 것.")
        {
        }
    }
}
