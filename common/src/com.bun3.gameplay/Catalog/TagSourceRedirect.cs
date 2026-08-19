#nullable enable
using System;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// 한 태그 이름을 다른 태그 이름으로 바꾸는 Source 리디렉션입니다.
    /// </summary>
    public sealed class TagSourceRedirect
    {
        /// <summary>소문자로 정규화된 이전 태그 이름입니다.</summary>
        public string From { get; }

        /// <summary>소문자로 정규화된 대상 태그 이름입니다.</summary>
        public string To { get; }

        /// <summary>태그 Source 리디렉션을 만듭니다.</summary>
        public TagSourceRedirect(string from, string to)
        {
            if (!TagName.TryFold(from, out var canonicalFrom))
            {
                throw new ArgumentException("리디렉션 원본 태그 이름 형식이 올바르지 않습니다.", nameof(from));
            }

            if (!TagName.TryFold(to, out var canonicalTo))
            {
                throw new ArgumentException("리디렉션 대상 태그 이름 형식이 올바르지 않습니다.", nameof(to));
            }

            From = canonicalFrom;
            To = canonicalTo;
        }
    }
}
