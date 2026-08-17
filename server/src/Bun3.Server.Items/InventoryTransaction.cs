using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    /// <summary>
    /// 인벤토리 트랜잭션 빌더 — 정의 단위(<see cref="Add"/>/<see cref="Remove"/>)와
    /// 인스턴스 지정(<see cref="RemoveInstance(long)"/>) 연산을 섞어 쌓고
    /// <see cref="Commit"/>에서 전부-아니면-전무로 적용한다.
    /// 인벤토리가 소유한 스크래치를 재사용하므로 무할당이며, 동시 배치는 1개 —
    /// 새 <see cref="ItemInventory{TState}.BeginTransaction"/>은 이전 미커밋 배치를
    /// 버리고, 낡은 빌더나 커밋 후 재사용은 던진다.
    /// </summary>
    /// <typeparam name="TState">게임이 정의하는 인스턴스 상태 타입.</typeparam>
    public readonly struct InventoryTransaction<TState>
    {
        private readonly ItemInventory<TState> _inventory;
        private readonly int _token;

        internal InventoryTransaction(ItemInventory<TState> inventory, int token)
        {
            _inventory = inventory;
            _token = token;
        }

        /// <summary>지급을 쌓는다. amount는 양수(비스택형은 정수·연산 상한 이하).</summary>
        public void Add(ItemId item, BigNum amount) =>
            _inventory.TxRecord(_token, new ItemInventory<TState>.TxOp(
                ItemInventory<TState>.TxOpKind.Add, item, 0, amount));

        /// <summary>정의 단위 소모를 쌓는다. 배치 내 지정 인스턴스는 후보에서 제외된다.</summary>
        public void Remove(ItemId item, BigNum amount) =>
            _inventory.TxRecord(_token, new ItemInventory<TState>.TxOp(
                ItemInventory<TState>.TxOpKind.RemoveByItem, item, 0, amount));

        /// <summary>인스턴스 전량 소모를 쌓는다 — 비스택형은 인스턴스 파괴,
        /// 스택 싱글턴은 잔량 전부.</summary>
        public void RemoveInstance(long instanceId) =>
            _inventory.TxRecord(_token, new ItemInventory<TState>.TxOp(
                ItemInventory<TState>.TxOpKind.RemoveInstanceAll, ItemId.None, instanceId, BigNum.Zero));

        /// <summary>인스턴스 수량 지정 소모를 쌓는다 — 스택 싱글턴 부분 소모용,
        /// 비스택형은 1만 허용.</summary>
        public void RemoveInstance(long instanceId, BigNum amount) =>
            _inventory.TxRecord(_token, new ItemInventory<TState>.TxOp(
                ItemInventory<TState>.TxOpKind.RemoveInstanceAmount, ItemId.None, instanceId, amount));

        /// <summary>배치를 전부-아니면-전무로 적용한다. 실패 시 인벤토리는 완전 무변경이고
        /// <paramref name="failedIndex"/>가 원인 연산 순번을 가리킨다. 생성된 인스턴스는
        /// <paramref name="created"/>에 담긴다. 커밋 후 이 빌더는 무효다.</summary>
        public InventoryError Commit(out int failedIndex, List<ItemInstance<TState>>? created = null) =>
            _inventory.TxCommit(_token, out failedIndex, created);
    }
}
