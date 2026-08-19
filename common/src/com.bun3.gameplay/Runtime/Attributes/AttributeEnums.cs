#nullable enable
namespace Bun3.Gameplay.Attributes
{
    /// <summary>Operand shape discriminator. Expression nodes may be added in the future.</summary>
    public enum OperandKind : byte
    {
        /// <summary>Constant BigNum.</summary>
        Constant = 0,
        /// <summary>Target attribute Current × constant coefficient.</summary>
        Attribute = 1,
        /// <summary>Source (caster) attribute Current × constant coefficient. Evaluates to 0 when the source is unresolved.</summary>
        SourceAttribute = 2,
    }

    /// <summary>Modifier operation kind.</summary>
    public enum AttributeModifierOp : byte
    {
        /// <summary>Additive — summed into ΣAdd.</summary>
        Add = 0,
        /// <summary>Additive-percent multiply — percentages sum into ΣMulPct, then multiply once.</summary>
        Multiply = 1,
        /// <summary>Highest-priority override — with multiple, the latest instance wins.</summary>
        Override = 2,
    }

    /// <summary>Whether Base follows when the max bound increases.</summary>
    public enum MaxIncreasePolicy : byte
    {
        /// <summary>Keep Base — only the empty headroom grows (default).</summary>
        Stay = 0,
        /// <summary>Base += Δ — the missing amount is preserved.</summary>
        Follow = 1,
    }

    /// <summary>Base handling when the max bound decreases.</summary>
    public enum MaxDecreasePolicy : byte
    {
        /// <summary>Truncate Base to the bound — the excess is permanently lost (default).</summary>
        Follow = 0,
        /// <summary>Keep Base — only Current is pressed down by the safety net and restores when the bound recovers.</summary>
        Stay = 1,
    }

    /// <summary>Condition comparison operator.</summary>
    public enum ComparisonOp : byte
    {
        /// <summary>Equal.</summary>
        Equal = 0,
        /// <summary>Not equal.</summary>
        NotEqual = 1,
        /// <summary>Less than.</summary>
        Less = 2,
        /// <summary>Less than or equal.</summary>
        LessOrEqual = 3,
        /// <summary>Greater than.</summary>
        Greater = 4,
        /// <summary>Greater than or equal.</summary>
        GreaterOrEqual = 5,
    }
}
