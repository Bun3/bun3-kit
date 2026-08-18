using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 인벤토리가 보유하는 아이템 인스턴스. 비스택형 정의는 인스턴스당 수량 1,
    /// 스택형 정의(재화 포함)는 정의당 싱글턴 인스턴스에 수량이 병합된다.
    /// 수량은 <see cref="BigNum"/> — long은 암시 변환되며, BigNum 덧셈은 유효 자릿수
    /// 밖 항을 흡수하는 손실 연산이다(방치형 수량 의미론, 전량 소모는 정확히 Zero).
    /// <see cref="State"/>는 게임 소유의 불투명 상태(레벨·경험치·옵션 등) —
    /// 변경 후 <see cref="MarkChanged"/>를 호출해야 저장/전송 추적에 잡힌다.
    /// </summary>
    /// <typeparam name="TState">게임이 정의하는 인스턴스 상태 타입.</typeparam>
    public sealed class ItemInstance<TState>
    {
        private uint _flags;
        private long _expiresAtTicksUtc;

        internal ItemInstance(
            ItemInventory<TState> owner,
            long instanceId,
            ItemId item,
            BigNum quantity,
            uint flags,
            long expiresAtTicksUtc,
            TState state)
        {
            _owner = owner;
            InstanceId = instanceId;
            Item = item;
            Quantity = quantity;
            _flags = flags;
            _expiresAtTicksUtc = expiresAtTicksUtc;
            State = state;
        }

        internal ItemInventory<TState>? _owner;
        internal bool IsNew;
        internal bool Changed;

        /// <summary>인스턴스 고유 id — 발급 시섬 또는 로드된 외부 권위 id(DB·Steam).</summary>
        public long InstanceId { get; }

        /// <summary>정의 식별자 (불변).</summary>
        public ItemId Item { get; }

        /// <summary>보유 수량. 비스택형은 항상 1. 프레임워크만 변경한다.</summary>
        public BigNum Quantity { get; internal set; }

        /// <summary>상태 비트 플래그 — 의미는 게임/플랫폼 몫(예: 사용 중, 잠금, 거래 불가).
        /// 인벤토리의 removeBlockingFlags 마스크에 걸리면 소모가 차단된다.
        /// setter가 변경 추적에 자동 반영한다.</summary>
        public uint Flags
        {
            get => _flags;
            set
            {
                if (_flags != value)
                {
                    _flags = value;
                    MarkChanged();
                }
            }
        }

        /// <summary>만료 시각(UTC ticks). 0 = 무기한. 연장 규칙(누적/갱신/최대)은 게임이
        /// 이 값을 갱신하는 방식으로 정한다. 프레임워크는 시계를 모르므로 만료 판정·처리는
        /// <see cref="ItemInventory{TState}.CollectExpired"/>에 게임이 현재 시각을 주입해
        /// 수행한다(만료 ≠ 자동 삭제 — idlez 의미론). setter가 변경 추적에 자동 반영한다.</summary>
        public long ExpiresAtTicksUtc
        {
            get => _expiresAtTicksUtc;
            set
            {
                if (_expiresAtTicksUtc != value)
                {
                    _expiresAtTicksUtc = value;
                    MarkChanged();
                }
            }
        }

        /// <summary>게임 소유 상태 — 프레임워크는 해석하지 않는다.</summary>
        public TState State { get; }

        /// <summary><see cref="State"/> 변경 후 호출 — 변경 추적(Updated)과
        /// 인벤토리 onChanged(저장 주기)에 반영된다. 인벤토리에서 제거된 뒤에는 무시된다.</summary>
        public void MarkChanged()
        {
            Changed = true;
            _owner?.OnInstanceChanged();
        }
    }
}
