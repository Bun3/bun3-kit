#nullable enable
using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>
    /// 아키타입이 선언한 속성들의 밀집 슬롯 집합입니다. Base 쓰기는 항상 클램프를 통과하고
    /// Current 변경은 이벤트 버퍼에 적재됩니다.
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

        private readonly AttributeRegistry _registry;
        private readonly Slot[] _slots;                    // AttributeId 오름차순 canonical
        private readonly int[] _slotByAttributeId;         // 희소 → 밀집 (등록 최대 id + 1 크기, -1 = 없음)
        private AttributeChange[] _changes = new AttributeChange[8];
        private int _changeCount;

        /// <summary>아키타입이 선언한 속성 id들로 밀집 슬롯을 만듭니다. 선언 순서는 무관합니다.</summary>
        public AttributeSet(AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            var ids = attributeIds.ToArray();
            Array.Sort(ids);
            var maxId = 0;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!registry.Contains(ids[i]))
                    throw new ArgumentException($"미등록 속성 {ids[i]}입니다.", nameof(attributeIds));
                if (i > 0 && ids[i] == ids[i - 1])
                    throw new ArgumentException($"속성 {ids[i]}이(가) 중복 선언되었습니다.", nameof(attributeIds));
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

            // 클램프 경계 검증 — Min/Max의 속성 참조는 반드시 선언되어야 함
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                var definition = registry.GetDefinition(id);
                if (definition.Min.HasValue && definition.Min.Value.Kind == OperandKind.Attribute)
                {
                    var refId = definition.Min.Value.AttributeId;
                    if (!Has(refId))
                        throw new ArgumentException(
                            $"속성 {id}의 클램프가 참조하는 {refId}이(가) 아키타입 선언에 없습니다.",
                            nameof(attributeIds));
                }

                if (definition.Max.HasValue && definition.Max.Value.Kind == OperandKind.Attribute)
                {
                    var refId = definition.Max.Value.AttributeId;
                    if (!Has(refId))
                        throw new ArgumentException(
                            $"속성 {id}의 클램프가 참조하는 {refId}이(가) 아키타입 선언에 없습니다.",
                            nameof(attributeIds));
                }
            }
        }

        /// <summary>이 집합이 해당 속성을 선언했는지 확인합니다.</summary>
        public bool Has(ushort attributeId) =>
            attributeId < _slotByAttributeId.Length && _slotByAttributeId[attributeId] >= 0;

        private int SlotIndex(ushort attributeId)
        {
            if (!Has(attributeId))
                throw new ArgumentOutOfRangeException(nameof(attributeId), attributeId, "선언되지 않은 속성입니다.");
            return _slotByAttributeId[attributeId];
        }

        /// <summary>영구값 Base를 가져옵니다.</summary>
        public BigNum GetBase(ushort attributeId) => _slots[SlotIndex(attributeId)].Base;

        /// <summary>집계·클램프가 반영된 Current를 가져옵니다.</summary>
        public BigNum GetCurrent(ushort attributeId) => _slots[SlotIndex(attributeId)].Current;

        /// <summary>Base를 설정합니다. 항상 클램프를 통과하며 Current가 즉시 갱신됩니다.</summary>
        public void SetBase(ushort attributeId, BigNum value)
        {
            var index = SlotIndex(attributeId);
            _slots[index].Base = ClampToBounds(index, value);
            ReapplyFormula(index);
        }

        /// <summary>Base에 델타를 더합니다. 항상 클램프를 통과합니다.</summary>
        public void AddBase(ushort attributeId, BigNum delta)
        {
            var index = SlotIndex(attributeId);
            SetBase(attributeId, _slots[index].Base + delta);
        }

        private BigNum ResolveBound(Operand bound)
        {
            if (bound.Kind == OperandKind.Constant) return bound.Value;
            // 클램프 경계의 속성 참조는 생성자에서 검증됨 — 존재 보장
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

        // 공식 재적용 — Σ 캐시 불변 경로 (O(1)). Task 4에서 집계, Task 5에서 전파가 이어진다.
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

        // changedAttributeId의 Current 변화(old→new)를 클램프 경계로 참조하는 후손들에 즉시 전파합니다.
        // 후손 정의별 OnMaxIncrease/OnMaxDecrease 정책에 따라 Base를 동반 이동시킨 뒤 재적용합니다.
        // 의존 그래프는 레지스트리 빌드 시 검증된 DAG이므로 재귀 호출이 안전합니다.
        private void PropagateToDependents(ushort changedAttributeId, BigNum oldValue, BigNum newValue)
        {
            var dependents = _registry.GetClampDependents(changedAttributeId);
            for (var i = 0; i < dependents.Length; i++)
            {
                if (!Has(dependents[i])) continue;   // 이 아키타입에 선언되지 않은 후손이면 건너뜀
                var index = _slotByAttributeId[dependents[i]];
                var definition = _registry.GetDefinition(dependents[i]);
                var maxOperand = definition.Max.GetValueOrDefault();
                var referencesAsMax = definition.Max.HasValue
                    && maxOperand.Kind == OperandKind.Attribute
                    && maxOperand.AttributeId == changedAttributeId;

                if (referencesAsMax && newValue > oldValue
                    && definition.OnMaxIncrease == MaxIncreasePolicy.Follow)
                {
                    var delta = (newValue - oldValue) * maxOperand.Value;   // 계수 반영
                    _slots[index].Base = ClampToBounds(index, _slots[index].Base + delta);
                }

                if (referencesAsMax && newValue < oldValue
                    && definition.OnMaxDecrease == MaxDecreasePolicy.Follow)
                {
                    _slots[index].Base = ClampToBounds(index, _slots[index].Base);    // 경계로 잘라 기록
                }

                ReapplyFormula(index);   // Stay는 안전망만 — 재적용이 처리
            }
        }

        /// <summary>수정자를 부착합니다. (source.Id, rowIndex) 오름차순 삽입 정렬로 canonical 순서를 유지합니다.</summary>
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
            // (Id, RowIndex) 오름차순 삽입 정렬 — 목록이 항상 canonical
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

        /// <summary>해당 소스가 부착한 모든 수정자를 제거합니다.</summary>
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

        /// <summary>해당 속성 슬롯을 dirty로 표시합니다(예: Enabled 토글).</summary>
        internal void MarkDirty(ushort attributeId) => _slots[SlotIndex(attributeId)].Dirty = true;

        /// <summary>dirty 슬롯을 canonical 순서로 전체 재집계합니다. 호출 순서는 클램프 위상(레지스트리 EvaluationOrder)을 따릅니다.</summary>
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
                for (var i = 0; i < modifiers.Count; i++)   // 목록이 canonical 정렬 유지
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
                        default:   // Override — 목록이 Id 순이라 마지막 활성 항목이 최신
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

        /// <summary>아직 소비되지 않은 변경 이벤트입니다.</summary>
        public ReadOnlySpan<AttributeChange> PendingChanges => _changes.AsSpan(0, _changeCount);

        /// <summary>변경 이벤트 버퍼를 비웁니다.</summary>
        public void ClearChanges() => _changeCount = 0;
    }
}
