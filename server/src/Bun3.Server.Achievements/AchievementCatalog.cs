using System;
using System.Collections.Generic;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// 업적 정의 카탈로그 — 기동 시 게임 로더가 만든 정의 목록을 받아 일괄 검증 후
    /// 동결한다(불변). 문자열 id는 여기서 조밀한 int 인덱스(0..Count-1)로 인터닝되며,
    /// 이후 런타임 식별자는 인덱스다 — 게임은 기동 시 <see cref="GetIndex"/>로 인덱스를
    /// 캐시하고 핫패스에서는 인덱스만 쓴다. 검증 실패는 예외 = 기동 실패.
    /// </summary>
    /// <typeparam name="TDef">게임의 업적 정의 타입 — 훅과 조회가 캐스팅 없이 이 타입을 받는다.</typeparam>
    public sealed class AchievementCatalog<TDef> where TDef : AchievementDefinition
    {
        /// <summary>카탈로그가 받는 정의 수 상한 — 실수로 만든 거대 입력(생성기 폭주 등)을
        /// 기동 시점에 걸러내기 위한 안전판.</summary>
        public const int MaxDefinitions = 65_536;

        private readonly TDef[] _definitions;
        private readonly Dictionary<string, int> _indexById;

        /// <summary>정의 수.</summary>
        public int Count => _definitions.Length;

        /// <summary>정의 목록을 검증하고 동결한다. 프레임워크 검증(빈/중복 id, Target ≤ 0,
        /// 상한 초과) 후 정의별로 <paramref name="validator"/>를 호출한다 — 도메인 불변식
        /// (보상 테이블 존재 등)은 게임이 여기서 던진다.</summary>
        /// <exception cref="ArgumentException">정의 목록이 불변식을 위반할 때.</exception>
        public AchievementCatalog(IReadOnlyList<TDef> definitions, Action<TDef>? validator = null)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (definitions.Count > MaxDefinitions)
            {
                throw new ArgumentException($"업적 정의 수가 상한을 초과했습니다 ({definitions.Count} > {MaxDefinitions}).", nameof(definitions));
            }

            _definitions = new TDef[definitions.Count];
            _indexById = new Dictionary<string, int>(definitions.Count, StringComparer.Ordinal);
            for (var i = 0; i < definitions.Count; i++)
            {
                var def = definitions[i];
                if (def == null)
                {
                    throw new ArgumentException($"업적 정의 [{i}]가 null입니다.", nameof(definitions));
                }
                if (string.IsNullOrEmpty(def.Id))
                {
                    throw new ArgumentException($"업적 정의 [{i}]의 Id가 비어 있습니다.", nameof(definitions));
                }
                if (def.Target <= 0)
                {
                    throw new ArgumentException($"업적 '{def.Id}'의 Target이 양수가 아닙니다 ({def.Target}).", nameof(definitions));
                }
                if (!TryAddIndex(def.Id, i))
                {
                    throw new ArgumentException($"업적 Id '{def.Id}'가 중복입니다.", nameof(definitions));
                }

                validator?.Invoke(def);
                _definitions[i] = def;
            }
        }

        private bool TryAddIndex(string id, int index)
        {
            if (_indexById.ContainsKey(id))
            {
                return false;
            }

            _indexById.Add(id, index);
            return true;
        }

        /// <summary>인덱스로 정의를 조회한다.</summary>
        public TDef GetDefinition(int index) => _definitions[index];

        /// <summary>id로 인덱스를 조회한다 — 기동 시 1회 호출해 캐시할 것. 없으면 예외.</summary>
        /// <exception cref="KeyNotFoundException">id가 카탈로그에 없을 때.</exception>
        public int GetIndex(string id)
        {
            if (!_indexById.TryGetValue(id, out var index))
            {
                throw new KeyNotFoundException($"업적 Id '{id}'가 카탈로그에 없습니다.");
            }

            return index;
        }

        /// <summary>id로 인덱스를 조회한다. 없으면 false.</summary>
        public bool TryGetIndex(string id, out int index) => _indexById.TryGetValue(id, out index);
    }
}
