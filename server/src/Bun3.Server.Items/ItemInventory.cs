using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 스택/인스턴스 통합 인벤토리 — 유일한 플레이어 아이템 컨테이너. 재화도 스택형
    /// 정의의 아이템 행으로 처리한다(idlez 실물 구조). 스택형·비스택형 판정은 카탈로그
    /// 메타로 내부에서 정확히 한 번 수행하며, 스택형은 정의당 싱글턴 인스턴스로 자동
    /// 병합, 비스택형은 수량 1 인스턴스 N개. 수량은 <see cref="BigNum"/>(long 암시 변환).
    /// 모든 변경 연산은 원자적이며 실패 시 완전 무변경. 락 없음(세션 액터 단일 스레드 계약).
    /// 조회·열거·소모 경로는 무할당, 인스턴스 생성만 본질적 할당(저빈도).
    /// 파일 구성: 이 파일(생성·조회) / Operations(지급·소모·트랜잭션) / Tracking(변경 추적·로드).
    /// </summary>
    /// <typeparam name="TState">게임이 정의하는 인스턴스 상태 타입.</typeparam>
    public sealed partial class ItemInventory<TState>
    {
        // ponytail: 비스택형 1회 연산 인스턴스 수 상한 — 무제한 maxStack 정의에 대량 지급 시
        // 생성 루프 폭주를 막는다. 정당한 대량 지급이 필요해지면 옵션으로 승격.
        internal const int MaxInstancesPerOperation = 1000;

        private readonly ItemCatalog _catalog;
        private readonly Func<long> _instanceIdIssuer;
        private readonly Func<ItemId, TState> _stateFactory;
        private readonly Action? _onChanged;
        private readonly uint _removeBlockingFlags;
        private readonly Dictionary<long, ItemInstance<TState>> _instances;
        private readonly Dictionary<ItemId, long> _stackSingletons;
        private readonly List<long> _removed = new List<long>();
        private readonly List<ItemInstance<TState>> _removeScratch = new List<ItemInstance<TState>>();
        private bool _hasChanges;

        // 트랜잭션 스크래치 — 전부 생성 시 1회 할당 후 재사용(커밋 경로 무할당).
        private readonly List<TxOp> _txOps = new List<TxOp>();
        private readonly List<TxOp> _applyOps = new List<TxOp>();
        private readonly List<long> _txUnstackableTargets = new List<long>();
        private readonly List<long> _txConsumedTargets = new List<long>();
        private readonly List<BigNum> _txResolved = new List<BigNum>();
        private readonly List<ItemId> _txNetIds = new List<ItemId>();
        private readonly List<BigNum> _txNetTotal = new List<BigNum>();
        private readonly List<BigNum> _txNetPool = new List<BigNum>();
        private int _txToken;

        /// <summary>인벤토리를 만든다.</summary>
        /// <param name="catalog">아이템 카탈로그.</param>
        /// <param name="instanceIdIssuer">인스턴스 id 발급자 — 세션 간 고유해야 한다
        /// (스노플레이크·하이로우·DB 시퀀스 등은 게임 선택). 검증 통과 후에만 호출된다.</param>
        /// <param name="stateFactory">인스턴스 생성 시 초기 상태 팩토리.</param>
        /// <param name="capacity">초기 용량(0이면 기본).</param>
        /// <param name="onChanged">성공한 변경 연산당 1회 + <see cref="ItemInstance{TState}.MarkChanged"/>당
        /// 1회 호출 — 게임은 Player.MarkDirty를 넘겨 저장 주기와 맞물린다.</param>
        /// <param name="removeBlockingFlags">이 마스크에 걸리는 플래그의 인스턴스는 소모
        /// 후보에서 제외된다(예: 사용 중·유저 잠금). 0이면 잠금 없음.</param>
        public ItemInventory(
            ItemCatalog catalog,
            Func<long> instanceIdIssuer,
            Func<ItemId, TState> stateFactory,
            int capacity = 0,
            Action? onChanged = null,
            uint removeBlockingFlags = 0)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _instanceIdIssuer = instanceIdIssuer ?? throw new ArgumentNullException(nameof(instanceIdIssuer));
            _stateFactory = stateFactory ?? throw new ArgumentNullException(nameof(stateFactory));
            _onChanged = onChanged;
            _removeBlockingFlags = removeBlockingFlags;
            _instances = new Dictionary<long, ItemInstance<TState>>(capacity);
            _stackSingletons = new Dictionary<ItemId, long>();
        }

        /// <summary>이 인벤토리가 묶인 카탈로그.</summary>
        public ItemCatalog Catalog => _catalog;

        /// <summary>보유 인스턴스 수(스택 싱글턴 포함).</summary>
        public int InstanceCount => _instances.Count;

        /// <summary>정의의 총 보유 수량 — 스택형은 싱글턴 수량, 비스택형은 인스턴스 수. 미보유면 0.</summary>
        public BigNum GetQuantity(ItemId item)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                return _instances[singletonId].Quantity;
            }

            long count = 0;
            // ponytail: 정의별 색인 없이 전체 스캔(O(인스턴스 수)) — 플레이어 인벤 수백 규모 전제.
            foreach (var entry in _instances)
            {
                if (entry.Value.Item == item)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>인스턴스 id로 조회한다.</summary>
        public bool TryGetInstance(long instanceId, out ItemInstance<TState> instance)
        {
            if (_instances.TryGetValue(instanceId, out var found))
            {
                instance = found;
                return true;
            }

            instance = null!;
            return false;
        }

        /// <summary>보유 인스턴스 열거 — foreach 무할당(struct 열거자).</summary>
        public Enumerator GetEnumerator() => new Enumerator(_instances.Values.GetEnumerator());

        /// <summary>딕셔너리 값 struct 열거자를 감싼 인스턴스 열거자.</summary>
        public struct Enumerator
        {
            private Dictionary<long, ItemInstance<TState>>.ValueCollection.Enumerator _inner;

            internal Enumerator(Dictionary<long, ItemInstance<TState>>.ValueCollection.Enumerator inner)
            {
                _inner = inner;
            }

            /// <summary>현재 인스턴스.</summary>
            public ItemInstance<TState> Current => _inner.Current;

            /// <summary>다음 인스턴스로 이동한다.</summary>
            public bool MoveNext() => _inner.MoveNext();
        }
    }
}
