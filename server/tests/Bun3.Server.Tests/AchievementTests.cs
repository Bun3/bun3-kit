using Bun3.Server.Achievements;
using NUnit.Framework;

namespace Bun3.Server.Tests;

[TestFixture]
public class AchievementTests
{
    private sealed class GameDef : AchievementDefinition
    {
        public string RewardTable { get; }

        public GameDef(string id, long target, bool repeatable = false, string rewardTable = "ok")
            : base(id, target, repeatable) => RewardTable = rewardTable;
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

    // ── 비반복 달성 ───────────────────────────────────────────────────────

    [Test]
    public void 비반복_도달_시_1회_달성하고_시각을_기록한다()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var tracker = new AchievementTracker<GameDef>(catalog, utcNowTicks: () => 777);

        Assert.That(tracker.Add(0, 9), Is.EqualTo(0));
        Assert.That(tracker.GetState(0).CompletedCount, Is.EqualTo(0));
        Assert.That(tracker.Add(0, 1), Is.EqualTo(1));

        ref readonly var state = ref tracker.GetState(0);
        Assert.That(state.CompletedCount, Is.EqualTo(1));
        Assert.That(state.LastCompletedAtUtcTicks, Is.EqualTo(777));
    }

    [Test]
    public void 비반복_초과_Add는_클램프되고_재달성하지_않는다()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.Add(0, 100), Is.EqualTo(1));
        Assert.That(tracker.Add(0, 100), Is.EqualTo(0));   // 중복 달성 방지

        ref readonly var state = ref tracker.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(10));
        Assert.That(state.CompletedCount, Is.EqualTo(1));
    }

    // ── 반복 달성 ─────────────────────────────────────────────────────────

    [Test]
    public void 반복_다회_달성과_큰_점프_몰아_발화()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.Add(0, 10), Is.EqualTo(1));
        Assert.That(tracker.Add(0, 35), Is.EqualTo(3));    // 45/10 = 4 → 신규 3
        ref readonly var state = ref tracker.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(45));       // 진행도는 누적 유지
        Assert.That(state.CompletedCount, Is.EqualTo(4));
    }

    [Test]
    public void 반복_오버플로_클램프()
    {
        var catalog = Catalog(new GameDef("inf", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        tracker.Add(0, long.MaxValue - 5);
        tracker.Add(0, long.MaxValue);                     // 클램프, 예외 없음
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(long.MaxValue));
    }

    // ── Set ──────────────────────────────────────────────────────────────

    [Test]
    public void Set_상향은_달성하고_하향은_달성_수를_유지한다()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.Set(0, 25), Is.EqualTo(2));
        Assert.That(tracker.Set(0, 3), Is.EqualTo(0));     // 하향 — 단조 유지
        ref readonly var state = ref tracker.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(3));
        Assert.That(state.CompletedCount, Is.EqualTo(2));
    }

    [Test]
    public void Add_Set_음수는_예외()
    {
        var tracker = new AchievementTracker<GameDef>(Catalog(new GameDef("a", 1)));
        Assert.That(() => tracker.Add(0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => tracker.Set(0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // ── 클레임 ───────────────────────────────────────────────────────────

    [Test]
    public void 클레임은_달성_횟수만큼만_성공한다()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);

        Assert.That(tracker.TryClaim(0), Is.False);        // 미달성
        tracker.Add(0, 30);                                // 달성 3회
        Assert.That(tracker.GetClaimableCount(0), Is.EqualTo(3));
        Assert.That(tracker.TryClaim(0), Is.True);
        Assert.That(tracker.TryClaim(0), Is.True);
        Assert.That(tracker.TryClaim(0), Is.True);
        Assert.That(tracker.TryClaim(0), Is.False);        // 중복 수령 방지
        Assert.That(tracker.GetState(0).ClaimedCount, Is.EqualTo(3));
    }

    // ── Restore ──────────────────────────────────────────────────────────

    [Test]
    public void Restore는_훅과_dirty를_발화하지_않는다()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var dirtyCount = 0;
        var tracker = new AchievementTracker<GameDef>(catalog, () => dirtyCount++);
        tracker.OnCompleted = (_, _, _) => Assert.Fail("Restore가 훅을 발화했습니다");

        tracker.Restore(0, new AchievementState { Progress = 10, CompletedCount = 1, ClaimedCount = 1, LastCompletedAtUtcTicks = 5 });

        Assert.That(dirtyCount, Is.EqualTo(0));
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(10));
    }

    [Test]
    public void Restore_불변식_위반은_예외()
    {
        var tracker = new AchievementTracker<GameDef>(Catalog(new GameDef("a", 10)));

        Assert.That(() => tracker.Restore(0, new AchievementState { Progress = -1 }), Throws.ArgumentException);
        Assert.That(() => tracker.Restore(0, new AchievementState { ClaimedCount = 1, CompletedCount = 0 }), Throws.ArgumentException);
        Assert.That(() => tracker.Restore(0, new AchievementState { Progress = 10, CompletedCount = 2, ClaimedCount = 0 }), Throws.ArgumentException);   // 비반복 다회
    }

    [Test]
    public void Restore_목표_하향_시_비반복_진행도는_클램프되고_Add0으로_재판정한다()
    {
        var catalog = Catalog(new GameDef("kill", 10));    // 저장 당시 목표 100 → 10으로 하향된 상황
        var tracker = new AchievementTracker<GameDef>(catalog);

        tracker.Restore(0, new AchievementState { Progress = 42 });
        Assert.That(tracker.GetState(0).Progress, Is.EqualTo(10));
        Assert.That(tracker.Add(0, 0), Is.EqualTo(1));     // 재평가로 달성 발화
    }

    // ── dirty 연계 ───────────────────────────────────────────────────────

    [Test]
    public void 실제_변경_시에만_onDirty가_호출된다()
    {
        var catalog = Catalog(new GameDef("a", 10), new GameDef("b", 5, repeatable: true));
        var dirtyCount = 0;
        var tracker = new AchievementTracker<GameDef>(catalog, () => dirtyCount++);

        tracker.Add(0, 3);
        Assert.That(dirtyCount, Is.EqualTo(1));
        tracker.Add(0, 0);                                 // 변경 없음
        tracker.Set(0, 3);                                 // 같은 값
        Assert.That(dirtyCount, Is.EqualTo(1));
        tracker.Add(0, 7);                                 // 달성
        Assert.That(dirtyCount, Is.EqualTo(2));
        tracker.Add(0, 5);                                 // 클램프 후 변경 없음
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
            if (index != metaIdx) tracker.Add(metaIdx, count);   // 티어/메타 체인 = 게임 몫
        };

        tracker.Add(0, 10);
        Assert.That(tracker.GetState(metaIdx).Progress, Is.EqualTo(1));
    }

    [Test]
    public void OnCompleted는_인덱스_정의_신규달성수를_받는다()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var tracker = new AchievementTracker<GameDef>(catalog);
        (int index, string id, int count)? seen = null;
        tracker.OnCompleted = (index, def, count) => seen = (index, def.Id, count);

        tracker.Add(0, 25);

        Assert.That(seen, Is.EqualTo((0, "daily", 2)));
    }
}
