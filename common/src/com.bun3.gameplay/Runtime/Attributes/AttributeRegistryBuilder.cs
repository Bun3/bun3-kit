#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>Collects attribute definitions and validates them all at Build. Registration order does not affect the result.</summary>
    public sealed class AttributeRegistryBuilder
    {
        private readonly Dictionary<ushort, AttributeDefinition> _definitions = new Dictionary<ushort, AttributeDefinition>();
        private bool _built;

        /// <summary>Registers an attribute definition. Forward references are allowed; validation happens at Build.</summary>
        public void Register(
            ushort attributeId,
            Operand? min = null,
            Operand? max = null,
            MaxIncreasePolicy onMaxIncrease = MaxIncreasePolicy.Stay,
            MaxDecreasePolicy onMaxDecrease = MaxDecreasePolicy.Follow)
        {
            if (_built) throw new InvalidOperationException("Cannot register after Build.");
            if (_definitions.ContainsKey(attributeId))
                throw new InvalidOperationException($"Attribute {attributeId} is registered more than once.");

            _definitions.Add(attributeId, new AttributeDefinition(min, max, onMaxIncrease, onMaxDecrease));
        }

        /// <summary>Validates references, cycles, and policy consistency, computes the topological order and dependent lists, and builds the immutable registry.</summary>
        public AttributeRegistry Build()
        {
            _built = true;
            var dependencyLists = new Dictionary<ushort, List<ushort>>();
            foreach (var pair in _definitions)
            {
                ValidateBound(pair.Key, pair.Value.Min, dependencyLists);
                ValidateBound(pair.Key, pair.Value.Max, dependencyLists);
                if (pair.Value.OnMaxIncrease != MaxIncreasePolicy.Stay && !IsAttributeBound(pair.Value.Max))
                    throw new InvalidOperationException($"Attribute {pair.Key}: a non-default MaxIncreasePolicy requires max to be an attribute reference.");
                if (pair.Value.OnMaxDecrease != MaxDecreasePolicy.Follow && !IsAttributeBound(pair.Value.Max))
                    throw new InvalidOperationException($"Attribute {pair.Key}: a non-default MaxDecreasePolicy requires max to be an attribute reference.");
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

            if (bound.Value.Kind == OperandKind.SourceAttribute)
                throw new InvalidOperationException($"Clamp bounds of attribute {owner} cannot reference a SourceAttribute.");

            if (!IsAttributeBound(bound)) return;

            var referenced = bound!.Value.AttributeId;
            if (!_definitions.ContainsKey(referenced))
                throw new InvalidOperationException($"Clamp of attribute {owner} references unregistered attribute {referenced}.");

            if (!dependencyLists.TryGetValue(referenced, out var list))
            {
                list = new List<ushort>();
                dependencyLists.Add(referenced, list);
            }

            if (!list.Contains(owner)) list.Add(owner);
        }

        /// <summary>Evaluation order via level-wise Kahn's algorithm; within a level, ascending AttributeId.</summary>
        private ushort[] TopologicalOrder(Dictionary<ushort, List<ushort>> dependents)
        {
            var inDegree = new Dictionary<ushort, int>();
            foreach (var id in _definitions.Keys) inDegree[id] = 0;
            foreach (var pair in dependents)
                foreach (var dependent in pair.Value) inDegree[dependent]++;

            var result = new List<ushort>();
            var currentLevel = new List<ushort>();

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
                throw new InvalidOperationException("Clamp references contain a cycle.");
            return result.ToArray();
        }
    }
}
