#nullable enable
using System;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>
    /// Dense slot set for the attributes an archetype declares. Base writes always pass through
    /// clamping; Current changes are recorded into the change log of the pipeline the set is attached to
    /// (none when detached, e.g. before spawn).
    /// </summary>
    public sealed class AttributeSet
    {
        private struct Slot
        {
            public BigNum Base;
            public BigNum Current;
            public int Aggregate;   // index into _aggregates once a modifier has attached, -1 before
            public ushort AttributeId;
            public bool Dirty;
        }

        // Cached modifier sums of one slot, so a Base write reapplies the formula without re-summing.
        private struct Aggregate
        {
            public BigNum SumAdd;
            public BigNum SumMulPct;
            public BigNum OverrideValue;
            public bool HasOverride;
        }

        private struct ModifierEntry
        {
            public IAttributeModifierSource Source;
            public BigNum Magnitude;
            public int RowIndex;
            public ushort Slot;
            public AttributeModifierOp Op;
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

        private readonly AttributeLayout _layout;
        private readonly Slot[] _slots;                    // canonical: ascending AttributeId
        private Aggregate[] _aggregates = Array.Empty<Aggregate>();
        private int _aggregateCount;
        private ModifierEntry[] _modifiers = Array.Empty<ModifierEntry>();   // sorted by (Slot, Source.Id, RowIndex)
        private int _modifierCount;
        private AttributeChangeLog? _changeLog;
        private TargetId _targetId;
        private bool _anyDirty;

        /// <summary>Builds dense slots from the attribute ids the archetype declares. Declaration order is irrelevant.</summary>
        public AttributeSet(AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds)
            : this(new AttributeLayout(registry, attributeIds))
        {
        }

        internal AttributeSet(AttributeLayout layout)
        {
            _layout = layout;
            var ids = layout.AttributeIds;
            _slots = new Slot[ids.Length];
            for (var i = 0; i < ids.Length; i++)
            {
                _slots[i].AttributeId = ids[i];
                _slots[i].Aggregate = -1;
            }
        }

        internal AttributeLayout Layout => _layout;

        /// <summary>Returns whether this set declares the attribute.</summary>
        public bool Has(ushort attributeId) => _layout.Has(attributeId);

        private int SlotIndex(ushort attributeId)
        {
            if (!Has(attributeId))
                throw new ArgumentOutOfRangeException(nameof(attributeId), attributeId, "Attribute is not declared.");
            return _layout.SlotByAttributeId[attributeId];
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
            _anyDirty = true;
        }

        /// <summary>Snapshot save only — collects the modifier rows attached by this source, in canonical order.</summary>
        internal void CollectModifiers(IAttributeModifierSource source, System.Collections.Generic.List<ModifierSnapshotRow> output)
        {
            for (var i = 0; i < _modifierCount; i++)
            {
                var entry = _modifiers[i];
                if (!ReferenceEquals(entry.Source, source)) continue;
                output.Add(new ModifierSnapshotRow(
                    _slots[entry.Slot].AttributeId, entry.RowIndex, entry.Op, entry.Magnitude, entry.ScaleWithStack));
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
            // attribute-referencing bounds are validated by the layout — the slot exists
            return _slots[_layout.SlotByAttributeId[bound.AttributeId]].Current * bound.Value;
        }

        private BigNum ClampToBounds(int slotIndex, BigNum value)
        {
            var attribute = _layout.Registry.GetCompiledAttribute(_slots[slotIndex].AttributeId);
            if (attribute.Min.HasValue)
            {
                var min = ResolveBound(attribute.Min.Value);
                if (value < min) value = min;
            }

            if (attribute.Max.HasValue)
            {
                var max = ResolveBound(attribute.Max.Value);
                if (value > max) value = max;
            }

            return value;
        }

        // O(1) formula reapply via cached Σ aggregates. A slot no modifier has touched is (Base + 0) × (1 + 0),
        // whose canonical BigNum is Base itself.
        private void ReapplyFormula(int slotIndex)
        {
            ref var slot = ref _slots[slotIndex];
            BigNum value;
            if (slot.Aggregate < 0)
            {
                value = slot.Base;
            }
            else
            {
                ref var aggregate = ref _aggregates[slot.Aggregate];
                value = aggregate.HasOverride
                    ? aggregate.OverrideValue
                    : (slot.Base + aggregate.SumAdd) * (BigNum.One + aggregate.SumMulPct);
            }

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
            var dependents = _layout.Registry.GetClampDependents(changedAttributeId);
            for (var i = 0; i < dependents.Length; i++)
            {
                if (!Has(dependents[i])) continue;
                var index = _layout.SlotByAttributeId[dependents[i]];
                var attribute = _layout.Registry.GetCompiledAttribute(dependents[i]);
                var maxOperand = attribute.Max.GetValueOrDefault();
                var referencesAsMax = attribute.Max.HasValue
                    && maxOperand.Kind == OperandKind.Attribute
                    && maxOperand.AttributeId == changedAttributeId;

                if (referencesAsMax && newValue > oldValue
                    && attribute.OnMaxIncrease == MaxIncreasePolicy.Follow)
                {
                    var delta = (newValue - oldValue) * maxOperand.Value;   // coefficient applied
                    _slots[index].Base = ClampToBounds(index, _slots[index].Base + delta);
                }

                if (referencesAsMax && newValue < oldValue
                    && attribute.OnMaxDecrease == MaxDecreasePolicy.Follow)
                {
                    _slots[index].Base = ClampToBounds(index, _slots[index].Base);    // truncate Base to the lowered bound
                }

                ReapplyFormula(index);   // the Stay policies keep Base and only re-clamp Current
            }
        }

        /// <summary>Attaches a modifier. Insertion-sorted ascending by (slot, source.Id, rowIndex) to keep canonical order.</summary>
        internal void AttachModifier(
            IAttributeModifierSource source, int rowIndex, ushort attributeId,
            AttributeModifierOp op, BigNum magnitude, bool scaleWithStack)
        {
            var index = SlotIndex(attributeId);
            ref var slot = ref _slots[index];
            if (slot.Aggregate < 0)
            {
                if (_aggregateCount == _aggregates.Length) Array.Resize(ref _aggregates, Math.Max(4, _aggregates.Length * 2));
                slot.Aggregate = _aggregateCount++;
            }

            var position = _modifierCount;
            while (position > 0)
            {
                var previous = _modifiers[position - 1];
                if (previous.Slot < index) break;
                if (previous.Slot == index
                    && (previous.Source.Id < source.Id
                        || (previous.Source.Id == source.Id && previous.RowIndex <= rowIndex))) break;
                position--;
            }

            if (_modifierCount == _modifiers.Length) Array.Resize(ref _modifiers, Math.Max(4, _modifiers.Length * 2));
            Array.Copy(_modifiers, position, _modifiers, position + 1, _modifierCount - position);
            _modifiers[position] = new ModifierEntry
            {
                Source = source, RowIndex = rowIndex, Slot = (ushort)index, Op = op,
                Magnitude = magnitude, ScaleWithStack = scaleWithStack,
            };
            _modifierCount++;
            slot.Dirty = true;
            _anyDirty = true;
        }

        /// <summary>Removes all modifiers attached by the source.</summary>
        internal void DetachModifiers(IAttributeModifierSource source)
        {
            var kept = 0;
            for (var i = 0; i < _modifierCount; i++)
            {
                var entry = _modifiers[i];
                if (ReferenceEquals(entry.Source, source))
                {
                    _slots[entry.Slot].Dirty = true;
                    _anyDirty = true;
                    continue;
                }

                _modifiers[kept++] = entry;
            }

            Array.Clear(_modifiers, kept, _modifierCount - kept);
            _modifierCount = kept;
        }

        /// <summary>Marks the attribute slot dirty (e.g. on an Enabled toggle).</summary>
        internal void MarkDirty(ushort attributeId)
        {
            _slots[SlotIndex(attributeId)].Dirty = true;
            _anyDirty = true;
        }

        /// <summary>Rebuilds all dirty slots in canonical order, following the clamp topology (registry EvaluationOrder).</summary>
        internal void RebuildDirty()
        {
            if (!_anyDirty) return;
            _anyDirty = false;

            var order = _layout.EvaluationSlots;
            for (var i = 0; i < order.Length; i++)
            {
                if (_slots[order[i]].Dirty) RebuildSlot(order[i]);
            }
        }

        private void RebuildSlot(int index)
        {
            ref var slot = ref _slots[index];
            slot.Dirty = false;
            if (slot.Aggregate >= 0)
            {
                ref var aggregate = ref _aggregates[slot.Aggregate];
                aggregate = default;
                // The slot's entries are contiguous and in canonical order — summation order is part of the
                // result, since BigNum addition truncates and is not associative.
                for (var i = FirstModifierOf(index); i < _modifierCount && _modifiers[i].Slot == index; i++)
                {
                    var entry = _modifiers[i];
                    if (!entry.Source.Enabled) continue;
                    var magnitude = entry.ScaleWithStack ? entry.Magnitude * entry.Source.Stack : entry.Magnitude;
                    switch (entry.Op)
                    {
                        case AttributeModifierOp.Add:
                            aggregate.SumAdd += magnitude;
                            break;
                        case AttributeModifierOp.Multiply:
                            aggregate.SumMulPct += magnitude;
                            break;
                        default:   // Override — entries are Id-ordered, so the last enabled entry wins
                            aggregate.HasOverride = true;
                            aggregate.OverrideValue = magnitude;
                            break;
                    }
                }
            }

            ReapplyFormula(index);
        }

        private int FirstModifierOf(int slotIndex)
        {
            var low = 0;
            var high = _modifierCount;
            while (low < high)
            {
                var mid = low + ((high - low) >> 1);
                if (_modifiers[mid].Slot < slotIndex) low = mid + 1;
                else high = mid;
            }

            return low;
        }

        /// <summary>Routes Current changes into a pipeline's log under the owning unit's id; null detaches.</summary>
        internal void AttachChangeLog(AttributeChangeLog? log, TargetId targetId)
        {
            _changeLog = log;
            _targetId = targetId;
        }

        private void EmitChange(ushort attributeId, BigNum oldCurrent, BigNum newCurrent) =>
            _changeLog?.Append(new AttributeChange(_targetId, attributeId, oldCurrent, newCurrent));
    }
}
