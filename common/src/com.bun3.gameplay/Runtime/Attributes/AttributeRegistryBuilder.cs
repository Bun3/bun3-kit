#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>Collects attribute registrations and validates them all at Build. Registration order does not affect the result.</summary>
    public sealed class AttributeRegistryBuilder
    {
        private readonly Dictionary<ushort, CompiledAttribute> _attributes = new Dictionary<ushort, CompiledAttribute>();
        private readonly Dictionary<ushort, string> _names = new Dictionary<ushort, string>();
        private bool _built;

        /// <summary>Registers an attribute's name, clamp bounds and boundary policies. Forward references are
        /// allowed; validation happens at Build.</summary>
        /// <param name="attributeId">Attribute id.</param>
        /// <param name="name">Name authoring data refers to the attribute by; unique within the registry.</param>
        /// <param name="min">Lower clamp bound, or null.</param>
        /// <param name="max">Upper clamp bound, or null.</param>
        /// <param name="onMaxIncrease">How Base follows when the max bound increases.</param>
        /// <param name="onMaxDecrease">How Base follows when the max bound decreases.</param>
        /// <exception cref="ArgumentException">Thrown when the name is null or empty.</exception>
        public void Register(
            ushort attributeId,
            string name,
            Operand? min = null,
            Operand? max = null,
            MaxIncreasePolicy onMaxIncrease = MaxIncreasePolicy.Stay,
            MaxDecreasePolicy onMaxDecrease = MaxDecreasePolicy.Follow)
        {
            if (_built) throw new InvalidOperationException("Cannot register after Build.");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Attribute name must not be empty.", nameof(name));
            if (_attributes.ContainsKey(attributeId))
                throw new InvalidOperationException($"Attribute {attributeId} is registered more than once.");

            _attributes.Add(attributeId, new CompiledAttribute(min, max, onMaxIncrease, onMaxDecrease));
            _names.Add(attributeId, name);
        }

        /// <summary>Validates references, cycles, and policy consistency, computes the topological order and dependent lists, and builds the immutable registry.</summary>
        public AttributeRegistry Build()
        {
            _built = true;
            var dependencyLists = new Dictionary<ushort, List<ushort>>();
            foreach (var pair in _attributes)
            {
                ValidateBound(pair.Key, pair.Value.Min, dependencyLists);
                ValidateBound(pair.Key, pair.Value.Max, dependencyLists);
                if (pair.Value.OnMaxIncrease != MaxIncreasePolicy.Stay && !IsAttributeBound(pair.Value.Max))
                    throw new InvalidOperationException($"Attribute {Describe(pair.Key)}: a non-default MaxIncreasePolicy requires max to be an attribute reference.");
                if (pair.Value.OnMaxDecrease != MaxDecreasePolicy.Follow && !IsAttributeBound(pair.Value.Max))
                    throw new InvalidOperationException($"Attribute {Describe(pair.Key)}: a non-default MaxDecreasePolicy requires max to be an attribute reference.");
            }

            var nameToId = new Dictionary<string, ushort>(StringComparer.Ordinal);
            foreach (var pair in _names)
            {
                if (nameToId.TryGetValue(pair.Value, out var other))
                {
                    var (first, second) = other < pair.Key ? (other, pair.Key) : (pair.Key, other);
                    throw new InvalidOperationException(
                        $"Attribute name '{pair.Value}' is registered for both {first} and {second}.");
                }

                nameToId.Add(pair.Value, pair.Key);
            }

            var order = TopologicalOrder(dependencyLists);
            var dependents = new Dictionary<ushort, ushort[]>();
            foreach (var pair in dependencyLists)
            {
                pair.Value.Sort();
                dependents.Add(pair.Key, pair.Value.ToArray());
            }

            return new AttributeRegistry(
                new Dictionary<ushort, CompiledAttribute>(_attributes), new Dictionary<ushort, string>(_names), nameToId,
                order, dependents, ComputeSignature(_attributes, order));
        }

        // FNV-1a over the shape in evaluation order. Evaluation order is itself derived from the shape,
        // so the hash is independent of registration order.
        private static uint ComputeSignature(
            Dictionary<ushort, CompiledAttribute> attributes, ushort[] order)
        {
            var hash = 2166136261u;
            for (var i = 0; i < order.Length; i++)
            {
                var id = order[i];
                var attribute = attributes[id];
                FeedUInt16(ref hash, id);
                FeedBound(ref hash, attribute.Min);
                FeedBound(ref hash, attribute.Max);
                FeedByte(ref hash, (byte)attribute.OnMaxIncrease);
                FeedByte(ref hash, (byte)attribute.OnMaxDecrease);
            }

            return hash;
        }

        private static void FeedBound(ref uint hash, Operand? bound)
        {
            FeedByte(ref hash, bound.HasValue ? (byte)1 : (byte)0);
            if (!bound.HasValue) return;
            FeedByte(ref hash, (byte)bound.Value.Kind);
            FeedUInt16(ref hash, bound.Value.AttributeId);
            FeedInt64(ref hash, bound.Value.Value.Mantissa);
            FeedInt64(ref hash, bound.Value.Value.Exponent);
        }

        private static void FeedUInt16(ref uint hash, ushort value)
        {
            FeedByte(ref hash, (byte)value);
            FeedByte(ref hash, (byte)(value >> 8));
        }

        private static void FeedInt64(ref uint hash, long value)
        {
            for (var shift = 0; shift < 64; shift += 8) FeedByte(ref hash, (byte)(value >> shift));
        }

        private static void FeedByte(ref uint hash, byte value)
        {
            unchecked
            {
                hash = (hash ^ value) * 16777619u;
            }
        }

        private string Describe(ushort attributeId) =>
            _names.TryGetValue(attributeId, out var name) ? $"'{name}' ({attributeId})" : attributeId.ToString();

        private static bool IsAttributeBound(Operand? bound) =>
            bound.HasValue && bound.Value.Kind == OperandKind.Attribute;

        private void ValidateBound(ushort owner, Operand? bound, Dictionary<ushort, List<ushort>> dependencyLists)
        {
            if (!bound.HasValue) return;

            if (bound.Value.Kind == OperandKind.SourceAttribute)
                throw new InvalidOperationException($"Clamp bounds of attribute {Describe(owner)} cannot reference a SourceAttribute.");

            if (bound.Value.Kind == OperandKind.EventMagnitude)
                throw new InvalidOperationException($"Clamp bounds of attribute {Describe(owner)} cannot reference EventMagnitude.");

            if (!IsAttributeBound(bound)) return;

            var referenced = bound!.Value.AttributeId;
            if (!_attributes.ContainsKey(referenced))
                throw new InvalidOperationException($"Clamp of attribute {Describe(owner)} references unregistered attribute {referenced}.");

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
            foreach (var id in _attributes.Keys) inDegree[id] = 0;
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

            if (result.Count != _attributes.Count)
                throw new InvalidOperationException("Clamp references contain a cycle.");
            return result.ToArray();
        }
    }
}
