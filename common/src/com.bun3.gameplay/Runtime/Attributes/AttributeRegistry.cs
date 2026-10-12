#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>Resolved clamp bounds and boundary policies for a single attribute.</summary>
    public readonly struct CompiledAttribute
    {
        internal CompiledAttribute(
            Operand? min, Operand? max,
            MaxIncreasePolicy onMaxIncrease, MaxDecreasePolicy onMaxDecrease)
        {
            Min = min;
            Max = max;
            OnMaxIncrease = onMaxIncrease;
            OnMaxDecrease = onMaxDecrease;
        }

        /// <summary>Lower bound, or null if none.</summary>
        public Operand? Min { get; }

        /// <summary>Upper bound, or null if none.</summary>
        public Operand? Max { get; }

        /// <summary>Base follow policy when the max bound increases.</summary>
        public MaxIncreasePolicy OnMaxIncrease { get; }

        /// <summary>Base handling policy when the max bound decreases.</summary>
        public MaxDecreasePolicy OnMaxDecrease { get; }
    }

    /// <summary>Immutable registry of compiled attribute shapes, built once at startup.</summary>
    public sealed class AttributeRegistry
    {
        private readonly Dictionary<ushort, CompiledAttribute> _attributes;
        private readonly Dictionary<ushort, string> _names;
        private readonly Dictionary<string, ushort> _nameToId;
        private readonly ushort[] _evaluationOrder;
        private readonly Dictionary<ushort, ushort[]> _clampDependents;
        private static readonly ushort[] Empty = Array.Empty<ushort>();

        internal AttributeRegistry(
            Dictionary<ushort, CompiledAttribute> attributes,
            Dictionary<ushort, string> names,
            Dictionary<string, ushort> nameToId,
            ushort[] evaluationOrder,
            Dictionary<ushort, ushort[]> clampDependents,
            uint signature)
        {
            _attributes = attributes;
            _names = names;
            _nameToId = nameToId;
            _evaluationOrder = evaluationOrder;
            _clampDependents = clampDependents;
            Signature = signature;
        }

        /// <summary>
        /// Stable hash of the registered attribute shape — ids, clamp bounds, and boundary policies in
        /// evaluation order (names are not part of it). A compiled catalog artifact is only valid against the same shape, because
        /// its magnitudes and clamps were resolved against it.
        /// </summary>
        public uint Signature { get; }

        /// <summary>Number of registered attributes.</summary>
        public int Count => _attributes.Count;

        /// <summary>Returns whether the attribute id is registered.</summary>
        public bool Contains(ushort attributeId) => _attributes.ContainsKey(attributeId);

        /// <summary>Registered name of an attribute.</summary>
        /// <param name="attributeId">Registered attribute id.</param>
        /// <returns>The attribute's name.</returns>
        public string GetName(ushort attributeId) => _names[attributeId];

        /// <summary>Finds an attribute id by its registered name (ordinal comparison).</summary>
        /// <param name="name">Attribute name.</param>
        /// <param name="attributeId">Id when found.</param>
        /// <returns>True when an attribute has that name.</returns>
        public bool TryGetId(string name, out ushort attributeId) => _nameToId.TryGetValue(name, out attributeId);

        internal IReadOnlyDictionary<string, ushort> NameToId => _nameToId;

        // Error-message label: "'Hp' (1)" for a registered attribute, the bare id otherwise.
        internal string Describe(ushort attributeId) =>
            _names.TryGetValue(attributeId, out var name) ? $"'{name}' ({attributeId})" : attributeId.ToString();

        internal CompiledAttribute GetCompiledAttribute(ushort attributeId) => _attributes[attributeId];

        /// <summary>Clamp-dependency topological order (ties broken by ascending id).</summary>
        public ReadOnlySpan<ushort> EvaluationOrder => _evaluationOrder;

        /// <summary>Attributes that reference this attribute as a clamp bound (ascending id).</summary>
        public ReadOnlySpan<ushort> GetClampDependents(ushort attributeId) =>
            _clampDependents.GetValueOrDefault(attributeId, Empty);
    }
}
