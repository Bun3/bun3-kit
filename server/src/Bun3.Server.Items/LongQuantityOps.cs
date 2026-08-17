namespace Bun3.Server.Items
{
    /// <summary>기본 수량 타입 long의 산술 구현 — 오버플로는 false로 보고한다.</summary>
    public struct LongQuantityOps : IQuantityOps<long>
    {
        /// <inheritdoc />
        public long Zero => 0;

        /// <inheritdoc />
        public int Compare(long a, long b) => a.CompareTo(b);

        /// <inheritdoc />
        public long Negate(long value) => -value;

        /// <inheritdoc />
        public long FromLong(long value) => value;

        /// <inheritdoc />
        public bool TryAdd(long a, long b, out long result)
        {
            result = unchecked(a + b);
            if (((a ^ result) & (b ^ result)) < 0)
            {
                result = 0;
                return false;
            }

            return true;
        }
    }
}
