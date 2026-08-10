using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 카운트를 갖는 태그 집합 — 같은 태그를 부여하는 소스 여럿이 공존한다(하나가 꺼져도
    /// 카운트만 줄어든다). 계층 쿼리(Has/Count)는 보유 태그의 조상 체인을 걷는다.
    /// 단일 스레드(월드 액터) 소유 전제 — 스레드 안전을 보장하지 않는다.
    /// </summary>
    public sealed class TagSet
    {
        private readonly TagRegistry _registry;
        private readonly Dictionary<int, int> _counts = new Dictionary<int, int>();

        /// <summary>레지스트리에 바인딩된 빈 집합을 만든다.</summary>
        public TagSet(TagRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>보유 태그 종류 수(카운트 무관).</summary>
        public int KindCount => _counts.Count;

        /// <summary>태그를 count만큼 추가한다.</summary>
        public void Add(GameplayTag tag, int count = 1)
        {
            RequireValid(tag);
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _counts.TryGetValue(tag.Handle, out var current);
            _counts[tag.Handle] = current + count;
        }

        /// <summary>태그를 count만큼 제거한다. 0 이하가 되면 집합에서 빠진다.
        /// 애초에 없었으면 false.</summary>
        public bool Remove(GameplayTag tag, int count = 1)
        {
            RequireValid(tag);
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (!_counts.TryGetValue(tag.Handle, out var current))
            {
                return false;
            }

            var next = current - count;
            if (next > 0)
            {
                _counts[tag.Handle] = next;
            }
            else
            {
                _counts.Remove(tag.Handle);
            }

            return true;
        }

        /// <summary>정확히 이 태그를 보유하는지(계층 아님).</summary>
        public bool HasExact(GameplayTag tag) =>
            tag.IsValid && _counts.ContainsKey(tag.Handle);

        /// <summary>정확히 이 태그의 카운트. 없으면 0.</summary>
        public int ExactCount(GameplayTag tag) =>
            tag.IsValid && _counts.TryGetValue(tag.Handle, out var count) ? count : 0;

        /// <summary>계층 매칭: 보유 태그 중 tag 자신이거나 그 하위인 것이 있는지.
        /// 예) "State.Dead.Ghost" 보유 시 Has("State.Dead") == true.</summary>
        public bool Has(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                return false;
            }

            foreach (var pair in _counts)
            {
                if (_registry.IsAncestorOrSelf(tag, new GameplayTag(pair.Key)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>계층 카운트: tag 자신·하위 태그들의 카운트 합.</summary>
        public int Count(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                return 0;
            }

            var total = 0;
            foreach (var pair in _counts)
            {
                if (_registry.IsAncestorOrSelf(tag, new GameplayTag(pair.Key)))
                {
                    total += pair.Value;
                }
            }

            return total;
        }

        private static void RequireValid(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                throw new ArgumentException("무효 태그(None)는 집합에 넣을 수 없다.", nameof(tag));
            }
        }
    }
}
