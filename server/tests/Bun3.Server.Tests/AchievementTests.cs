using Bun3.Server.Achievements;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class AchievementTests
{
    private sealed class GameDef : AchievementDefinition
    {
        public string RewardTable { get; }
        public int MinGrade { get; }

        public GameDef(string id, long target, bool repeatable = false,
            AchievementStatus initialAvailability = AchievementStatus.Active,
            string[]? tags = null, string rewardTable = "ok", int minGrade = 0)
            : base(id, target, repeatable, initialAvailability, tags)
        {
            RewardTable = rewardTable;
            MinGrade = minGrade;
        }
    }

    private static AchievementCatalog<GameDef> Catalog(params GameDef[] defs) => new(defs);

    // ── 카탈로그 ──────────────────────────────────────────────────────────

    [Test]
    public void Catalog_인터닝_인덱스와_정의_조회()
    {
        var catalog = Catalog(new GameDef("a", 10), new GameDef("b", 5, repeatable: true));

        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.GetIndex("b"), Is.EqualTo(1));
        Assert.That(catalog.TryGetIndex("a", out var idx), Is.True);
        Assert.That(idx, Is.EqualTo(0));
        Assert.That(catalog.TryGetIndex("없음", out _), Is.False);
        Assert.That(() => catalog.GetIndex("없음"), Throws.TypeOf<KeyNotFoundException>());
        Assert.That(catalog.GetDefinition(1).Id, Is.EqualTo("b"));
    }

    [TestCase("", 10)]
    [TestCase("dup", 0)]
    [TestCase("dup", -1)]
    public void Catalog_빈_id_또는_비양수_Target은_예외(string id, long target)
    {
        Assert.That(() => Catalog(new GameDef(id, target)), Throws.ArgumentException);
    }

    [Test]
    public void Catalog_중복_id는_예외()
    {
        Assert.That(() => Catalog(new GameDef("x", 1), new GameDef("x", 2)), Throws.ArgumentException);
    }

    [Test]
    public void Catalog_null_정의는_예외()
    {
        Assert.That(() => new AchievementCatalog<GameDef>(new GameDef[] { null! }), Throws.ArgumentException);
    }

    [Test]
    public void Catalog_상한_초과는_예외()
    {
        var defs = new GameDef[AchievementCatalog<GameDef>.MaxDefinitions + 1];
        for (var i = 0; i < defs.Length; i++) defs[i] = new GameDef($"a{i}", 1);
        Assert.That(() => new AchievementCatalog<GameDef>(defs), Throws.ArgumentException);
    }

    [Test]
    public void Catalog_게임_validator_예외가_전파된다()
    {
        Assert.That(
            () => new AchievementCatalog<GameDef>(
                new[] { new GameDef("a", 1, rewardTable: "") },
                def => { if (def.RewardTable.Length == 0) throw new InvalidOperationException("보상 없음"); }),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Catalog_초기_가용성에_파생_상태는_예외()
    {
        Assert.That(() => Catalog(new GameDef("a", 1, initialAvailability: AchievementStatus.Completed)),
            Throws.ArgumentException);
    }

    [Test]
    public void Catalog_태그_인터닝과_그룹_조회()
    {
        var catalog = Catalog(
            new GameDef("kill_10", 10, tags: new[] { "KILL", "DAILY" }),
            new GameDef("kill_100", 100, tags: new[] { "KILL" }),
            new GameDef("login", 1));

        Assert.That(catalog.TagCount, Is.EqualTo(2));
        Assert.That(catalog.GetIndicesByTag(catalog.GetTagIndex("KILL")).ToArray(), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(catalog.GetIndicesByTag(catalog.GetTagIndex("DAILY")).ToArray(), Is.EqualTo(new[] { 0 }));
        Assert.That(catalog.TryGetTagIndex("없는태그", out _), Is.False);
        Assert.That(() => catalog.GetTagIndex("없는태그"), Throws.TypeOf<KeyNotFoundException>());
    }

    [Test]
    public void Catalog_빈_태그와_같은_정의_내_중복_태그는_예외()
    {
        Assert.That(() => Catalog(new GameDef("a", 1, tags: new[] { "" })), Throws.ArgumentException);
        Assert.That(() => Catalog(new GameDef("a", 1, tags: new[] { "T", "T" })), Throws.ArgumentException);
    }

    // ── 비반복 달성 ───────────────────────────────────────────────────────

    [Test]
    public void 비반복_도달_시_1회_달성하고_시각을_기록한다()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var tracker = new AchievementTracker<GameDef>(catalog, utcNowTicks: () => 777);

        Assert.That(tracker.Increase(0, 9), Is.EqualTo(0));
        Assert.That(tracker.GetStatus(0), Is.EqualTo(AchievementStatus.Active));
        Assert.That(tracker.Increase(0, 1), Is.EqualTo(1));

        ref readonly var state = ref tracker.GetState(0);
        Assert.That(state.CompletedCount, Is.EqualTo(1));
        Assert.That(state.LastCompletedAtUtcTicks, Is.EqualTo(777));
        Assert.That(tracker.GetStatus(0), Is.EqualTo(AchievementStatus.Completed));
    }

    [Test]
    public void 비반복_초과_Increase는_클램프되고_재달성하지_않는다()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.Increase(0, 100), Is.EqualTo(1));
        Assert.That(tracker.Increase(0, 100), Is.EqualTo(0));   // 중복 달성 방지

        ref readonly var state = ref tracker.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(10));
        Assert.That(state.CompletedCount, Is.EqualTo(1));
    }

    [Test]
    public void 비반복_수령_후_Claimed_종결_상태가_된다()
    {
        var tracker = new AchievementTracker<GameDef>(Catalog(new GameDef("kill", 10)));

        tracker.Increase(0, 10);
        Assert.That(tracker.TryClaim(0), Is.True);
        Assert.That(tracker.GetStatus(0), Is.EqualTo(AchievementStatus.Claimed));
        Assert.That(tracker.TryClaim(0), Is.False);             // 중복 수령 방지
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(10));   // 비반복은 차감 없음
    }

    // ── 반복 달성 — 누적 + 수령 시 차감 ──────────────────────────────────

    [Test]
    public void 반복_다회_달성과_큰_점프_몰아_발화()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.Increase(0, 10), Is.EqualTo(1));
        Assert.That(tracker.Increase(0, 35), Is.EqualTo(3));    // 45/10 = 대기 4 → 신규 3
        ref readonly var state = ref tracker.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(45));            // 수령 전엔 누적 유지
        Assert.That(state.CompletedCount, Is.EqualTo(4));
    }

    [Test]
    public void 반복_수령이_목표치를_차감하고_10에_10_UI가_성립한다()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        tracker.Increase(0, 25);                                 // 달성 2, 대기 2
        Assert.That(tracker.GetStatus(0), Is.EqualTo(AchievementStatus.Completed));
        Assert.That(Math.Min(tracker.GetState(0).Progress, 10), Is.EqualTo(10));   // "10/10 [보상받기]"

        Assert.That(tracker.TryClaim(0), Is.True);
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(15));
        Assert.That(tracker.GetStatus(0), Is.EqualTo(AchievementStatus.Completed));   // 대기 1건 남음

        Assert.That(tracker.TryClaim(0), Is.True);
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(5));
        Assert.That(tracker.GetStatus(0), Is.EqualTo(AchievementStatus.Active));      // 다음 사이클 5/10

        Assert.That(tracker.Increase(0, 5), Is.EqualTo(1));      // 불변식 유지 확인 — 3번째 달성
    }

    [Test]
    public void 반복_오버플로_클램프()
    {
        var catalog = Catalog(new GameDef("inf", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        tracker.Increase(0, long.MaxValue - 5);
        tracker.Increase(0, long.MaxValue);                      // 클램프, 예외 없음
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(long.MaxValue));
    }

    // ── SetProgress ──────────────────────────────────────────────────────

    [Test]
    public void SetProgress_상향은_달성하고_하향은_달성_수를_유지한다()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.SetProgress(0, 25), Is.EqualTo(2));
        Assert.That(tracker.SetProgress(0, 3), Is.EqualTo(0));   // 하향 — 단조 유지
        ref readonly var state = ref tracker.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(3));
        Assert.That(state.CompletedCount, Is.EqualTo(2));
    }

    [Test]
    public void 비반복_SetProgress_포화_변환은_목표치에_클램프된다()
    {
        var tracker = new AchievementTracker<GameDef>(Catalog(new GameDef("gold", 1_000)));

        Assert.That(tracker.SetProgress(0, long.MaxValue), Is.EqualTo(1));   // 거대 재화 포화 시나리오
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(1_000));
    }

    [Test]
    public void Increase_SetProgress_음수는_예외()
    {
        var tracker = new AchievementTracker<GameDef>(Catalog(new GameDef("a", 1, tags: new[] { "T" })));
        Assert.That(() => tracker.Increase(0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => tracker.SetProgress(0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => tracker.IncreaseByTag(0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // ── 가용성 ───────────────────────────────────────────────────────────

    [Test]
    public void 초기_가용성은_정의를_따르고_Locked_Ready는_진행을_받지_않는다()
    {
        var catalog = Catalog(
            new GameDef("locked", 10, initialAvailability: AchievementStatus.Locked),
            new GameDef("ready", 10, initialAvailability: AchievementStatus.Ready));
        var dirtyCount = 0;
        var tracker = new AchievementTracker<GameDef>(catalog, () => dirtyCount++);

        Assert.That(tracker.GetStatus(0), Is.EqualTo(AchievementStatus.Locked));
        Assert.That(tracker.GetStatus(1), Is.EqualTo(AchievementStatus.Ready));
        Assert.That(tracker.Increase(0, 10), Is.EqualTo(0));     // no-op
        Assert.That(tracker.SetProgress(1, 10), Is.EqualTo(0));
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(0));
        Assert.That(dirtyCount, Is.EqualTo(0));
    }

    [Test]
    public void Unlock_Activate_Lock_전이와_dirty()
    {
        var catalog = Catalog(new GameDef("a", 10, initialAvailability: AchievementStatus.Locked));
        var dirtyCount = 0;
        var tracker = new AchievementTracker<GameDef>(catalog, () => dirtyCount++);

        Assert.That(tracker.Unlock(0), Is.True);                 // Locked → Ready
        Assert.That(tracker.Unlock(0), Is.False);                // Ready에서 재호출 no-op
        Assert.That(tracker.Activate(0), Is.True);               // Ready → Active
        Assert.That(tracker.Activate(0), Is.False);
        Assert.That(tracker.Increase(0, 10), Is.EqualTo(1));     // 이제 진행됨
        Assert.That(tracker.Lock(0), Is.True);                   // 로테이션 아웃 — 카운터 유지
        Assert.That(tracker.GetState(0).CompletedCount, Is.EqualTo(1));
        Assert.That(dirtyCount, Is.EqualTo(4));                  // Unlock+Activate+달성+Lock
    }

    [Test]
    public void Locked_상태에서도_수령_정산은_가능하다()
    {
        var tracker = new AchievementTracker<GameDef>(Catalog(new GameDef("weekly", 10)));

        tracker.Increase(0, 10);
        tracker.Lock(0);                                         // 주간 교체로 닫힘
        Assert.That(tracker.TryClaim(0), Is.True);               // 미수령 보상 정산은 허용
    }

    // ── 태그 라우팅 ──────────────────────────────────────────────────────

    [Test]
    public void IncreaseByTag는_Active_업적에만_각각_적용된다()
    {
        var catalog = Catalog(
            new GameDef("kill_10", 10, tags: new[] { "KILL" }),
            new GameDef("kill_100", 100, tags: new[] { "KILL" }),
            new GameDef("kill_locked", 10, initialAvailability: AchievementStatus.Locked, tags: new[] { "KILL" }));
        var tracker = new AchievementTracker<GameDef>(catalog);
        var kill = catalog.GetTagIndex("KILL");

        Assert.That(tracker.IncreaseByTag(kill, 10), Is.EqualTo(1));   // kill_10만 달성
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(10));
        Assert.That(tracker.GetState(1).Progress, Is.EqualTo(10));     // 독립 카운터로 같은 양
        Assert.That(tracker.GetState(2).Progress, Is.EqualTo(0));      // Locked 스킵
    }

    [Test]
    public void IncreaseByTag_필터_오버로드는_조건값_게이트로_동작한다()
    {
        var catalog = Catalog(
            new GameDef("grade1", 1, tags: new[] { "GOT" }, minGrade: 1),
            new GameDef("grade5", 1, tags: new[] { "GOT" }, minGrade: 5));
        var tracker = new AchievementTracker<GameDef>(catalog);
        var got = catalog.GetTagIndex("GOT");

        Assert.That(tracker.IncreaseByTag(got, 1, 3, static (def, grade) => def.MinGrade <= grade), Is.EqualTo(1));
        Assert.That(tracker.GetState(0).CompletedCount, Is.EqualTo(1));
        Assert.That(tracker.GetState(1).Progress, Is.EqualTo(0));      // 필터 미통과
    }

    [Test]
    public void 포함형_티어는_동시_Active로_한_번에_전부_진행된다()
    {
        var catalog = Catalog(
            new GameDef("ruby_1", 1, tags: new[] { "RUBY" }),
            new GameDef("ruby_10", 10, tags: new[] { "RUBY" }),
            new GameDef("ruby_100", 100, tags: new[] { "RUBY" }));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.IncreaseByTag(catalog.GetTagIndex("RUBY"), 100), Is.EqualTo(3));   // 100개 한 방에 3티어 전부
    }

    [Test]
    public void 신규누적형_티어는_체인_Activate로_0부터_시작한다()
    {
        var catalog = Catalog(
            new GameDef("kill_1", 1, tags: new[] { "KILL" }),
            new GameDef("kill_10", 10, initialAvailability: AchievementStatus.Locked, tags: new[] { "KILL" }));
        var tracker = new AchievementTracker<GameDef>(catalog);
        var next = catalog.GetIndex("kill_10");
        tracker.OnCompleted = (index, _, _) => { if (index == 0) tracker.Activate(next); };

        tracker.IncreaseByTag(catalog.GetTagIndex("KILL"), 1000);      // 대량 이벤트

        Assert.That(tracker.GetStatus(0), Is.EqualTo(AchievementStatus.Completed));
        Assert.That(tracker.GetStatus(next), Is.EqualTo(AchievementStatus.Active));
        Assert.That(tracker.GetState(next).Progress, Is.EqualTo(0));   // 이월 없이 새로 쌓기
    }

    // ── 수령 ─────────────────────────────────────────────────────────────

    [Test]
    public void 클레임은_달성_횟수만큼만_성공한다()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.TryClaim(0), Is.False);              // 미달성
        tracker.Increase(0, 30);                                 // 달성 3회
        Assert.That(tracker.GetClaimableCount(0), Is.EqualTo(3));
        var claimed = 0;
        while (tracker.TryClaim(0)) claimed++;                   // 일괄 수령 패턴
        Assert.That(claimed, Is.EqualTo(3));
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(0));
    }

    // ── Reset (일간/주간 사이클) ─────────────────────────────────────────

    [Test]
    public void Reset_후_반복_업적이_재달성된다()
    {
        var catalog = Catalog(new GameDef("daily_kill", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.Increase(0, 10), Is.EqualTo(1));     // 1일차 달성
        tracker.TryClaim(0);
        tracker.Reset(0);                                        // 자정 리셋

        ref readonly var state = ref tracker.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(0));
        Assert.That(state.CompletedCount, Is.EqualTo(0));
        Assert.That(state.Availability, Is.EqualTo(AchievementStatus.Active));   // 가용성은 유지
        Assert.That(tracker.Increase(0, 10), Is.EqualTo(1));     // 2일차 재달성
    }

    [Test]
    public void Reset_후_비반복_업적도_재달성된다()
    {
        var tracker = new AchievementTracker<GameDef>(Catalog(new GameDef("daily_once", 5)));

        tracker.Increase(0, 5);
        tracker.Reset(0);
        Assert.That(tracker.Increase(0, 5), Is.EqualTo(1));
    }

    [Test]
    public void Reset은_변경_시에만_dirty이고_훅을_발화하지_않는다()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var dirtyCount = 0;
        var tracker = new AchievementTracker<GameDef>(catalog, () => dirtyCount++);

        tracker.Reset(0);                                        // 이미 0 — 변경 없음
        Assert.That(dirtyCount, Is.EqualTo(0));

        tracker.Increase(0, 10);
        dirtyCount = 0;
        tracker.OnCompleted = (_, _, _) => Assert.Fail("Reset이 훅을 발화했습니다");
        tracker.Reset(0);
        Assert.That(dirtyCount, Is.EqualTo(1));
    }

    // ── Restore ──────────────────────────────────────────────────────────

    [Test]
    public void Restore는_훅과_dirty를_발화하지_않는다()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var dirtyCount = 0;
        var tracker = new AchievementTracker<GameDef>(catalog, () => dirtyCount++);
        tracker.OnCompleted = (_, _, _) => Assert.Fail("Restore가 훅을 발화했습니다");

        tracker.Restore(0, new AchievementState
        {
            Progress = 10, CompletedCount = 1, ClaimedCount = 1, LastCompletedAtUtcTicks = 5,
            Availability = AchievementStatus.Active,
        });

        Assert.That(dirtyCount, Is.EqualTo(0));
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(10));
        Assert.That(tracker.GetStatus(0), Is.EqualTo(AchievementStatus.Claimed));
    }

    [Test]
    public void Restore_불변식_위반은_예외()
    {
        var tracker = new AchievementTracker<GameDef>(Catalog(new GameDef("a", 10)));

        Assert.That(() => tracker.Restore(0, new AchievementState { Progress = -1 }), Throws.ArgumentException);
        Assert.That(() => tracker.Restore(0, new AchievementState { ClaimedCount = 1, CompletedCount = 0 }), Throws.ArgumentException);
        Assert.That(() => tracker.Restore(0, new AchievementState { Progress = 10, CompletedCount = 2 }), Throws.ArgumentException);   // 비반복 다회
        Assert.That(() => tracker.Restore(0, new AchievementState { Availability = AchievementStatus.Completed }), Throws.ArgumentException);   // 파생 상태 저장 금지
    }

    [Test]
    public void Restore_목표_하향_시_비반복_진행도는_클램프되고_Increase0으로_재판정한다()
    {
        var catalog = Catalog(new GameDef("kill", 10));          // 저장 당시 목표 100 → 10으로 하향된 상황
        var tracker = new AchievementTracker<GameDef>(catalog);

        tracker.Restore(0, new AchievementState { Progress = 42, Availability = AchievementStatus.Active });
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(10));
        Assert.That(tracker.Increase(0, 0), Is.EqualTo(1));      // 재평가로 달성 발화
    }

    // ── dirty 연계 ───────────────────────────────────────────────────────

    [Test]
    public void 실제_변경_시에만_onDirty가_호출된다()
    {
        var catalog = Catalog(new GameDef("a", 10));
        var dirtyCount = 0;
        var tracker = new AchievementTracker<GameDef>(catalog, () => dirtyCount++);

        tracker.Increase(0, 3);
        Assert.That(dirtyCount, Is.EqualTo(1));
        tracker.Increase(0, 0);                                  // 변경 없음
        tracker.SetProgress(0, 3);                               // 같은 값
        Assert.That(dirtyCount, Is.EqualTo(1));
        tracker.Increase(0, 7);                                  // 달성
        Assert.That(dirtyCount, Is.EqualTo(2));
        tracker.Increase(0, 5);                                  // 클램프 후 변경 없음
        Assert.That(dirtyCount, Is.EqualTo(2));
        tracker.TryClaim(0);
        Assert.That(dirtyCount, Is.EqualTo(3));
    }

    // ── OnCompleted 체인 ─────────────────────────────────────────────────

    [Test]
    public void OnCompleted_훅에서_다른_업적을_진행할_수_있다()
    {
        var catalog = Catalog(new GameDef("tier1", 10), new GameDef("meta", 2));
        var tracker = new AchievementTracker<GameDef>(catalog);
        var metaIdx = catalog.GetIndex("meta");
        tracker.OnCompleted = (index, def, count) =>
        {
            if (index != metaIdx) tracker.Increase(metaIdx, count);   // 메타 체인 = 게임 몫
        };

        tracker.Increase(0, 10);
        Assert.That(tracker.GetState(metaIdx).Progress, Is.EqualTo(1));
    }

    [Test]
    public void OnCompleted는_인덱스_정의_신규달성수를_받는다()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);
        (int index, string id, int count)? seen = null;
        tracker.OnCompleted = (index, def, count) => seen = (index, def.Id, count);

        tracker.Increase(0, 25);

        Assert.That(seen, Is.EqualTo((0, "daily", 2)));
    }
}
