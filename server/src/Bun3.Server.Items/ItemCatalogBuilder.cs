using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 카탈로그 빌더 — 기동 시 게임이 정의 소스(DB/JSON/코드)를 읽어 채우고
    /// <see cref="Build"/> 1회로 불변 카탈로그를 만든다. 검증 델리게이트는 빌드 시
    /// 일괄 실행되며 실패는 <see cref="ItemCatalogException"/>으로 기동을 막는다.
    /// </summary>
    /// <typeparam name="TDefinition">게임이 정의하는 아이템 정의 타입.</typeparam>
    public sealed class ItemCatalogBuilder<TDefinition>
    {
        private readonly List<string> _ids = new List<string>();
        private readonly List<TDefinition> _definitions = new List<TDefinition>();
        private readonly List<long> _maxStacks = new List<long>();
        private readonly List<long> _externalIds = new List<long>();
        private readonly List<bool> _unstackables = new List<bool>();
        private readonly Dictionary<string, int> _lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<long, int> _externalLookup = new Dictionary<long, int>();
        private readonly List<Action<ItemCatalog<TDefinition>>> _validators = new List<Action<ItemCatalog<TDefinition>>>();
        private bool _built;

        /// <summary>
        /// 정의를 등록한다. id는 카탈로그가 인터닝해 보관하며 이후
        /// <see cref="ItemCatalog.GetIdString"/>이 같은 참조를 돌려준다.
        /// </summary>
        /// <param name="id">고유 문자열 id(서수 비교) — 정식 키. 중복이면 던진다.</param>
        /// <param name="definition">게임 정의(불투명 보관).</param>
        /// <param name="maxStack">정의당 최대 보유량(스택형=스택 상한, 비스택형=최대 인스턴스 수).
        /// 기본 <see cref="long.MaxValue"/> = 무제한. 0 이하는 거부.</param>
        /// <param name="externalId">선택적 외부 숫자 id(DB 컬럼·Steam itemdefid 등) — 역색인에
        /// 등록된다. 중복이면 던진다. <see cref="long.MinValue"/>는 예약값이라 거부.</param>
        /// <param name="unstackable">true면 비스택형(인스턴스형) — 수량 병합 대신 개별
        /// 인스턴스로 보유하며 <see cref="ItemStackContainer{TQuantity,TOps}"/>가 거부한다.</param>
        /// <returns>체이닝용 빌더 자신.</returns>
        public ItemCatalogBuilder<TDefinition> Register(
            string id,
            TDefinition definition,
            long maxStack = long.MaxValue,
            long? externalId = null,
            bool unstackable = false)
        {
            ThrowIfBuilt();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("아이템 id는 비어 있을 수 없습니다.", nameof(id));
            }

            if (maxStack <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxStack), maxStack, "maxStack은 1 이상이어야 합니다.");
            }

            if (externalId == ItemCatalog.NoExternalId)
            {
                throw new ArgumentOutOfRangeException(nameof(externalId), externalId, "long.MinValue는 예약값입니다.");
            }

            if (_lookup.ContainsKey(id))
            {
                throw new ItemCatalogException($"중복 등록된 아이템 id: '{id}'");
            }

            if (externalId.HasValue && _externalLookup.ContainsKey(externalId.Value))
            {
                throw new ItemCatalogException($"중복 등록된 외부 id: {externalId.Value} (아이템 '{id}')");
            }

            if (externalId.HasValue)
            {
                _externalLookup.Add(externalId.Value, _ids.Count);
            }

            _lookup.Add(id, _ids.Count);
            _ids.Add(id);
            _definitions.Add(definition);
            _maxStacks.Add(maxStack);
            _externalIds.Add(externalId ?? ItemCatalog.NoExternalId);
            _unstackables.Add(unstackable);
            return this;
        }

        /// <summary>
        /// 빌드 시 실행할 검증 델리게이트를 추가한다. 게임 규칙 위반은
        /// <see cref="ItemCatalogException"/>을 던져 기동을 막는다.
        /// </summary>
        /// <param name="validator">완성된 카탈로그를 받아 검증하는 델리게이트.</param>
        /// <returns>체이닝용 빌더 자신.</returns>
        public ItemCatalogBuilder<TDefinition> AddValidator(Action<ItemCatalog<TDefinition>> validator)
        {
            ThrowIfBuilt();
            _validators.Add(validator ?? throw new ArgumentNullException(nameof(validator)));
            return this;
        }

        /// <summary>카탈로그를 빌드하고 검증을 실행한다. 빌더당 1회만 호출할 수 있다.</summary>
        public ItemCatalog<TDefinition> Build()
        {
            ThrowIfBuilt();
            _built = true;

            var catalog = new ItemCatalog<TDefinition>(
                _ids.ToArray(),
                _maxStacks.ToArray(),
                _externalIds.ToArray(),
                _unstackables.ToArray(),
                _lookup,
                _externalLookup,
                _definitions.ToArray());

            foreach (var validator in _validators)
            {
                validator(catalog);
            }

            return catalog;
        }

        private void ThrowIfBuilt()
        {
            if (_built)
            {
                throw new InvalidOperationException("이미 빌드된 빌더입니다 — 카탈로그는 기동 시 1회만 만든다.");
            }
        }
    }
}
