#nullable enable
namespace Bun3.Gameplay.Attributes
{
    /// <summary>수정자를 공급하는 소스(EffectInstance 등)가 집계에 노출하는 최소 상태입니다.</summary>
    public interface IAttributeModifierSource
    {
        /// <summary>World가 발급한 단조 증가 id — canonical 집계 순서의 근거입니다.</summary>
        ulong Id { get; }

        /// <summary>현재 스택 수입니다.</summary>
        int Stack { get; }

        /// <summary>Ongoing 조건 토글 상태입니다. false면 집계에서 건너뜁니다.</summary>
        bool Enabled { get; }
    }
}
