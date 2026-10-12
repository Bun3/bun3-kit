#nullable enable
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>Attribute Current change event, consumed by the replication queue and game subscriptions.</summary>
    public readonly struct AttributeChange
    {
        internal AttributeChange(TargetId target, ushort attributeId, BigNum oldCurrent, BigNum newCurrent)
        {
            Target = target;
            AttributeId = attributeId;
            OldCurrent = oldCurrent;
            NewCurrent = newCurrent;
        }

        /// <summary>Unit whose attribute changed.</summary>
        public TargetId Target { get; }

        /// <summary>Changed attribute id.</summary>
        public ushort AttributeId { get; }

        /// <summary>Current before the change.</summary>
        public BigNum OldCurrent { get; }

        /// <summary>Current after the change.</summary>
        public BigNum NewCurrent { get; }
    }
}
