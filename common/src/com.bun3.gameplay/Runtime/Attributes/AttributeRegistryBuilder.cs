#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>속성 정의를 수집한 뒤 Build에서 일괄 검증·확정하는 빌더입니다. 등록 순서는 결과에 영향을 주지 않습니다.</summary>
    public sealed class AttributeRegistryBuilder
    {
        private readonly Dictionary<ushort, AttributeDefinition> _definitions = new Dictionary<ushort, AttributeDefinition>();
        private bool _built;

        /// <summary>속성 정의를 등록합니다. 전방 참조를 허용하며 검증은 Build에서 일괄 수행합니다.</summary>
        public void Register(
            ushort attributeId,
            Operand? min = null,
            Operand? max = null,
            MaxIncreasePolicy onMaxIncrease = MaxIncreasePolicy.Stay,
            MaxDecreasePolicy onMaxDecrease = MaxDecreasePolicy.Follow)
        {
            if (_built) throw new InvalidOperationException("Build 후에는 등록할 수 없습니다.");
            if (_definitions.ContainsKey(attributeId))
                throw new InvalidOperationException($"속성 {attributeId}이(가) 중복 등록되었습니다.");

            _definitions.Add(attributeId, new AttributeDefinition(min, max, onMaxIncrease, onMaxDecrease));
        }

        /// <summary>참조·순환·정책 정합성을 검증하고 위상 순서·후손 목록을 계산해 불변 레지스트리를 만듭니다.</summary>
        public AttributeRegistry Build()
        {
            _built = true;
            var dependencyLists = new Dictionary<ushort, List<ushort>>();
            foreach (var pair in _definitions)
            {
                ValidateBound(pair.Key, pair.Value.Min, dependencyLists);
                ValidateBound(pair.Key, pair.Value.Max, dependencyLists);
                if (pair.Value.OnMaxIncrease != MaxIncreasePolicy.Stay && !IsAttributeBound(pair.Value.Max))
                    throw new InvalidOperationException($"속성 {pair.Key}: MaxIncreasePolicy가 기본값이 아니면 max가 속성 참조여야 합니다.");
                if (pair.Value.OnMaxDecrease != MaxDecreasePolicy.Follow && !IsAttributeBound(pair.Value.Max))
                    throw new InvalidOperationException($"속성 {pair.Key}: MaxDecreasePolicy가 기본값이 아니면 max가 속성 참조여야 합니다.");
            }

            var order = TopologicalOrder(dependencyLists);
            var dependents = new Dictionary<ushort, ushort[]>();
            foreach (var pair in dependencyLists)
            {
                pair.Value.Sort();
                dependents.Add(pair.Key, pair.Value.ToArray());
            }

            return new AttributeRegistry(
                new Dictionary<ushort, AttributeDefinition>(_definitions), order, dependents);
        }

        private static bool IsAttributeBound(Operand? bound) =>
            bound.HasValue && bound.Value.Kind == OperandKind.Attribute;

        private void ValidateBound(ushort owner, Operand? bound, Dictionary<ushort, List<ushort>> dependencyLists)
        {
            if (!bound.HasValue) return;

            // Reject SourceAttribute operands in clamp bounds
            if (bound.Value.Kind == OperandKind.SourceAttribute)
                throw new InvalidOperationException($"속성 {owner}의 클램프 경계는 SourceAttribute를 참조할 수 없습니다.");

            if (!IsAttributeBound(bound)) return;

            var referenced = bound!.Value.AttributeId;
            if (!_definitions.ContainsKey(referenced))
                throw new InvalidOperationException($"속성 {owner}의 클램프가 미등록 속성 {referenced}을(를) 참조합니다.");

            if (!dependencyLists.TryGetValue(referenced, out var list))
            {
                list = new List<ushort>();
                dependencyLists.Add(referenced, list);
            }

            if (!list.Contains(owner)) list.Add(owner);
        }

        // Kahn — 레벨별로 처리해 동순위 원소를 id 오름차순으로 canonical하게 만든다.
        private ushort[] TopologicalOrder(Dictionary<ushort, List<ushort>> dependents)
        {
            var inDegree = new Dictionary<ushort, int>();
            foreach (var id in _definitions.Keys) inDegree[id] = 0;
            foreach (var pair in dependents)
                foreach (var dependent in pair.Value) inDegree[dependent]++;

            var result = new List<ushort>();
            var currentLevel = new List<ushort>();

            // 초기 레벨: in-degree가 0인 노드
            foreach (var pair in inDegree)
                if (pair.Value == 0) currentLevel.Add(pair.Key);

            while (currentLevel.Count > 0)
            {
                currentLevel.Sort();
                var nextLevel = new List<ushort>();

                foreach (var current in currentLevel)
                {
                    result.Add(current);
                    if (dependents.TryGetValue(current, out var children))
                    {
                        foreach (var child in children)
                        {
                            if (--inDegree[child] == 0) nextLevel.Add(child);
                        }
                    }
                }

                currentLevel = nextLevel;
            }

            if (result.Count != _definitions.Count)
                throw new InvalidOperationException("클램프 참조에 순환이 있습니다.");
            return result.ToArray();
        }
    }
}
