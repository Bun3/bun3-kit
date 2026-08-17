using System;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// 플레이어당 1개의 업적 진행/달성/클레임 추적기. 게임 Player 파생 클래스가 소유하며
    /// 플레이어 상태와 같은 전제(세션 액터 안에서만 접근)로 락이 없다. 조건 판정은
    /// 게임 몫이다 — 게임이 자기 이벤트를 인덱스로 라우팅해 <see cref="Add"/>를 부른다.
    /// 핫패스(Add/Set/TryClaim)는 무할당: 배열 인덱싱과 정수 연산, 캐시된 델리게이트
    /// 호출뿐이다. 달성 횟수는 단조 증가라 같은 달성이 두 번 발화하지 않는다.
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
        /// 뒤 호출되므로 훅 안에서 다른 업적에 Add(체인/티어 구성)해도 된다. 자동 보상은
        /// 여기서 <see cref="TryClaim"/> 후 게임이 지급한다.</summary>
        public Action<int, TDef, int>? OnCompleted { get; set; }

        /// <summary>추적 대상 업적 수 (= 카탈로그 정의 수).</summary>
        public int Count => _states.Length;

        /// <summary>추적기를 생성한다. <paramref name="onDirty"/>에 Player의 MarkDirty를
        /// 넘기면 상태가 실제로 변할 때마다 저장 스윕 대상으로 표시된다.
        /// <paramref name="utcNowTicks"/>는 달성 시각원(기본 UTC 현재) — 테스트용 주입점.</summary>
        public AchievementTracker(AchievementCatalog<TDef> catalog, Action? onDirty = null, Func<long>? utcNowTicks = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _states = new AchievementState[catalog.Count];
            _onDirty = onDirty;
            _utcNowTicks = utcNowTicks ?? DefaultClock;
        }

        /// <summary>진행도를 증가시키고 신규 달성 수를 반환한다. amount 0은 진행도 변경
        /// 없이 달성 재평가만 한다(Restore 후 목표 하향 재판정용).</summary>
        /// <exception cref="ArgumentOutOfRangeException">amount가 음수일 때.</exception>
        public int Add(int index, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "진행도 증가량은 음수일 수 없습니다.");

            var def = _catalog.GetDefinition(index);
            var progress = _states[index].Progress;
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

            return ApplyProgress(index, def, newProgress);
        }

        /// <summary>진행도를 설정하고 신규 달성 수를 반환한다. 하향 설정해도 달성 횟수는
        /// 감소하지 않는다(단조 규칙 — 중복 달성 방지의 근거).</summary>
        /// <exception cref="ArgumentOutOfRangeException">value가 음수일 때.</exception>
        public int Set(int index, long value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "진행도는 음수일 수 없습니다.");

            var def = _catalog.GetDefinition(index);
            var newProgress = !def.Repeatable && value > def.Target ? def.Target : value;
            return ApplyProgress(index, def, newProgress);
        }

        private int ApplyProgress(int index, TDef def, long newProgress)
        {
            ref var state = ref _states[index];
            var changed = state.Progress != newProgress;
            state.Progress = newProgress;

            int newCompletions;
            if (def.Repeatable)
            {
                var total = newProgress / def.Target;
                var delta = total - state.CompletedCount;
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

        /// <summary>수령하지 않은 달성이 있으면 수령 횟수를 1 올리고 true. 보상 지급은
        /// 게임이 true 반환 후 수행한다 — 프레임워크는 중복 수령만 막는다.</summary>
        public bool TryClaim(int index)
        {
            ref var state = ref _states[index];
            if (state.ClaimedCount >= state.CompletedCount)
            {
                return false;
            }

            state.ClaimedCount++;
            _onDirty?.Invoke();
            return true;
        }

        /// <summary>수령 가능 횟수 (달성 횟수 − 수령 횟수).</summary>
        public int GetClaimableCount(int index)
        {
            ref readonly var state = ref _states[index];
            return state.CompletedCount - state.ClaimedCount;
        }

        /// <summary>상태를 복사 없이 열람한다 — 저장 직렬화는 게임이 이걸 순회한다.</summary>
        public ref readonly AchievementState GetState(int index) => ref _states[index];

        /// <summary>로드 복원 — 훅과 dirty를 발화하지 않는다. 불변식 위반(음수, 수령 &gt;
        /// 달성, 비반복 다회 달성)은 예외로 저장 데이터 손상을 표면화하고, 비반복 업적의
        /// 초과 진행도만 목표치로 클램프한다(밸런스 패치로 목표 하향 대응).
        /// 달성 횟수가 진행도 대비 모자란 상태를 복원하면 다음 Add/Set에서 차액만큼
        /// 몰아 발화한다(at-least-once — 달성 처리 도중 크래시 복구에 안전한 방향).</summary>
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

        /// <summary>상태 전체(진행도·달성·수령·시각)를 0으로 되감는다 — 일간/주간 사이클
        /// 교체용. 달성 횟수는 단조라 <see cref="Set"/>(0)으로는 재달성이 불가능하므로,
        /// 카운터를 함께 되감는 지점은 여기뿐이다. 변경이 있었으면 dirty 1회, 훅 없음.
        /// 미수령 보상 정산(우편 발송 등)은 게임이 Reset 전에 처리할 것.</summary>
        public void Reset(int index)
        {
            ref var state = ref _states[index];
            if (state.Progress == 0 && state.CompletedCount == 0 && state.ClaimedCount == 0 && state.LastCompletedAtUtcTicks == 0)
            {
                return;
            }

            state = default;
            _onDirty?.Invoke();
        }
    }
}
