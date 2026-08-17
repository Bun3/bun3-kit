#nullable enable
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Seams
{
    /// <summary>크기(피해, 회복 등)를 계산하는 계약입니다.</summary>
    public interface IMagnitudeCalc
    {
        /// <summary>주어진 컨텍스트에서 크기를 계산합니다.</summary>
        /// <param name="ctx">계산에 사용할 컨텍스트입니다.</param>
        /// <returns>계산된 크기 값입니다.</returns>
        BigNum Calculate(in MagnitudeContext ctx);
    }

    /// <summary>효과 실행을 수행하는 계약입니다.</summary>
    public interface IExecutionCalc
    {
        /// <summary>주어진 컨텍스트에서 효과를 실행합니다.</summary>
        /// <param name="ctx">실행에 사용할 컨텍스트입니다.</param>
        void Execute(ref ExecutionContext ctx);
    }

    /// <summary>대상을 선택하는 계약입니다.</summary>
    public interface ITargetSelector
    {
        /// <summary>주어진 컨텍스트에서 대상을 선택합니다.</summary>
        /// <param name="ctx">선택에 사용할 컨텍스트입니다.</param>
        /// <param name="results">선택된 대상 식별자들을 저장할 배열입니다.</param>
        /// <returns>선택된 대상의 개수입니다.</returns>
        int Select(in SelectorContext ctx, System.Span<Effects.TargetId> results);
    }

    /// <summary>크기 계산에 필요한 컨텍스트입니다.</summary>
    public readonly ref struct MagnitudeContext
    {
    }

    /// <summary>효과 실행에 필요한 컨텍스트입니다.</summary>
    public ref struct ExecutionContext
    {
    }

    /// <summary>대상 선택에 필요한 컨텍스트입니다.</summary>
    public readonly ref struct SelectorContext
    {
    }
}
