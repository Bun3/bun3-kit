#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// 한 태그 Source에서 읽거나 쓸 전체 문서입니다.
    /// </summary>
    public sealed class TagSourceDocument
    {
        /// <summary>문서를 소유한 Source의 설명자입니다.</summary>
        public TagSourceDescriptor Descriptor { get; }

        /// <summary>오류 진단에 쓰는 원본 경로 또는 선언 레이블입니다.</summary>
        public string Origin { get; }

        /// <summary>Source가 명시적으로 선언한 태그입니다.</summary>
        public IReadOnlyList<TagSourceTag> Tags { get; }

        /// <summary>Source가 선언한 태그 리디렉션입니다.</summary>
        public IReadOnlyList<TagSourceRedirect> Redirects { get; }

        /// <summary>방어적으로 복사한 태그 Source 문서를 만듭니다.</summary>
        public TagSourceDocument(
            TagSourceDescriptor descriptor,
            string origin,
            IReadOnlyList<TagSourceTag> tags,
            IReadOnlyList<TagSourceRedirect> redirects)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            Tags = Copy(tags, nameof(tags));
            Redirects = Copy(redirects, nameof(redirects));
        }

        private static T[] Copy<T>(IReadOnlyList<T> values, string parameterName)
        {
            if (values is null) throw new ArgumentNullException(parameterName);
            var copy = new T[values.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }
}
