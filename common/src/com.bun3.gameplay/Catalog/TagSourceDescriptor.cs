#nullable enable
using System;

namespace Bun3.Gameplay.Tags.Catalog
{
    /// <summary>
    /// 태그 Source의 안정적인 식별자와 표시 정보를 나타냅니다.
    /// </summary>
    public sealed class TagSourceDescriptor
    {
        /// <summary>태그 Source 식별자입니다.</summary>
        public string SourceId { get; }

        /// <summary>사용자에게 표시할 Source 이름입니다.</summary>
        public string DisplayName { get; }

        /// <summary>Source의 제공 형식입니다.</summary>
        public TagSourceKind Kind { get; }

        /// <summary>Source가 읽기 전용인지 나타냅니다.</summary>
        public bool IsReadOnly { get; }

        /// <summary>검증된 태그 Source 설명자를 만듭니다.</summary>
        public TagSourceDescriptor(string sourceId, string displayName, TagSourceKind kind, bool isReadOnly)
        {
            if (!IsValidSourceId(sourceId))
            {
                throw new ArgumentException("Source ID는 소문자 영숫자 세그먼트를 점 또는 하이픈으로 연결해야 합니다.", nameof(sourceId));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("표시 이름은 비어 있을 수 없습니다.", nameof(displayName));
            }

            if (kind == TagSourceKind.GameJson)
            {
                if (!string.Equals(sourceId, "game", StringComparison.Ordinal) || isReadOnly)
                {
                    throw new ArgumentException("GameJson Source는 ID가 game이고 편집 가능해야 합니다.", nameof(sourceId));
                }
            }
            else
            {
                if (string.Equals(sourceId, "game", StringComparison.Ordinal) || !isReadOnly)
                {
                    throw new ArgumentException("game ID 이외의 Source는 읽기 전용이어야 합니다.", nameof(sourceId));
                }

                if (kind != TagSourceKind.PackageJson && kind != TagSourceKind.Native)
                {
                    throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }

            SourceId = sourceId;
            DisplayName = displayName;
            Kind = kind;
            IsReadOnly = isReadOnly;
        }

        private static bool IsValidSourceId(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var sourceId = value!;
            var previousWasSeparator = true;
            for (var i = 0; i < sourceId.Length; i++)
            {
                var character = sourceId[i];
                var alphaNumeric = (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9');
                if (alphaNumeric)
                {
                    previousWasSeparator = false;
                    continue;
                }

                if ((character == '.' || character == '-') && !previousWasSeparator)
                {
                    previousWasSeparator = true;
                    continue;
                }

                return false;
            }

            return !previousWasSeparator;
        }
    }
}
