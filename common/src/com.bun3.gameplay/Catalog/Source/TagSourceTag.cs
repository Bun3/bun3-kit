#nullable enable
using System;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// 하나의 Source가 선언한 태그와 설명입니다.
    /// </summary>
    public sealed class TagSourceTag
    {
        /// <summary>소문자로 정규화된 태그 이름입니다.</summary>
        public string Name { get; }

        /// <summary>Source가 제공한 태그 설명입니다.</summary>
        public string Comment { get; }

        /// <summary>태그 Source 행을 만듭니다.</summary>
        public TagSourceTag(string name, string comment)
        {
            if (!TagName.TryFold(name, out var canonical))
            {
                throw new ArgumentException("태그 이름 형식이 올바르지 않습니다.", nameof(name));
            }

            Name = canonical;
            Comment = comment ?? throw new ArgumentNullException(nameof(comment));
        }
    }
}
