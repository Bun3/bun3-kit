#nullable enable
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>Attribute Current change event, consumed by the replication queue and game subscriptions.</summary>
    public readonly struct AttributeChange
    {
        internal AttributeChange(ushort attributeId, BigNum oldCurrent, BigNum newCurrent)
        {
            AttributeId = attributeId;
            OldCurrent = oldCurrent;
            NewCurrent = newCurrent;
        }

        /// <summary>Changed attribute id.</summary>
        public ushort AttributeId { get; }

        /// <summary>Current before the change.</summary>
        public BigNum OldCurrent { get; }

        /// <summary>Current after the change.</summary>
        public BigNum NewCurrent { get; }
    }
}
