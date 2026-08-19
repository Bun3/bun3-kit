#nullable enable
using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>
    /// 수정자 크기·조건 양변·클램프 경계가 공유하는 피연산자 — 상수 또는 속성 Current × 계수입니다.
    /// </summary>
    public readonly struct Operand : IEquatable<Operand>
    {
        /// <summary>형태 판별자입니다.</summary>
        public OperandKind Kind { get; }

        /// <summary>상수 값 또는 속성 참조의 계수입니다.</summary>
        public BigNum Value { get; }

        /// <summary>참조하는 속성 id이며 상수면 0입니다.</summary>
        public ushort AttributeId { get; }

        private Operand(OperandKind kind, BigNum value, ushort attributeId)
        {
            Kind = kind;
            Value = value;
            AttributeId = attributeId;
        }

        /// <summary>상수 피연산자를 만듭니다.</summary>
        public static Operand Constant(BigNum value) => new Operand(OperandKind.Constant, value, 0);

        /// <summary>계수 1의 대상 속성 참조 피연산자를 만듭니다.</summary>
        public static Operand Attribute(ushort attributeId) => Attribute(attributeId, BigNum.One);

        /// <summary>대상 속성 Current × 계수 피연산자를 만듭니다.</summary>
        public static Operand Attribute(ushort attributeId, BigNum coefficient) =>
            new Operand(OperandKind.Attribute, coefficient, attributeId);

        /// <summary>계수 1의 시전자 속성 참조 피연산자를 만듭니다.</summary>
        public static Operand SourceAttribute(ushort attributeId) => SourceAttribute(attributeId, BigNum.One);

        /// <summary>시전자 속성 Current × 계수 피연산자를 만듭니다.</summary>
        public static Operand SourceAttribute(ushort attributeId, BigNum coefficient) =>
            new Operand(OperandKind.SourceAttribute, coefficient, attributeId);

        /// <summary>값 동등 비교입니다.</summary>
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
