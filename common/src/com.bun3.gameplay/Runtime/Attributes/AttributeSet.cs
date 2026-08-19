#nullable enable
using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>
    /// Dense slot set for the attributes an archetype declares. Base writes always pass through
    /// clamping; Current changes are appended to the event buffer.
    /// </summary>
    public sealed class AttributeSet
    {
        private struct Slot
        {
            public ushort AttributeId;
            public BigNum Base;
            public BigNum SumAdd;
            public BigNum SumMulPct;
            public bool HasOverride;
            public BigNum OverrideValue;
            public BigNum Current;
            public bool Dirty;
            public System.Collections.Generic.List<ModifierEntry>? Modifiers;
        }

        private struct ModifierEntry
        {
            public IAttributeModifierSource Source;
            public int RowIndex;
            public AttributeModifierOp Op;
            public BigNum Magnitude;
            public bool ScaleWithStack;
        }

        /// <summary>One modifier row attached by a source, captured for snapshot storage.
        /// Magnitude is the value evaluated at apply time (not re-evaluated) and is reattached as-is on restore.</summary>
        internal readonly struct ModifierSnapshotRow
        {
            internal ModifierSnapshotRow(
                ushort attributeId, int rowIndex, AttributeModifierOp op, BigNum magnitude, bool scaleWithStack)
            {
                AttributeId = attributeId;
                RowIndex = rowIndex;
                Op = op;
                Magnitude = magnitude;
                ScaleWithStack = scaleWithStack;
            }

            internal ushort AttributeId { get; }
            internal int RowIndex { get; }
            internal AttributeModifierOp Op { get; }
            internal BigNum Magnitude { get; }
            internal bool ScaleWithStack { get; }
        }

        private readonly AttributeRegistry _registry;
        private readonly Slot[] _slots;                    // canonical: ascending AttributeId
        private readonly int[] _slotByAttributeId;         // sparse → dense (size = max registered id + 1, -1 = absent)
        private AttributeChange[] _changes = new AttributeChange[8];
        private int _changeCount;

        /// <summary>Builds dense slots from the attribute ids the archetype declares. Declaration order is irrelevant.</summary>
        public AttributeSet(AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            var ids = attributeIds.ToArray();
            Array.Sort(ids);
            var maxId = 0;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!registry.Contains(ids[i]))
                    throw new ArgumentException($"Attribute {ids[i]} is not registered.", nameof(attributeIds));
                if (i > 0 && ids[i] == ids[i - 1])
                    throw new ArgumentException($"Attribute {ids[i]} is declared more than once.", nameof(attributeIds));
                if (ids[i] > maxId) maxId = ids[i];
            }

            _slots = new Slot[ids.Length];
            _slotByAttributeId = new int[maxId + 1];
            Array.Fill(_slotByAttributeId, -1);
            for (var i = 0; i < ids.Length; i++)
            {
                _slots[i].AttributeId = ids[i];
                _slotByAttributeId[ids[i]] = i;
            }

            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                var definition = registry.GetDefinition(id);
                if (definition.Min.HasValue && definition.Min.Value.Kind == OperandKind.Attribute)
                {
                    var refId = definition.Min.Value.AttributeId;
                    if (!Has(refId))
                        throw new ArgumentException(
                            $"Clamp of attribute {id} references {refId}, which is not in the archetype declaration.",
                            nameof(attributeIds));
                }

                if (definition.Max.HasValue && definition.Max.Value.Kind == OperandKind.Attribute)
                {
                    var refId = definition.Max.Value.AttributeId;
                    if (!Has(refId))
                        throw new ArgumentException(
                            $"Clamp of attribute {id} references {refId}, which is not in the archetype declaration.",
                            nameof(attributeIds));
                }
            }
        }

        /// <summary>Returns whether this set declares the attribute.</summary>
        public bool Has(ushort attributeId) =>
            attributeId < _slotByAttributeId.Length && _slotByAttributeId[attributeId] >= 0;

        private int SlotIndex(ushort attributeId)
        {
            if (!Has(attributeId))
                throw new ArgumentOutOfRangeException(nameof(attributeId), attributeId, "Attribute is not declared.");
            return _slotByAttributeId[attributeId];
        }

        /// <summary>Gets the persistent Base value.</summary>
        public BigNum GetBase(ushort attributeId) => _slots[SlotIndex(attributeId)].Base;

        /// <summary>Number of declared attributes (slot count, ascending id order of the attributeIds passed to the constructor).</summary>
        internal int DeclaredCount => _slots.Length;

        /// <summary>Gets the attribute id at a declaration-order (ascending id) index. Snapshot save/restore only.</summary>
        internal ushort DeclaredAttributeIdAt(int index) => _slots[index].AttributeId;

        /// <summary>Gets Base at a declaration-order index. Snapshot save only.</summary>
        internal BigNum DeclaredBaseAt(int index) => _slots[index].Base;

        /// <summary>
        /// Snapshot restore only — writes Base as-is (no clamp, events, or propagation) and marks the slot dirty.
        /// The stored Base already passed clamping via <see cref="SetBase"/>, so re-clamping is unnecessary.
        /// The caller must restore all slots in <see cref="DeclaredAttributeIdAt"/> order (same as constructor order),
        /// then call <see cref="RebuildDirty"/> once to rebuild Current.
        /// </summary>
        internal void RestoreDeclaredBase(int index, BigNum rawBase)
        {
            _slots[index].Base = rawBase;
            _slots[index].Dirty = true;
        }

        /// <summary>Snapshot save only — collects the modifier rows attached by this source via a slot scan.</summary>
        internal void CollectModifiers(IAttributeModifierSource source, System.Collections.Generic.List<ModifierSnapshotRow> output)
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                var modifiers = _slots[i].Modifiers;
                if (modifiers is null) continue;
                for (var j = 0; j < modifiers.Count; j++)
                {
                    var entry = modifiers[j];
                    if (!ReferenceEquals(entry.Source, source)) continue;
                    output.Add(new ModifierSnapshotRow(
                        _slots[i].AttributeId, entry.RowIndex, entry.Op, entry.Magnitude, entry.ScaleWithStack));
                }
            }
        }

        /// <summary>Gets Current with aggregation and clamping applied.</summary>
        public BigNum GetCurrent(ushort attributeId) => _slots[SlotIndex(attributeId)].Current;

        /// <summary>Sets Base. Always passes through clamping; Current updates immediately.</summary>
        public void SetBase(ushort attributeId, BigNum value)
        {
            var index = SlotIndex(attributeId);
            _slots[index].Base = ClampToBounds(index, value);
            ReapplyFormula(index);
        }

        /// <summary>Adds a delta to Base. Always passes through clamping.</summary>
        public void AddBase(ushort attributeId, BigNum delta)
        {
            var index = SlotIndex(attributeId);
            SetBase(attributeId, _slots[index].Base + delta);
        }

        private BigNum ResolveBound(Operand bound)
        {
            if (bound.Kind == OperandKind.Constant) return bound.Value;
            // attribute-referencing bounds are validated in the constructor — existence guaranteed
            return _slots[_slotByAttributeId[bound.AttributeId]].Current * bound.Value;
        }

        private BigNum ClampToBounds(int slotIndex, BigNum value)
        {
            var definition = _registry.GetDefinition(_slots[slotIndex].AttributeId);
            if (definition.Min.HasValue)
            {
                var min = ResolveBound(definition.Min.Value);
                if (value < min) value = min;
            }

            if (definition.Max.HasValue)
            {
                var max = ResolveBound(definition.Max.Value);
                if (value > max) value = max;
            }

            return value;
        }

        // O(1) formula reapply via cached Σ aggregates.
        internal void ReapplyFormula(int slotIndex)
        {
            ref var slot = ref _slots[slotIndex];
            var value = slot.HasOverride
                ? slot.OverrideValue
                : (slot.Base + slot.SumAdd) * (BigNum.One + slot.SumMulPct);
            var clamped = ClampToBounds(slotIndex, value);
            var old = slot.Current;
            if (clamped.Equals(old)) return;
            var attributeId = slot.AttributeId;
            slot.Current = clamped;
            EmitChange(attributeId, old, clamped);
            PropagateToDependents(attributeId, old, clamped);
        }

        // Propagates a Current change (old→new) to attributes that reference it as a clamp bound,
        // moving Base per each dependent's OnMaxIncrease/OnMaxDecrease policy before reapplying.
        // The dependency graph is a DAG validated at registry build, so recursion is safe.
        private void PropagateToDependents(ushort changedAttributeId, BigNum oldValue, BigNum newValue)
        {
            var dependents = _registry.GetClampDependents(changedAttributeId);
            for (var i = 0; i < dependents.Length; i++)
            {
                if (!Has(dependents[i])) continue;
                var index = _slotByAttributeId[dependents[i]];
                var definition = _registry.GetDefinition(dependents[i]);
                var maxOperand = definition.Max.GetValueOrDefault();
                var referencesAsMax = definition.Max.HasValue
                    && maxOperand.Kind == OperandKind.Attribute
                    && maxOperand.AttributeId == changedAttributeId;

                if (referencesAsMax && newValue > oldValue
                    && definition.OnMaxIncrease == MaxIncreasePolicy.Follow)
                {
                    var delta = (newValue - oldValue) * maxOperand.Value;   // coefficient applied
                    _slots[index].Base = ClampToBounds(index, _slots[index].Base + delta);
                }

                if (referencesAsMax && newValue < oldValue
                    && definition.OnMaxDecrease == MaxDecreasePolicy.Follow)
                {
                    _slots[index].Base = ClampToBounds(index, _slots[index].Base);    // truncate Base to the lowered bound
                }

                ReapplyFormula(index);   // Stay: reapply alone is the safety net
            }
        }

        /// <summary>Attaches a modifier. Insertion-sorted ascending by (source.Id, rowIndex) to keep canonical order.</summary>
        internal void AttachModifier(
            IAttributeModifierSource source, int rowIndex, ushort attributeId,
            AttributeModifierOp op, BigNum magnitude, bool scaleWithStack)
        {
            var index = SlotIndex(attributeId);
            ref var slot = ref _slots[index];
            slot.Modifiers ??= new System.Collections.Generic.List<ModifierEntry>(4);
            var entry = new ModifierEntry
            {
                Source = source, RowIndex = rowIndex, Op = op,
                Magnitude = magnitude, ScaleWithStack = scaleWithStack,
            };
            var position = slot.Modifiers.Count;
            while (position > 0)
            {
                var previous = slot.Modifiers[position - 1];
                if (previous.Source.Id < source.Id
                    || (previous.Source.Id == source.Id && previous.RowIndex <= rowIndex)) break;
                position--;
            }
            slot.Modifiers.Insert(position, entry);
            slot.Dirty = true;
        }

        /// <summary>Removes all modifiers attached by the source.</summary>
        internal void DetachModifiers(IAttributeModifierSource source)
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                var modifiers = _slots[i].Modifiers;
                if (modifiers is null) continue;
                for (var j = modifiers.Count - 1; j >= 0; j--)
                {
                    if (ReferenceEquals(modifiers[j].Source, source))
                    {
                        modifiers.RemoveAt(j);
                        _slots[i].Dirty = true;
                    }
                }
            }
        }

        /// <summary>Marks the attribute slot dirty (e.g. on an Enabled toggle).</summary>
        internal void MarkDirty(ushort attributeId) => _slots[SlotIndex(attributeId)].Dirty = true;

        /// <summary>Rebuilds all dirty slots in canonical order, following the clamp topology (registry EvaluationOrder).</summary>
        internal void RebuildDirty()
        {
            var order = _registry.EvaluationOrder;
            for (var i = 0; i < order.Length; i++)
            {
                if (!Has(order[i])) continue;
                var index = _slotByAttributeId[order[i]];
                if (!_slots[index].Dirty) continue;
                RebuildSlot(index);
            }
        }

        private void RebuildSlot(int index)
        {
            ref var slot = ref _slots[index];
            slot.Dirty = false;
            slot.SumAdd = BigNum.Zero;
            slot.SumMulPct = BigNum.Zero;
            slot.HasOverride = false;
            slot.OverrideValue = BigNum.Zero;
            var modifiers = slot.Modifiers;
            if (modifiers is not null)
            {
                for (var i = 0; i < modifiers.Count; i++)   // list is kept in canonical order
                {
                    var entry = modifiers[i];
                    if (!entry.Source.Enabled) continue;
                    var magnitude = entry.ScaleWithStack ? entry.Magnitude * entry.Source.Stack : entry.Magnitude;
                    switch (entry.Op)
                    {
                        case AttributeModifierOp.Add:
                            slot.SumAdd += magnitude;
                            break;
                        case AttributeModifierOp.Multiply:
                            slot.SumMulPct += magnitude;
                            break;
                        default:   // Override — list is Id-ordered, so the last enabled entry wins
                            slot.HasOverride = true;
                            slot.OverrideValue = magnitude;
                            break;
                    }
                }
            }

            ReapplyFormula(index);
        }

        private void EmitChange(ushort attributeId, BigNum oldCurrent, BigNum newCurrent)
        {
            if (_changeCount == _changes.Length)
                Array.Resize(ref _changes, _changes.Length * 2);
            _changes[_changeCount++] = new AttributeChange(attributeId, oldCurrent, newCurrent);
        }

        /// <summary>Change events not yet consumed.</summary>
        public ReadOnlySpan<AttributeChange> PendingChanges => _changes.AsSpan(0, _changeCount);

        /// <summary>Clears the change event buffer.</summary>
        public void ClearChanges() => _changeCount = 0;
    }
}
