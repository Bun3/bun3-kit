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
            // 클램프 경계의 속성 참조는 등록 시 검증됨 — 미선언이면 경계 없음으로 취급
            return Has(bound.AttributeId)
                ? _slots[_slotByAttributeId[bound.AttributeId]].Current * bound.Value
                : bound.Value * 0;
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
            slot.Current = clamped;
            EmitChange(slot.AttributeId, old, clamped);
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
