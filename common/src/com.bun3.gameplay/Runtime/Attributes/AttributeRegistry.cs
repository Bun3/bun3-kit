#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>속성 하나의 클램프·정책 정의입니다.</summary>
    public readonly struct AttributeDefinition
    {
        internal AttributeDefinition(
            Operand? min, Operand? max,
            MaxIncreasePolicy onMaxIncrease, MaxDecreasePolicy onMaxDecrease)
        {
            Min = min;
            Max = max;
            OnMaxIncrease = onMaxIncrease;
            OnMaxDecrease = onMaxDecrease;
        }

        /// <summary>하한 경계이며 없으면 null입니다.</summary>
        public Operand? Min { get; }

        /// <summary>상한 경계이며 없으면 null입니다.</summary>
        public Operand? Max { get; }

        /// <summary>max 경계 상승 시 Base 동반 정책입니다.</summary>
        public MaxIncreasePolicy OnMaxIncrease { get; }

        /// <summary>max 경계 하락 시 Base 처리 정책입니다.</summary>
        public MaxDecreasePolicy OnMaxDecrease { get; }
    }

    /// <summary>기동 시 한 번 만들어져 변하지 않는 속성 정의 레지스트리입니다.</summary>
    public sealed class AttributeRegistry
    {
        private readonly Dictionary<ushort, AttributeDefinition> _definitions;
        private readonly ushort[] _evaluationOrder;
        private readonly Dictionary<ushort, ushort[]> _clampDependents;
        private static readonly ushort[] Empty = Array.Empty<ushort>();

        internal AttributeRegistry(
            Dictionary<ushort, AttributeDefinition> definitions,
            ushort[] evaluationOrder,
            Dictionary<ushort, ushort[]> clampDependents)
        {
            _definitions = definitions;
            _evaluationOrder = evaluationOrder;
            _clampDependents = clampDependents;
        }

        /// <summary>등록된 속성 수입니다.</summary>
        public int Count => _definitions.Count;

        /// <summary>속성 id가 등록되어 있는지 확인합니다.</summary>
        public bool Contains(ushort attributeId) => _definitions.ContainsKey(attributeId);

        internal AttributeDefinition GetDefinition(ushort attributeId) => _definitions[attributeId];

        /// <summary>클램프 의존 위상 순서(동순위 id 오름차순)입니다.</summary>
        public ReadOnlySpan<ushort> EvaluationOrder => _evaluationOrder;

        /// <summary>이 속성을 클램프 경계로 참조하는 속성들(id 오름차순)입니다.</summary>
        public ReadOnlySpan<ushort> GetClampDependents(ushort attributeId) =>
            _clampDependents.GetValueOrDefault(attributeId, Empty);
    }
}
