#nullable enable
using System.Collections.Generic;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// <see cref="TargetId"/>로 <see cref="EffectTarget"/>을 찾는 계약입니다.
    /// <see cref="TargetIds"/>는 항상 오름차순을 유지해야 합니다.
    /// </summary>
    public interface IEffectTargetResolver
    {
        /// <summary>대상 식별자로 <see cref="EffectTarget"/>을 찾습니다.</summary>
        /// <param name="id">찾을 대상 식별자입니다.</param>
        /// <param name="target">찾은 대상입니다.</param>
        /// <returns>대상을 찾았으면 true입니다.</returns>
        bool TryResolve(TargetId id, out EffectTarget? target);

        /// <summary>이 리졸버가 아는 모든 대상 식별자들입니다. 오름차순 유지가 계약입니다.</summary>
        IReadOnlyList<TargetId> TargetIds { get; }
    }
}
