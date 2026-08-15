#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Gameplay.Tags
{
    /// <summary>Unity 자산에 canonical 태그 경로를 저장하는 authoring reference입니다.</summary>
    [Serializable]
    public struct GameplayTagRef : IEquatable<GameplayTagRef>
    {
        [SerializeField]
        private string? _path;

        /// <summary>태그를 참조하지 않는 기본값입니다.</summary>
        public static readonly GameplayTagRef None = default;

        /// <summary>새 reference를 만들고 입력 경로를 canonical 소문자로 정규화합니다.</summary>
        /// <param name="path">저장할 태그 경로입니다.</param>
        /// <exception cref="ArgumentNullException"><paramref name="path"/>가 null인 경우입니다.</exception>
        /// <exception cref="ArgumentException">태그 경로 문법이 올바르지 않은 경우입니다.</exception>
        public GameplayTagRef(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (path.Length == 0)
            {
                _path = string.Empty;
                return;
            }

            if (!TagName.TryFold(path, out var canonical))
            {
                throw new ArgumentException("태그 경로 문법이 올바르지 않습니다.", nameof(path));
            }

            _path = canonical;
        }

        /// <summary>직렬화된 태그 경로이며 빈 문자열은 None을 뜻합니다.</summary>
        public string Path => _path ?? string.Empty;

        /// <summary>태그를 참조하지 않는지 나타냅니다.</summary>
        public bool IsEmpty => Path.Length == 0;

        /// <summary>명시한 Runtime Catalog에서 현재 reference를 태그로 해석합니다.</summary>
        /// <param name="catalog">해석에 사용할 Runtime Catalog입니다.</param>
        /// <param name="tag">해석된 태그 또는 None입니다.</param>
        /// <returns>None이거나 등록된 경로로 해석되면 true입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/>이 null인 경우입니다.</exception>
        public bool TryResolve(TagCatalog catalog, out GameplayTag tag)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (IsEmpty)
            {
                tag = GameplayTag.None;
                return true;
            }

            if (!TagName.TryFold(Path, out _))
            {
                tag = GameplayTag.None;
                return false;
            }

            return catalog.TryGet(Path, out tag);
        }

        /// <summary>명시한 Runtime Catalog에서 현재 reference를 태그로 해석하거나 실패하면 예외를 던집니다.</summary>
        /// <param name="catalog">해석에 사용할 Runtime Catalog입니다.</param>
        /// <returns>해석된 태그이며 빈 reference이면 None입니다.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/>이 null인 경우입니다.</exception>
        /// <exception cref="ArgumentException">직렬화된 태그 경로 문법이 올바르지 않은 경우입니다.</exception>
        /// <exception cref="KeyNotFoundException">현재 Catalog에 태그 경로가 없는 경우입니다.</exception>
        public GameplayTag ResolveRequired(TagCatalog catalog)
        {
            if (catalog is null) throw new ArgumentNullException(nameof(catalog));
            if (IsEmpty) return GameplayTag.None;
            if (!TagName.TryFold(Path, out _))
            {
                throw new ArgumentException("직렬화된 태그 경로 문법이 올바르지 않습니다.", nameof(Path));
            }

            return catalog.GetRequired(Path);
        }

        /// <summary>두 reference의 직렬화 경로가 ordinal 기준으로 같은지 비교합니다.</summary>
        /// <param name="other">비교할 reference입니다.</param>
        /// <returns>경로가 같으면 true입니다.</returns>
        public bool Equals(GameplayTagRef other) =>
            string.Equals(Path, other.Path, StringComparison.Ordinal);

        /// <summary>지정한 객체가 같은 경로를 가진 reference인지 비교합니다.</summary>
        /// <param name="obj">비교할 객체입니다.</param>
        /// <returns>같은 reference이면 true입니다.</returns>
        public override bool Equals(object? obj) => obj is GameplayTagRef other && Equals(other);

        /// <summary>직렬화 경로의 ordinal hash code를 반환합니다.</summary>
        /// <returns>reference hash code입니다.</returns>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Path);

        /// <summary>두 reference가 같은 경로를 저장하는지 비교합니다.</summary>
        public static bool operator ==(GameplayTagRef left, GameplayTagRef right) => left.Equals(right);

        /// <summary>두 reference가 다른 경로를 저장하는지 비교합니다.</summary>
        public static bool operator !=(GameplayTagRef left, GameplayTagRef right) => !left.Equals(right);
    }
}
