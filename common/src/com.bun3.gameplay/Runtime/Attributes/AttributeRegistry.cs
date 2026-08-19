#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>Clamp and policy definition for a single attribute.</summary>
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

        /// <summary>Lower bound, or null if none.</summary>
        public Operand? Min { get; }

        /// <summary>Upper bound, or null if none.</summary>
        public Operand? Max { get; }

        /// <summary>Base follow policy when the max bound increases.</summary>
        public MaxIncreasePolicy OnMaxIncrease { get; }

        /// <summary>Base handling policy when the max bound decreases.</summary>
        public MaxDecreasePolicy OnMaxDecrease { get; }
    }

    /// <summary>Immutable attribute definition registry, built once at startup.</summary>
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

        /// <summary>Number of registered attributes.</summary>
        public int Count => _definitions.Count;

        /// <summary>Returns whether the attribute id is registered.</summary>
        public bool Contains(ushort attributeId) => _definitions.ContainsKey(attributeId);

        internal AttributeDefinition GetDefinition(ushort attributeId) => _definitions[attributeId];

        /// <summary>Clamp-dependency topological order (ties broken by ascending id).</summary>
        public ReadOnlySpan<ushort> EvaluationOrder => _evaluationOrder;

        /// <summary>Attributes that reference this attribute as a clamp bound (ascending id).</summary>
        public ReadOnlySpan<ushort> GetClampDependents(ushort attributeId) =>
            _clampDependents.GetValueOrDefault(attributeId, Empty);
    }
}
