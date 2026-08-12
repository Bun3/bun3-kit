#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 엄격한 JSON에서 한 번 만들어진 뒤 변경되지 않는 게임플레이 태그 카탈로그입니다.
    /// </summary>
    public sealed partial class TagCatalog
    {
        private readonly Dictionary<string, ushort> _byCanonicalName;
        private readonly string[] _displayNames;
        private readonly ushort[] _parents;
        private readonly ushort[] _subtreeEnds;

        private TagCatalog(
            Dictionary<string, ushort> byCanonicalName,
            string[] displayNames,
            ushort[] parents,
            ushort[] subtreeEnds)
        {
            _byCanonicalName = byCanonicalName;
            _displayNames = displayNames;
            _parents = parents;
            _subtreeEnds = subtreeEnds;
            Count = displayNames.Length - 1;
        }

        /// <summary>카탈로그에 있는 태그 수이며 None은 포함하지 않습니다.</summary>
        public int Count { get; }

        /// <summary>
        /// UTF-8 JSON 스트림의 현재 위치부터 끝까지 읽어 불변 카탈로그를 만듭니다.
        /// </summary>
        /// <param name="utf8Json">읽을 수 있는 UTF-8 JSON 스트림입니다.</param>
        /// <returns>검증되고 색인화된 카탈로그입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="utf8Json"/>이 null인 경우입니다.</exception>
        /// <exception cref="ArgumentException">스트림을 읽을 수 없는 경우입니다.</exception>
        /// <exception cref="TagCatalogException">JSON 또는 카탈로그가 유효하지 않은 경우입니다.</exception>
        public static TagCatalog Load(Stream utf8Json)
        {
            if (utf8Json is null) throw new ArgumentNullException(nameof(utf8Json));
            if (!utf8Json.CanRead) throw new ArgumentException("읽을 수 있는 스트림이 필요합니다.", nameof(utf8Json));
            return Loader.Load(utf8Json);
        }

        /// <summary>경로에 해당하는 등록 태그를 찾습니다.</summary>
        /// <param name="path">ASCII 영숫자 세그먼트 경로입니다.</param>
        /// <param name="tag">찾은 태그 또는 None입니다.</param>
        /// <returns>문법상 유효한 경로가 등록되어 있으면 true입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> 문법이 올바르지 않은 경우입니다.</exception>
        public bool TryGet(string path, out GameplayTag tag)
        {
            if (!TagName.TryFold(path, out var canonical))
            {
                throw new ArgumentException("태그 경로 문법이 올바르지 않습니다.", nameof(path));
            }

            if (_byCanonicalName.TryGetValue(canonical, out var index))
            {
                tag = new GameplayTag(index);
                return true;
            }

            tag = GameplayTag.None;
            return false;
        }

        /// <summary>경로에 해당하는 등록 태그를 찾거나 없으면 예외를 던집니다.</summary>
        /// <param name="path">ASCII 영숫자 세그먼트 경로입니다.</param>
        /// <returns>찾은 태그입니다.</returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> 문법이 올바르지 않은 경우입니다.</exception>
        /// <exception cref="KeyNotFoundException">유효한 경로가 등록되어 있지 않은 경우입니다.</exception>
        public GameplayTag GetRequired(string path)
        {
            if (TryGet(path, out var tag))
            {
                return tag;
            }

            throw new KeyNotFoundException($"등록되지 않은 태그 경로입니다: {path}");
        }

        /// <summary>카탈로그 범위 안의 wire index를 태그로 복원합니다.</summary>
        /// <param name="index">복원할 wire index입니다.</param>
        /// <param name="tag">복원한 태그 또는 None입니다.</param>
        /// <returns>index가 카탈로그 범위 안이면 true입니다.</returns>
        public bool TryGetByIndex(ushort index, out GameplayTag tag)
        {
            if (index <= Count)
            {
                tag = new GameplayTag(index);
                return true;
            }

            tag = GameplayTag.None;
            return false;
        }

        /// <summary>카탈로그 범위 안의 wire index를 태그로 복원합니다.</summary>
        /// <param name="index">복원할 wire index입니다.</param>
        /// <returns>복원한 태그입니다.</returns>
        /// <exception cref="ArgumentOutOfRangeException">index가 카탈로그 범위를 벗어난 경우입니다.</exception>
        public GameplayTag GetRequiredByIndex(ushort index)
        {
            if (TryGetByIndex(index, out var tag))
            {
                return tag;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }

        /// <summary>태그의 표시용 대소문자 보존 이름을 가져옵니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>등록된 표시 이름 또는 빈 문자열입니다.</returns>
        public string GetDisplayName(GameplayTag tag) =>
            tag.IsValid && tag.Index <= Count ? _displayNames[tag.Index] : string.Empty;

        /// <summary>태그의 직접 부모를 가져오며 루트 또는 잘못된 태그에는 None을 반환합니다.</summary>
        /// <param name="tag">조회할 태그입니다.</param>
        /// <returns>직접 부모 또는 None입니다.</returns>
        public GameplayTag GetParent(GameplayTag tag) =>
            tag.IsValid && tag.Index <= Count ? new GameplayTag(_parents[tag.Index]) : GameplayTag.None;

        /// <summary>ancestor가 tag 자신 또는 조상인지 검사합니다.</summary>
        /// <param name="ancestor">후보 조상 태그입니다.</param>
        /// <param name="tag">후손 후보 태그입니다.</param>
        /// <returns>ancestor가 tag 자신 또는 조상이면 true입니다.</returns>
        public bool IsAncestorOrSelf(GameplayTag ancestor, GameplayTag tag)
        {
            if (!ancestor.IsValid || !tag.IsValid || ancestor.Index > Count || tag.Index > Count)
            {
                return false;
            }

            return ancestor.Index <= tag.Index && tag.Index <= _subtreeEnds[ancestor.Index];
        }

        internal ushort GetSubtreeEnd(GameplayTag tag) =>
            tag.IsValid && tag.Index <= Count ? _subtreeEnds[tag.Index] : (ushort)0;
    }
}
