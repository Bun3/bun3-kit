using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 방치형 확장용 <see cref="BigNum"/> 산술 구현.
    /// BigNum 덧셈은 보존 범위(유효 18~19자리) 밖의 항을 흡수하는 손실 연산이다 —
    /// 방치형 수량 의미론으로 수용한다. 전량 소모(잔량 == 소모량)는 뺄셈이 정확히
    /// Zero를 돌려주므로 안전하다. 지수 한계(1e8) 초과는 false로 보고한다.
    /// </summary>
    public struct BigNumQuantityOps : IQuantityOps<BigNum>
    {
        /// <inheritdoc />
        public BigNum Zero => BigNum.Zero;

        /// <inheritdoc />
        public int Compare(BigNum a, BigNum b) => a.CompareTo(b);

        /// <inheritdoc />
        public BigNum Negate(BigNum value) => -value;

        /// <inheritdoc />
        public BigNum FromLong(long value) => value;

        /// <inheritdoc />
        public bool TryAdd(BigNum a, BigNum b, out BigNum result)
        {
            try
            {
                result = a + b;
                return true;
            }
            catch (BigNumOverflowException)
            {
                result = BigNum.Zero;
                return false;
            }
        }
    }
}
