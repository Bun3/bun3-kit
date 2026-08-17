#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Seams
{
    /// <summary>시섬(Seam) 계약을 등록하고 검증하는 빌더입니다.</summary>
    public sealed class SeamRegistryBuilder
    {
        private readonly Dictionary<ushort, IMagnitudeCalc> _magnitudeCalcs = new();
        private readonly Dictionary<ushort, IExecutionCalc> _executionCalcs = new();
        private readonly Dictionary<ushort, ITargetSelector> _targetSelectors = new();

        /// <summary>크기 계산 계약을 등록합니다.</summary>
        /// <param name="tag">등록할 태그입니다.</param>
        /// <param name="calc">계산 구현입니다.</param>
        /// <exception cref="ArgumentNullException">계산이 null일 때 발생합니다.</exception>
        /// <exception cref="InvalidOperationException">같은 태그가 이미 등록되었을 때 발생합니다.</exception>
        public void RegisterMagnitudeCalc(GameplayTag tag, IMagnitudeCalc calc)
        {
            if (calc == null)
                throw new ArgumentNullException(nameof(calc));
            if (_magnitudeCalcs.ContainsKey(tag.Index))
                throw new InvalidOperationException($"태그 {tag.Index}는 이미 등록되었습니다.");
            _magnitudeCalcs[tag.Index] = calc;
        }

        /// <summary>효과 실행 계약을 등록합니다.</summary>
        /// <param name="tag">등록할 태그입니다.</param>
        /// <param name="exec">실행 구현입니다.</param>
        /// <exception cref="ArgumentNullException">실행이 null일 때 발생합니다.</exception>
        /// <exception cref="InvalidOperationException">같은 태그가 이미 등록되었을 때 발생합니다.</exception>
        public void RegisterExecutionCalc(GameplayTag tag, IExecutionCalc exec)
        {
            if (exec == null)
                throw new ArgumentNullException(nameof(exec));
            if (_executionCalcs.ContainsKey(tag.Index))
                throw new InvalidOperationException($"태그 {tag.Index}는 이미 등록되었습니다.");
            _executionCalcs[tag.Index] = exec;
        }

        /// <summary>대상 선택 계약을 등록합니다.</summary>
        /// <param name="tag">등록할 태그입니다.</param>
        /// <param name="selector">선택 구현입니다.</param>
        /// <exception cref="ArgumentNullException">선택이 null일 때 발생합니다.</exception>
        /// <exception cref="InvalidOperationException">같은 태그가 이미 등록되었을 때 발생합니다.</exception>
        public void RegisterTargetSelector(GameplayTag tag, ITargetSelector selector)
        {
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));
            if (_targetSelectors.ContainsKey(tag.Index))
                throw new InvalidOperationException($"태그 {tag.Index}는 이미 등록되었습니다.");
            _targetSelectors[tag.Index] = selector;
        }

        /// <summary>등록된 계약들을 바탕으로 시섬 레지스트리를 구축합니다.</summary>
        /// <param name="catalog">태그 카탈로그입니다.</param>
        /// <returns>구축된 레지스트리입니다.</returns>
        /// <exception cref="InvalidOperationException">태그가 예약된 루트 하위가 아니거나, 루트 태그 자체이거나, 루트 태그가 카탈로그에 없을 때 발생합니다.</exception>
        public SeamRegistry Build(TagCatalog catalog)
        {
            const string magnitudeRoot = "calc.magnitude";
            const string executionRoot = "calc.execution";
            const string selectorRoot = "selector";

            GameplayTag magRootTag = GameplayTag.None;
            GameplayTag execRootTag = GameplayTag.None;
            GameplayTag selectorRootTag = GameplayTag.None;

            // 루트 태그를 카탈로그에서 찾기
            if (_magnitudeCalcs.Count > 0)
            {
                if (!catalog.TryGet(magnitudeRoot, out magRootTag))
                    throw new InvalidOperationException($"예약 루트 태그가 카탈로그에 없습니다: {magnitudeRoot}");
            }

            if (_executionCalcs.Count > 0)
            {
                if (!catalog.TryGet(executionRoot, out execRootTag))
                    throw new InvalidOperationException($"예약 루트 태그가 카탈로그에 없습니다: {executionRoot}");
            }

            if (_targetSelectors.Count > 0)
            {
                if (!catalog.TryGet(selectorRoot, out selectorRootTag))
                    throw new InvalidOperationException($"예약 루트 태그가 카탈로그에 없습니다: {selectorRoot}");
            }

            // 크기 계산 검증
            foreach (var kvp in _magnitudeCalcs)
            {
                var tag = new GameplayTag(kvp.Key);
                if (tag == magRootTag)
                    throw new InvalidOperationException($"루트 태그 자체는 등록할 수 없습니다: {magnitudeRoot}");
                if (!catalog.IsAncestorOrSelf(magRootTag, tag))
                    throw new InvalidOperationException($"태그 {tag.Index}는 {magnitudeRoot} 루트 하위가 아닙니다.");
            }

            // 효과 실행 검증
            foreach (var kvp in _executionCalcs)
            {
                var tag = new GameplayTag(kvp.Key);
                if (tag == execRootTag)
                    throw new InvalidOperationException($"루트 태그 자체는 등록할 수 없습니다: {executionRoot}");
                if (!catalog.IsAncestorOrSelf(execRootTag, tag))
                    throw new InvalidOperationException($"태그 {tag.Index}는 {executionRoot} 루트 하위가 아닙니다.");
            }

            // 대상 선택 검증
            foreach (var kvp in _targetSelectors)
            {
                var tag = new GameplayTag(kvp.Key);
                if (tag == selectorRootTag)
                    throw new InvalidOperationException($"루트 태그 자체는 등록할 수 없습니다: {selectorRoot}");
                if (!catalog.IsAncestorOrSelf(selectorRootTag, tag))
                    throw new InvalidOperationException($"태그 {tag.Index}는 {selectorRoot} 루트 하위가 아닙니다.");
            }

            return new SeamRegistry(_magnitudeCalcs, _executionCalcs, _targetSelectors);
        }
    }
}
