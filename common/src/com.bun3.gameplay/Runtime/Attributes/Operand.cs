#nullable enable
using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>
    /// Operand shared by modifier magnitudes, condition sides, and clamp bounds — a constant, or attribute Current × coefficient.
    /// </summary>
    public readonly struct Operand : IEquatable<Operand>
    {
        /// <summary>Shape discriminator.</summary>
        public OperandKind Kind { get; }

        /// <summary>Constant value, or the coefficient of an attribute reference.</summary>
        public BigNum Value { get; }

        /// <summary>Referenced attribute id; 0 for constants.</summary>
        public ushort AttributeId { get; }

        private Operand(OperandKind kind, BigNum value, ushort attributeId)
        {
            Kind = kind;
            Value = value;
            AttributeId = attributeId;
        }

        /// <summary>Creates a constant operand.</summary>
        public static Operand Constant(BigNum value) => new Operand(OperandKind.Constant, value, 0);

        /// <summary>Creates a target attribute reference operand with coefficient 1.</summary>
        public static Operand Attribute(ushort attributeId) => Attribute(attributeId, BigNum.One);

        /// <summary>Creates a target attribute Current × coefficient operand.</summary>
        public static Operand Attribute(ushort attributeId, BigNum coefficient) =>
            new Operand(OperandKind.Attribute, coefficient, attributeId);

        /// <summary>Creates a source attribute reference operand with coefficient 1.</summary>
        public static Operand SourceAttribute(ushort attributeId) => SourceAttribute(attributeId, BigNum.One);

        /// <summary>Creates a source attribute Current × coefficient operand.</summary>
        public static Operand SourceAttribute(ushort attributeId, BigNum coefficient) =>
            new Operand(OperandKind.SourceAttribute, coefficient, attributeId);

        /// <summary>Value equality comparison.</summary>
        public bool Equals(Operand other) =>
            Kind == other.Kind && Value.Equals(other.Value) && AttributeId == other.AttributeId;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Operand other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                hash = (hash * 397) ^ Value.GetHashCode();
                hash = (hash * 397) ^ AttributeId;
                return hash;
            }
        }
    }
}
