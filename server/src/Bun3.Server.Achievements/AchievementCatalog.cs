using System;
using System.Collections.Generic;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// 업적 정의 카탈로그 — 기동 시 게임 로더가 만든 정의 목록을 받아 일괄 검증 후
    /// 동결한다(불변). 문자열 id와 태그는 여기서 조밀한 int 인덱스로 인터닝되며,
    /// 이후 런타임 식별자는 인덱스다 — 게임은 기동 시 <see cref="GetIndex"/>·
    /// <see cref="GetTagIndex"/>로 인덱스를 캐시하고 핫패스에서는 인덱스만 쓴다.
    /// 검증 실패는 예외 = 기동 실패.
    /// </summary>
    /// <typeparam name="TDef">게임의 업적 정의 타입 — 훅과 조회가 캐스팅 없이 이 타입을 받는다.</typeparam>
    public sealed class AchievementCatalog<TDef> where TDef : AchievementDefinition
    {
        /// <summary>카탈로그가 받는 정의 수 상한 — 실수로 만든 거대 입력(생성기 폭주 등)을
        /// 기동 시점에 걸러내기 위한 안전판.</summary>
        public const int MaxDefinitions = 65_536;

        private readonly TDef[] _definitions;
        private readonly Dictionary<string, int> _indexById;
        private readonly Dictionary<string, int> _tagIndexByName;
        private readonly int[][] _indicesByTag;

        /// <summary>정의 수.</summary>
        public int Count => _definitions.Length;

        /// <summary>인터닝된 태그 수.</summary>
        public int TagCount => _indicesByTag.Length;

        /// <summary>정의 목록을 검증하고 동결한다. 프레임워크 검증(빈/중복 id, Target ≤ 0,
        /// 상한 초과, 가용성 범위, 빈/중복 태그) 후 정의별로 <paramref name="validator"/>를
        /// 호출한다 — 도메인 불변식(보상 테이블 존재 등)은 게임이 여기서 던진다.</summary>
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
            _tagIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var indicesByTag = new List<List<int>>();

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
                if ((uint)def.InitialAvailability > (uint)AchievementStatus.Active)
                {
                    throw new ArgumentException($"업적 '{def.Id}'의 InitialAvailability는 Locked/Ready/Active만 가능합니다 ({def.InitialAvailability}).", nameof(definitions));
                }
                if (_indexById.ContainsKey(def.Id))
                {
                    throw new ArgumentException($"업적 Id '{def.Id}'가 중복입니다.", nameof(definitions));
                }

                _indexById.Add(def.Id, i);

                for (var t = 0; t < def.Tags.Count; t++)
                {
                    var tag = def.Tags[t];
                    if (string.IsNullOrEmpty(tag))
                    {
                        throw new ArgumentException($"업적 '{def.Id}'의 태그 [{t}]가 비어 있습니다.", nameof(definitions));
                    }
                    if (!_tagIndexByName.TryGetValue(tag, out var tagIndex))
                    {
                        tagIndex = indicesByTag.Count;
                        _tagIndexByName.Add(tag, tagIndex);
                        indicesByTag.Add(new List<int>());
                    }

                    var members = indicesByTag[tagIndex];
                    if (members.Count > 0 && members[members.Count - 1] == i)
                    {
                        throw new ArgumentException($"업적 '{def.Id}'에 태그 '{tag}'가 중복으로 붙어 있습니다.", nameof(definitions));
                    }
                    members.Add(i);
                }

                validator?.Invoke(def);
                _definitions[i] = def;
            }

            _indicesByTag = new int[indicesByTag.Count][];
            for (var t = 0; t < indicesByTag.Count; t++)
            {
                _indicesByTag[t] = indicesByTag[t].ToArray();
            }
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

        /// <summary>태그명으로 태그 인덱스를 조회한다 — 기동 시 1회 호출해 캐시할 것.
        /// 오타는 여기서 기동 실패로 표면화된다.</summary>
        /// <exception cref="KeyNotFoundException">태그가 카탈로그에 없을 때.</exception>
        public int GetTagIndex(string tag)
        {
            if (!_tagIndexByName.TryGetValue(tag, out var index))
            {
                throw new KeyNotFoundException($"업적 태그 '{tag}'가 카탈로그에 없습니다.");
            }

            return index;
        }

        /// <summary>태그명으로 태그 인덱스를 조회한다. 없으면 false.</summary>
        public bool TryGetTagIndex(string tag, out int index) => _tagIndexByName.TryGetValue(tag, out index);

        /// <summary>태그가 붙은 업적 인덱스 목록(동결, 정의 순). 라우팅 외에 리셋 스윕·선발
        /// 같은 그룹 순회에도 쓴다 — 무할당.</summary>
        public ReadOnlySpan<int> GetIndicesByTag(int tagIndex) => _indicesByTag[tagIndex];
    }
}
