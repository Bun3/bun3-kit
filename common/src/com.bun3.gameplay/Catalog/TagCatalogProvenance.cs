#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>병합된 canonical 태그별 Source 작성 정보를 제공하는 불변 색인입니다.</summary>
    public sealed class TagCatalogProvenance
    {
        private static readonly IReadOnlyList<TagSourceContribution> EmptyContributions =
            Array.AsReadOnly(Array.Empty<TagSourceContribution>());
        private readonly Dictionary<string, IReadOnlyList<TagSourceContribution>> _byCanonicalName;

        internal TagCatalogProvenance(Dictionary<string, IReadOnlyList<TagSourceContribution>> byCanonicalName)
        {
            _byCanonicalName = byCanonicalName ?? throw new ArgumentNullException(nameof(byCanonicalName));
        }

        /// <summary>canonical 태그에 기여한 Source 정보를 Source ID 순서로 가져옵니다.</summary>
        /// <param name="canonicalName">조회할 태그 경로입니다.</param>
        /// <returns>Source별 기여 목록이며 등록되지 않은 경로이면 빈 목록입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="canonicalName"/>의 태그 문법이 올바르지 않은 경우입니다.</exception>
        public IReadOnlyList<TagSourceContribution> GetContributions(string canonicalName)
        {
            if (!TagName.TryFold(canonicalName, out var folded))
            {
                throw new ArgumentException("태그 경로 문법이 올바르지 않습니다.", nameof(canonicalName));
            }

            return _byCanonicalName.GetValueOrDefault(folded, EmptyContributions);
        }
    }
}
