using System;
using System.Collections.Generic;

namespace Bun3.Server.Items
{
    /// <summary>보상 테이블 항목 — 가중치와 수량 롤 범위.</summary>
    public readonly struct RewardEntry
    {
        /// <summary>항목을 만든다. 데이터 오류는 즉시 던진다(기동 검증).</summary>
        /// <param name="item">지급 아이템.</param>
        /// <param name="weight">가중 추첨 가중치(0 이상 — 0은 grantAll 그룹에서만 의미).</param>
        /// <param name="minAmount">수량 롤 하한(양수).</param>
        /// <param name="maxAmount">수량 롤 상한(하한 이상). 하한과 같으면 고정 수량.</param>
        public RewardEntry(ItemId item, int weight, long minAmount, long maxAmount)
        {
            if (item.IsNone)
            {
                throw new ArgumentException("보상 항목의 아이템이 None입니다.", nameof(item));
            }

            if (weight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "가중치는 0 이상이어야 합니다.");
            }

            if (minAmount <= 0 || maxAmount < minAmount)
            {
                throw new ArgumentOutOfRangeException(nameof(minAmount), $"{minAmount}~{maxAmount}", "수량 범위가 잘못됐습니다.");
            }

            Item = item;
            Weight = weight;
            MinAmount = minAmount;
            MaxAmount = maxAmount;
        }

        /// <summary>지급 아이템.</summary>
        public ItemId Item { get; }

        /// <summary>가중 추첨 가중치.</summary>
        public int Weight { get; }

        /// <summary>수량 롤 하한.</summary>
        public long MinAmount { get; }

        /// <summary>수량 롤 상한.</summary>
        public long MaxAmount { get; }
    }

    /// <summary>보상 그룹 — 발동 확률(만분율)과 지급 방식(전체/가중 1건).</summary>
    public sealed class RewardGroup
    {
        private readonly RewardEntry[] _entries;

        /// <summary>그룹을 만든다. 데이터 오류는 즉시 던진다(기동 검증).</summary>
        /// <param name="probabilityPermyriad">발동 확률 만분율(0~10000). 10000=확정 —
        /// 0과 10000은 추첨 시 난수를 소모하지 않는다(결정론 시뮬레이션 고려).</param>
        /// <param name="grantAll">true면 전 항목 지급, false면 가중 추첨 1건.</param>
        /// <param name="entries">항목들. 가중 추첨 그룹은 가중치 합이 양수여야 한다.</param>
        public RewardGroup(int probabilityPermyriad, bool grantAll, params RewardEntry[] entries)
        {
            if (probabilityPermyriad < 0 || probabilityPermyriad > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(probabilityPermyriad), probabilityPermyriad, "만분율은 0~10000입니다.");
            }

            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            long totalWeight = 0;
            foreach (var entry in entries)
            {
                totalWeight += entry.Weight;
            }

            if (!grantAll && totalWeight <= 0)
            {
                throw new ArgumentException("가중 추첨 그룹은 가중치 합이 양수여야 합니다.", nameof(entries));
            }

            ProbabilityPermyriad = probabilityPermyriad;
            GrantAll = grantAll;
            _entries = (RewardEntry[])entries.Clone();
            TotalWeight = totalWeight;
        }

        /// <summary>발동 확률 만분율.</summary>
        public int ProbabilityPermyriad { get; }

        /// <summary>전 항목 지급 여부(false = 가중 추첨 1건).</summary>
        public bool GrantAll { get; }

        internal long TotalWeight { get; }

        /// <summary>항목 목록.</summary>
        public ReadOnlySpan<RewardEntry> Entries => _entries;
    }

    /// <summary>
    /// 확률 보상 테이블 — idlez AddItemGroup·growninja AddItem·Steam generator의 공통
    /// 코어(그룹 발동 확률 → 전체/가중 1건 → 수량 롤). 게이트(레벨 요건·배타 목록)·
    /// 우편 폴백·천장은 게임 몫 — 게임이 상황별 테이블을 만들거나 결과를 후처리한다.
    /// <see cref="Sample"/>은 미리보기·우편용, 지급은
    /// <see cref="ItemInventory{TState}.TryGrant"/>가 샘플→원자 지급을 1호출로 묶는다.
    /// </summary>
    public sealed class RewardTable
    {
        private readonly RewardGroup[] _groups;

        /// <summary>테이블을 만든다(배열 복사 — 기동 시 1회).</summary>
        public RewardTable(RewardGroup[] groups)
        {
            if (groups == null)
            {
                throw new ArgumentNullException(nameof(groups));
            }

            _groups = (RewardGroup[])groups.Clone();
        }

        /// <summary>그룹 목록.</summary>
        public ReadOnlySpan<RewardGroup> Groups => _groups;

        /// <summary>테이블을 추첨해 양수 델타를 버퍼에 이어 담는다(비우지 않음).
        /// 발동 실패 그룹은 아무것도 담지 않는다.</summary>
        public void Sample(IRandomSource rng, List<ItemDelta> buffer)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            foreach (var group in _groups)
            {
                if (group.ProbabilityPermyriad == 0)
                {
                    continue;
                }

                if (group.ProbabilityPermyriad < 10000 && rng.Next(10000) >= group.ProbabilityPermyriad)
                {
                    continue;
                }

                var entries = group.Entries;
                if (group.GrantAll)
                {
                    for (var i = 0; i < entries.Length; i++)
                    {
                        buffer.Add(new ItemDelta(entries[i].Item, RollAmount(entries[i], rng)));
                    }

                    continue;
                }

                var roll = rng.Next(group.TotalWeight);
                for (var i = 0; i < entries.Length; i++)
                {
                    roll -= entries[i].Weight;
                    if (roll < 0)
                    {
                        buffer.Add(new ItemDelta(entries[i].Item, RollAmount(entries[i], rng)));
                        break;
                    }
                }
            }
        }

        private static long RollAmount(in RewardEntry entry, IRandomSource rng) =>
            entry.MinAmount == entry.MaxAmount
                ? entry.MinAmount
                : entry.MinAmount + rng.Next(entry.MaxAmount - entry.MinAmount + 1);
    }
}
