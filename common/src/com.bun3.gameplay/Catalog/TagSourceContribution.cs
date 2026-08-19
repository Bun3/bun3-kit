#nullable enable
using System;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>한 Source가 병합 태그에 제공한 작성 정보를 나타냅니다.</summary>
    public sealed class TagSourceContribution
    {
        /// <summary>태그를 제공한 Source의 안정적인 식별자입니다.</summary>
        public string SourceId { get; }

        /// <summary>사용자에게 표시할 Source 이름입니다.</summary>
        public string DisplayName { get; }

        /// <summary>Source의 원본 경로 또는 선언 레이블입니다.</summary>
        public string Origin { get; }

        /// <summary>이 Source가 제공한 설명이며 암시 태그이면 빈 문자열입니다.</summary>
        public string Comment { get; }

        /// <summary>이 Source가 태그를 명시적으로 선언했는지 나타냅니다.</summary>
        public bool IsExplicit { get; }

        /// <summary>이 Source가 읽기 전용인지 나타냅니다.</summary>
        public bool IsReadOnly { get; }

        internal TagSourceContribution(
            string sourceId,
            string displayName,
            string origin,
            string comment,
            bool isExplicit,
            bool isReadOnly)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
            Comment = comment ?? throw new ArgumentNullException(nameof(comment));
            IsExplicit = isExplicit;
            IsReadOnly = isReadOnly;
        }
    }
}
