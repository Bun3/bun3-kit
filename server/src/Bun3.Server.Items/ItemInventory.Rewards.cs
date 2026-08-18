using System.Collections.Generic;

namespace Bun3.Server.Items
{
    // 보상 테이블 지급 — 샘플과 원자 지급을 1호출로 묶는다.
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>
        /// 보상 테이블을 추첨해 결과를 전부-아니면-전무로 지급한다.
        /// <paramref name="failedIndex"/>는 추첨된 델타 순번(상한 초과 등으로 실패 시
        /// 인벤토리는 완전 무변경 — 부분 지급이 필요하면 <see cref="RewardTable.Sample"/>로
        /// 추첨 후 <see cref="TryAddUpTo"/>를 항목별로 쓰고 잔여분은 우편 등 게임 폴백으로).
        /// </summary>
        public InventoryError TryGrant(
            RewardTable table,
            IRandomSource rng,
            out int failedIndex,
            List<ItemInstance<TState>>? created = null)
        {
            failedIndex = -1;
            if (table == null)
            {
                throw new System.ArgumentNullException(nameof(table));
            }

            _rewardScratch.Clear();
            table.Sample(rng, _rewardScratch);

            _applyOps.Clear();
            for (var i = 0; i < _rewardScratch.Count; i++)
            {
                _applyOps.Add(new TxOp(TxOpKind.Add, _rewardScratch[i].Item, 0, _rewardScratch[i].Amount));
            }

            return CommitOps(_applyOps, out failedIndex, created);
        }
    }
}
