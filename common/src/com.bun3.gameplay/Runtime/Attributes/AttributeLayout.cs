#nullable enable
using System;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>Validated slot layout of one attribute declaration, shared by every set built from it.</summary>
    internal sealed class AttributeLayout
    {
        /// <exception cref="ArgumentException">Thrown for an unregistered or repeated attribute, or a clamp bound
        /// that references an attribute outside the declaration.</exception>
        internal AttributeLayout(AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            var ids = attributeIds.ToArray();
            Array.Sort(ids);
            var maxId = 0;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!registry.Contains(ids[i]))
                    throw new ArgumentException($"Attribute {ids[i]} is not registered.", nameof(attributeIds));
                if (i > 0 && ids[i] == ids[i - 1])
                    throw new ArgumentException($"Attribute {registry.Describe(ids[i])} is declared more than once.", nameof(attributeIds));
                if (ids[i] > maxId) maxId = ids[i];
            }

            AttributeIds = ids;
            SlotByAttributeId = new int[maxId + 1];
            Array.Fill(SlotByAttributeId, -1);
            for (var i = 0; i < ids.Length; i++) SlotByAttributeId[ids[i]] = i;

            for (var i = 0; i < ids.Length; i++)
            {
                var attribute = registry.GetCompiledAttribute(ids[i]);
                RequireDeclared(registry, ids[i], attribute.Min);
                RequireDeclared(registry, ids[i], attribute.Max);
            }

            var order = registry.EvaluationOrder;
            EvaluationSlots = new int[ids.Length];
            var written = 0;
            for (var i = 0; i < order.Length; i++)
            {
                if (Has(order[i])) EvaluationSlots[written++] = SlotByAttributeId[order[i]];
            }
        }

        internal AttributeRegistry Registry { get; }

        /// <summary>Declared attribute ids, ascending; the index is the slot index.</summary>
        internal ushort[] AttributeIds { get; }

        /// <summary>Attribute id to slot index, -1 when undeclared.</summary>
        internal int[] SlotByAttributeId { get; }

        /// <summary>Slot indices in clamp evaluation order (bounds before the values they clamp).</summary>
        internal int[] EvaluationSlots { get; }

        internal bool Has(ushort attributeId) =>
            attributeId < SlotByAttributeId.Length && SlotByAttributeId[attributeId] >= 0;

        private void RequireDeclared(AttributeRegistry registry, ushort owner, Operand? bound)
        {
            if (!bound.HasValue || bound.Value.Kind != OperandKind.Attribute || Has(bound.Value.AttributeId)) return;
            throw new ArgumentException(
                $"Clamp of attribute {registry.Describe(owner)} references {registry.Describe(bound.Value.AttributeId)}, which is not in the archetype declaration.",
                "attributeIds");
        }
    }
}
