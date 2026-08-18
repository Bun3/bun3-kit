using System;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// 플레이어당 1개의 업적 진행/달성/수령/가용성 추적기. 게임 Player 파생 클래스가
    /// 소유하며 플레이어 상태와 같은 전제(세션 액터 안에서만 접근)로 락이 없다.
    /// 조건 판정은 게임 몫이다 — 게임이 자기 이벤트를 인덱스/태그로 라우팅해
    /// <see cref="Increase"/>·<see cref="IncreaseByTag(int, long)"/>를 부른다.
    /// 핫패스는 무할당: 배열 인덱싱과 정수 연산, 캐시된 델리게이트 호출뿐이다.
    /// 달성 횟수는 단조 증가라 같은 달성이 두 번 발화하지 않는다.
    /// 반복 업적은 진행도가 누적되고 수령 시 목표치만큼 차감된다(idlez/growninja 동일 모델)
    /// — 수령 대기 중 UI는 min(Progress, Target)/Target으로 "10/10 [보상받기]"가 된다.
    /// </summary>
    /// <typeparam name="TDef">게임의 업적 정의 타입.</typeparam>
    public sealed class AchievementTracker<TDef> where TDef : AchievementDefinition
    {
        private static readonly Func<long> DefaultClock = () => DateTime.UtcNow.Ticks;

        private readonly AchievementCatalog<TDef> _catalog;
        private readonly AchievementState[] _states;
        private readonly Action? _onDirty;
        private readonly Func<long> _utcNowTicks;

        /// <summary>달성 직후 호출되는 훅 — (인덱스, 정의, 신규 달성 수). 상태 갱신이 끝난
        /// 뒤 호출되므로 훅 안에서 다른 업적을 Increase/Activate(체인·티어 구성)해도 된다.
        /// 자동 보상은 여기서 <see cref="TryClaim"/> 후 게임이 지급한다.</summary>
        public Action<int, TDef, int>? OnCompleted { get; set; }

        /// <summary>추적 대상 업적 수 (= 카탈로그 정의 수).</summary>
        public int Count => _states.Length;

        /// <summary>추적기를 생성한다. 초기 가용성은 정의의 InitialAvailability를 따른다.
        /// <paramref name="onDirty"/>에 Player의 MarkDirty를 넘기면 상태가 실제로 변할
        /// 때마다 저장 스윕 대상으로 표시된다. <paramref name="utcNowTicks"/>는 달성
        /// 시각원(기본 UTC 현재) — 테스트용 주입점.</summary>
        public AchievementTracker(AchievementCatalog<TDef> catalog, Action? onDirty = null, Func<long>? utcNowTicks = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _states = new AchievementState[catalog.Count];
            _onDirty = onDirty;
            _utcNowTicks = utcNowTicks ?? DefaultClock;
            for (var i = 0; i < _states.Length; i++)
            {
                _states[i].Availability = catalog.GetDefinition(i).InitialAvailability;
            }
        }

        // ── 진행 ─────────────────────────────────────────────────────────────

        /// <summary>진행도를 증가시키고 신규 달성 수를 반환한다. Active가 아니면 no-op(0).
        /// amount 0은 진행도 변경 없이 달성 재평가만 한다(Restore 후 목표 하향 재판정용).</summary>
        /// <exception cref="ArgumentOutOfRangeException">amount가 음수일 때.</exception>
        public int Increase(int index, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "진행도 증가량은 음수일 수 없습니다.");

            return IncreaseCore(index, _catalog.GetDefinition(index), amount);
        }

        /// <summary>태그가 붙은 Active 업적 전부의 진행도를 증가시키고 신규 달성 수 합계를
        /// 반환한다. 각 업적은 독립 카운터라 같은 양을 각자 받는다(공유 원장 없음) —
        /// 티어 동시 개방(포함형)이 이걸로 성립한다. 대상은 호출 시점에 스냅샷된다:
        /// 훅에서 Activate된 업적(체인 티어)은 이번 배치를 받지 않고 다음 이벤트부터
        /// 쌓는다(신규 누적 의미론 — idlez의 스냅샷 순회와 동일). 배치 중 Lock된 업적은 스킵.</summary>
        /// <exception cref="ArgumentOutOfRangeException">amount가 음수일 때.</exception>
        public int IncreaseByTag(int tagIndex, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "진행도 증가량은 음수일 수 없습니다.");

            var indices = _catalog.GetIndicesByTag(tagIndex);
            Span<ulong> eligible = stackalloc ulong[(indices.Length + 63) >> 6];   // 스냅샷 — 스택이라 무할당·재진입 안전
            for (var i = 0; i < indices.Length; i++)
            {
                if (_states[indices[i]].Availability == AchievementStatus.Active)
                {
                    eligible[i >> 6] |= 1UL << (i & 63);
                }
            }

            var total = 0;
            for (var i = 0; i < indices.Length; i++)
            {
                if ((eligible[i >> 6] & (1UL << (i & 63))) == 0)
                {
                    continue;
                }

                var index = indices[i];
                total += IncreaseCore(index, _catalog.GetDefinition(index), amount);
            }

            return total;
        }

        /// <summary>필터를 통과한 태그 업적만 증가시킨다 — growninja AchievementCondition의
        /// 조건값 비교 대응. static 람다 + <typeparamref name="TArg"/>로 넘기면 무할당:
        /// <c>IncreaseByTag(tag, 1, level, static (def, lv) =&gt; def.MinLevel &lt;= lv)</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">amount가 음수일 때.</exception>
        public int IncreaseByTag<TArg>(int tagIndex, long amount, TArg arg, Func<TDef, TArg, bool> filter)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "진행도 증가량은 음수일 수 없습니다.");
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var indices = _catalog.GetIndicesByTag(tagIndex);
            Span<ulong> eligible = stackalloc ulong[(indices.Length + 63) >> 6];   // IncreaseByTag와 동일한 스냅샷 의미론
            for (var i = 0; i < indices.Length; i++)
            {
                if (_states[indices[i]].Availability == AchievementStatus.Active && filter(_catalog.GetDefinition(indices[i]), arg))
                {
                    eligible[i >> 6] |= 1UL << (i & 63);
                }
            }

            var total = 0;
            for (var i = 0; i < indices.Length; i++)
            {
                if ((eligible[i >> 6] & (1UL << (i & 63))) == 0)
                {
                    continue;
                }

                var index = indices[i];
                total += IncreaseCore(index, _catalog.GetDefinition(index), amount);
            }

            return total;
        }

        /// <summary>진행도를 설정하고 신규 달성 수를 반환한다. Active가 아니면 no-op(0).
        /// 하향 설정해도 달성 횟수는 감소하지 않는다(단조 규칙 — 중복 달성 방지의 근거).
        /// 용도: 로그인 시 파생값 동기화(레벨·수집 수 역산), 거대 재화의 포화 변환.</summary>
        /// <exception cref="ArgumentOutOfRangeException">value가 음수일 때.</exception>
        public int SetProgress(int index, long value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "진행도는 음수일 수 없습니다.");

            var def = _catalog.GetDefinition(index);
            ref var state = ref _states[index];
            if (state.Availability != AchievementStatus.Active)
            {
                return 0;
            }

            var newProgress = !def.Repeatable && value > def.Target ? def.Target : value;
            return ApplyProgress(index, def, ref state, newProgress);
        }

        private int IncreaseCore(int index, TDef def, long amount)
        {
            ref var state = ref _states[index];
            if (state.Availability != AchievementStatus.Active)
            {
                return 0;
            }

            var progress = state.Progress;
            long newProgress;
            if (def.Repeatable)
            {
                // 오버플로 클램프 — progress ≥ 0, amount ≥ 0 전제라 뺄셈 비교가 안전하다.
                newProgress = amount > long.MaxValue - progress ? long.MaxValue : progress + amount;
            }
            else
            {
                newProgress = amount >= def.Target - progress ? def.Target : progress + amount;
            }

            return ApplyProgress(index, def, ref state, newProgress);
        }

        private int ApplyProgress(int index, TDef def, ref AchievementState state, long newProgress)
        {
            var changed = state.Progress != newProgress;
            state.Progress = newProgress;

            int newCompletions;
            if (def.Repeatable)
            {
                // 불변식: CompletedCount = ClaimedCount + Progress/Target — 수령 차감과 맞물린다.
                var pendingByProgress = newProgress / def.Target;
                var delta = pendingByProgress - (state.CompletedCount - state.ClaimedCount);
                if (delta <= 0)
                {
                    newCompletions = 0;
                }
                else
                {
                    var headroom = int.MaxValue - state.CompletedCount;
                    newCompletions = delta > headroom ? headroom : (int)delta;
                }
            }
            else
            {
                newCompletions = newProgress == def.Target && state.CompletedCount == 0 ? 1 : 0;
            }

            if (newCompletions > 0)
            {
                state.CompletedCount += newCompletions;
                state.LastCompletedAtUtcTicks = _utcNowTicks();
                changed = true;
            }

            if (changed)
            {
                _onDirty?.Invoke();
            }

            if (newCompletions > 0)
            {
                OnCompleted?.Invoke(index, def, newCompletions);
            }

            return newCompletions;
        }

        // ── 수령 ─────────────────────────────────────────────────────────────

        /// <summary>수령하지 않은 달성이 있으면 수령 횟수를 1 올리고 true. 반복 업적은
        /// 진행도에서 목표치를 차감한다(다음 사이클 개시). 가용성과 무관하게 동작한다 —
        /// 로테이션 아웃된 업적의 보상 정산이 가능해야 하므로. 지급은 게임이 true 반환 후
        /// 수행하며, 일괄 수령은 <c>while (TryClaim(i))</c>.</summary>
        public bool TryClaim(int index)
        {
            ref var state = ref _states[index];
            if (state.ClaimedCount >= state.CompletedCount)
            {
                return false;
            }

            state.ClaimedCount++;
            var def = _catalog.GetDefinition(index);
            if (def.Repeatable)
            {
                var remaining = state.Progress - def.Target;
                state.Progress = remaining > 0 ? remaining : 0;   // 느슨한 Restore 대비 바닥 0
            }

            _onDirty?.Invoke();
            return true;
        }

        /// <summary>수령 가능 횟수 (달성 횟수 − 수령 횟수).</summary>
        public int GetClaimableCount(int index)
        {
            ref readonly var state = ref _states[index];
            return state.CompletedCount - state.ClaimedCount;
        }

        // ── 상태 ─────────────────────────────────────────────────────────────

        /// <summary>수명주기 상태를 파생 조회한다. Locked/Ready는 저장값 그대로, Active
        /// 계열은 카운터에서 판정: 수령 가능분 있음 → Completed, 비반복 전량 수령 → Claimed
        /// (종결), 그 외 Active(반복 업적은 수령 후 자동 복귀). 저장-파생 불일치가 원리적으로
        /// 없다 — Completed/Claimed는 어디에도 저장되지 않는다.</summary>
        public AchievementStatus GetStatus(int index)
        {
            ref readonly var state = ref _states[index];
            if (state.Availability != AchievementStatus.Active)
            {
                return state.Availability;
            }
            if (state.CompletedCount > state.ClaimedCount)
            {
                return AchievementStatus.Completed;
            }
            if (state.CompletedCount > 0 && !_catalog.GetDefinition(index).Repeatable)
            {
                return AchievementStatus.Claimed;
            }

            return AchievementStatus.Active;
        }

        /// <summary>Locked → Ready 전이(열림 — 목록 노출, 진행은 아직). 그 외 상태면 false.
        /// "완료/수령 시 자식 열림"은 게임이 OnCompleted나 수령 핸들러에서 호출한다.</summary>
        public bool Unlock(int index)
        {
            ref var state = ref _states[index];
            if (state.Availability != AchievementStatus.Locked)
            {
                return false;
            }

            state.Availability = AchievementStatus.Ready;
            _onDirty?.Invoke();
            return true;
        }

        /// <summary>Locked/Ready → Active 전이(진행 개시). 이미 Active면 false.</summary>
        public bool Activate(int index)
        {
            ref var state = ref _states[index];
            if (state.Availability == AchievementStatus.Active)
            {
                return false;
            }

            state.Availability = AchievementStatus.Active;
            _onDirty?.Invoke();
            return true;
        }

        /// <summary>→ Locked 전이(닫힘 — 로테이션 아웃 등). 카운터는 유지된다(되감기는
        /// <see cref="Reset"/>). 이미 Locked면 false. 수령 대기분의 정산(우편 등)은 게임 몫.</summary>
        public bool Lock(int index)
        {
            ref var state = ref _states[index];
            if (state.Availability == AchievementStatus.Locked)
            {
                return false;
            }

            state.Availability = AchievementStatus.Locked;
            _onDirty?.Invoke();
            return true;
        }

        // ── 저장 연계 ─────────────────────────────────────────────────────────

        /// <summary>상태를 복사 없이 열람한다 — 저장 직렬화는 게임이 이걸 순회한다.</summary>
        public ref readonly AchievementState GetState(int index) => ref _states[index];

        /// <summary>로드 복원 — 훅과 dirty를 발화하지 않는다. 불변식 위반(음수, 수령 &gt;
        /// 달성, 비반복 다회 달성, Availability에 파생 상태 저장)은 예외로 저장 데이터
        /// 손상을 표면화하고, 비반복 업적의 초과 진행도만 목표치로 클램프한다(밸런스
        /// 패치로 목표 하향 대응 — 재판정은 다음 Increase(i, 0)).
        /// 달성 횟수가 진행도 대비 모자란 상태를 복원하면 다음 Increase/SetProgress에서
        /// 차액만큼 몰아 발화한다(at-least-once — 달성 처리 도중 크래시 복구에 안전한 방향).</summary>
        /// <exception cref="ArgumentException">상태가 불변식을 위반할 때.</exception>
        public void Restore(int index, in AchievementState state)
        {
            if (state.Progress < 0 || state.CompletedCount < 0 || state.ClaimedCount < 0 || state.LastCompletedAtUtcTicks < 0)
            {
                throw new ArgumentException("업적 상태에 음수 값이 있습니다.", nameof(state));
            }
            if (state.ClaimedCount > state.CompletedCount)
            {
                throw new ArgumentException("수령 횟수가 달성 횟수를 초과합니다.", nameof(state));
            }
            if ((uint)state.Availability > (uint)AchievementStatus.Active)
            {
                throw new ArgumentException($"Availability에는 Locked/Ready/Active만 저장됩니다 ({state.Availability}) — Completed/Claimed는 파생 상태입니다.", nameof(state));
            }

            var def = _catalog.GetDefinition(index);
            if (!def.Repeatable && state.CompletedCount > 1)
            {
                throw new ArgumentException($"비반복 업적 '{def.Id}'의 달성 횟수가 1을 초과합니다 ({state.CompletedCount}).", nameof(state));
            }

            _states[index] = state;
            if (!def.Repeatable && _states[index].Progress > def.Target)
            {
                _states[index].Progress = def.Target;
            }
        }

        /// <summary>카운터(진행도·달성·수령·시각)를 0으로 되감는다 — 일간/주간 사이클
        /// 교체용. 가용성은 건드리지 않는다(전이는 Unlock/Activate/Lock으로 별도).
        /// 달성 횟수는 단조라 SetProgress(0)으로는 재달성이 불가능하므로, 카운터를 함께
        /// 되감는 지점은 여기뿐이다. 변경이 있었으면 dirty 1회, 훅 없음. 미수령 보상
        /// 정산(우편 발송 등)은 게임이 Reset 전에 처리할 것.</summary>
        public void Reset(int index)
        {
            ref var state = ref _states[index];
            if (state.Progress == 0 && state.CompletedCount == 0 && state.ClaimedCount == 0 && state.LastCompletedAtUtcTicks == 0)
            {
                return;
            }

            state.Progress = 0;
            state.CompletedCount = 0;
            state.ClaimedCount = 0;
            state.LastCompletedAtUtcTicks = 0;
            _onDirty?.Invoke();
        }
    }
}
