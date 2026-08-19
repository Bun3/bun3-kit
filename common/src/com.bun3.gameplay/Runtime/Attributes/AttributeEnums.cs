#nullable enable
namespace Bun3.Gameplay.Attributes
{
    /// <summary>피연산자 형태 판별자입니다. 미래의 Expression 노드가 추가될 수 있습니다.</summary>
    public enum OperandKind : byte
    {
        /// <summary>상수 BigNum입니다.</summary>
        Constant = 0,
        /// <summary>대상(target) 속성 Current × 상수 계수입니다.</summary>
        Attribute = 1,
        /// <summary>시전자(source) 속성 Current × 상수 계수입니다. 소스 미해석 시 0으로 평가됩니다.</summary>
        SourceAttribute = 2,
    }

    /// <summary>수정자 연산 종류입니다.</summary>
    public enum AttributeModifierOp : byte
    {
        /// <summary>가산 — ΣAdd에 합산됩니다.</summary>
        Add = 0,
        /// <summary>합산식 곱 — 퍼센트가 ΣMulPct에 합산된 뒤 한 번 곱해집니다.</summary>
        Multiply = 1,
        /// <summary>최우선 덮어쓰기 — 복수면 가장 나중 인스턴스가 이깁니다.</summary>
        Override = 2,
    }

    /// <summary>max 경계가 상승할 때 Base의 동반 여부입니다.</summary>
    public enum MaxIncreasePolicy : byte
    {
        /// <summary>Base 유지 — 빈 여유만 커집니다(기본).</summary>
        Stay = 0,
        /// <summary>Base += Δ — 잃은 량이 보존됩니다.</summary>
        Follow = 1,
    }

    /// <summary>max 경계가 하락할 때 Base의 처리입니다.</summary>
    public enum MaxDecreasePolicy : byte
    {
        /// <summary>Base를 경계로 잘라 기록 — 초과분 영구 소실(기본).</summary>
        Follow = 0,
        /// <summary>Base 보존 — Current만 안전망에 눌리고 경계 복귀 시 복원됩니다.</summary>
        Stay = 1,
    }

    /// <summary>조건 비교 연산자입니다.</summary>
    public enum ComparisonOp : byte
    {
        /// <summary>같음.</summary>
        Equal = 0,
        /// <summary>다름.</summary>
        NotEqual = 1,
        /// <summary>미만.</summary>
        Less = 2,
        /// <summary>이하.</summary>
        LessOrEqual = 3,
        /// <summary>초과.</summary>
        Greater = 4,
        /// <summary>이상.</summary>
        GreaterOrEqual = 5,
    }
}
