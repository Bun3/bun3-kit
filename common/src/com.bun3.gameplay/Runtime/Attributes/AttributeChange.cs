#nullable enable
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>속성 Current 변경 이벤트입니다. 복제 큐와 게임 구독이 소비합니다.</summary>
    public readonly struct AttributeChange
    {
        internal AttributeChange(ushort attributeId, BigNum oldCurrent, BigNum newCurrent)
        {
            AttributeId = attributeId;
            OldCurrent = oldCurrent;
            NewCurrent = newCurrent;
        }

        /// <summary>변경된 속성 id입니다.</summary>
        public ushort AttributeId { get; }

        /// <summary>변경 전 Current입니다.</summary>
        public BigNum OldCurrent { get; }

        /// <summary>변경 후 Current입니다.</summary>
        public BigNum NewCurrent { get; }
    }
}
