#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Seams
{
    /// <summary>등록된 시섬(Seam) 계약을 관리하는 레지스트리입니다.</summary>
    public sealed class SeamRegistry
    {
        private readonly IReadOnlyDictionary<ushort, IMagnitudeCalc> _magnitudeCalcs;
        private readonly IReadOnlyDictionary<ushort, IExecutionCalc> _executionCalcs;
        private readonly IReadOnlyDictionary<ushort, ITargetSelector> _targetSelectors;

        internal SeamRegistry(
            IReadOnlyDictionary<ushort, IMagnitudeCalc> magnitudeCalcs,
            IReadOnlyDictionary<ushort, IExecutionCalc> executionCalcs,
            IReadOnlyDictionary<ushort, ITargetSelector> targetSelectors)
        {
            _magnitudeCalcs = magnitudeCalcs;
            _executionCalcs = executionCalcs;
            _targetSelectors = targetSelectors;
        }

        /// <summary>주어진 태그에 등록된 크기 계산 계약을 반환합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>등록된 계약입니다.</returns>
        /// <exception cref="KeyNotFoundException">태그가 등록되지 않았을 때 발생합니다.</exception>
        internal IMagnitudeCalc GetMagnitudeCalc(GameplayTag tag)
        {
            if (_magnitudeCalcs.TryGetValue(tag.Index, out var calc))
                return calc;
            throw new KeyNotFoundException($"태그 {tag.Index}에 등록된 크기 계산이 없습니다.");
        }

        /// <summary>주어진 태그에 등록된 효과 실행 계약을 반환합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>등록된 계약입니다.</returns>
        /// <exception cref="KeyNotFoundException">태그가 등록되지 않았을 때 발생합니다.</exception>
        internal IExecutionCalc GetExecutionCalc(GameplayTag tag)
        {
            if (_executionCalcs.TryGetValue(tag.Index, out var exec))
                return exec;
            throw new KeyNotFoundException($"태그 {tag.Index}에 등록된 효과 실행이 없습니다.");
        }

        /// <summary>주어진 태그에 등록된 대상 선택 계약을 반환합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>등록된 계약입니다.</returns>
        /// <exception cref="KeyNotFoundException">태그가 등록되지 않았을 때 발생합니다.</exception>
        internal ITargetSelector GetTargetSelector(GameplayTag tag)
        {
            if (_targetSelectors.TryGetValue(tag.Index, out var selector))
                return selector;
            throw new KeyNotFoundException($"태그 {tag.Index}에 등록된 대상 선택이 없습니다.");
        }

        /// <summary>주어진 태그에 등록된 크기 계산 계약을 시도합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <param name="calc">찾은 계약입니다.</param>
        /// <returns>태그가 등록되었으면 true입니다.</returns>
        internal bool TryGetMagnitudeCalc(GameplayTag tag, out IMagnitudeCalc? calc)
        {
            return _magnitudeCalcs.TryGetValue(tag.Index, out calc);
        }

        /// <summary>주어진 태그에 등록된 효과 실행 계약을 시도합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <param name="exec">찾은 계약입니다.</param>
        /// <returns>태그가 등록되었으면 true입니다.</returns>
        internal bool TryGetExecutionCalc(GameplayTag tag, out IExecutionCalc? exec)
        {
            return _executionCalcs.TryGetValue(tag.Index, out exec);
        }

        /// <summary>주어진 태그에 등록된 대상 선택 계약을 시도합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <param name="selector">찾은 계약입니다.</param>
        /// <returns>태그가 등록되었으면 true입니다.</returns>
        internal bool TryGetTargetSelector(GameplayTag tag, out ITargetSelector? selector)
        {
            return _targetSelectors.TryGetValue(tag.Index, out selector);
        }
    }
}
