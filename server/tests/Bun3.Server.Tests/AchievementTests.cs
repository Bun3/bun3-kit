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

    // ── Catalog ──────────────────────────────────────────────────────────

    [Test]
    public void Catalog_interning_index_and_definition_lookup()
    {
        var catalog = Catalog(new GameDef("a", 10), new GameDef("b", 5, repeatable: true));

        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.GetIndex("b"), Is.EqualTo(1));
        Assert.That(catalog.TryGetIndex("a", out var idx), Is.True);
        Assert.That(idx, Is.EqualTo(0));
        Assert.That(catalog.TryGetIndex("missing", out _), Is.False);
        Assert.That(() => catalog.GetIndex("missing"), Throws.TypeOf<KeyNotFoundException>());
        Assert.That(catalog.GetDefinition(1).Id, Is.EqualTo("b"));
    }

    [TestCase("", 10)]
    [TestCase("dup", 0)]
    [TestCase("dup", -1)]
    public void Catalog_empty_id_or_nonpositive_target_throws(string id, long target)
    {
        Assert.That(() => Catalog(new GameDef(id, target)), Throws.ArgumentException);
    }

    [Test]
    public void Catalog_duplicate_id_throws()
    {
        Assert.That(() => Catalog(new GameDef("x", 1), new GameDef("x", 2)), Throws.ArgumentException);
    }

    [Test]
    public void Catalog_null_definition_throws()
    {
        Assert.That(() => new AchievementCatalog<GameDef>(new GameDef[] { null! }), Throws.ArgumentException);
    }

    [Test]
    public void Catalog_exceeding_max_definitions_throws()
    {
        var defs = new GameDef[AchievementCatalog<GameDef>.MaxDefinitions + 1];
        for (var i = 0; i < defs.Length; i++) defs[i] = new GameDef($"a{i}", 1);
        Assert.That(() => new AchievementCatalog<GameDef>(defs), Throws.ArgumentException);
    }

    [Test]
    public void Catalog_game_validator_exception_propagates()
    {
        Assert.That(
            () => new AchievementCatalog<GameDef>(
                new[] { new GameDef("a", 1, rewardTable: "") },
                def => { if (def.RewardTable.Length == 0) throw new InvalidOperationException("no reward"); }),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Catalog_derived_status_as_initial_availability_throws()
    {
        Assert.That(() => Catalog(new GameDef("a", 1, initialAvailability: AchievementStatus.Completed)),
            Throws.ArgumentException);
    }

    [Test]
    public void Catalog_tag_interning_and_group_lookup()
    {
        var catalog = Catalog(
            new GameDef("kill_10", 10, tags: new[] { "KILL", "DAILY" }),
            new GameDef("kill_100", 100, tags: new[] { "KILL" }),
            new GameDef("login", 1));

        Assert.That(catalog.TagCount, Is.EqualTo(2));
        Assert.That(catalog.GetIndicesByTag(catalog.GetTagIndex("KILL")).ToArray(), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(catalog.GetIndicesByTag(catalog.GetTagIndex("DAILY")).ToArray(), Is.EqualTo(new[] { 0 }));
        Assert.That(catalog.TryGetTagIndex("no_such_tag", out _), Is.False);
        Assert.That(() => catalog.GetTagIndex("no_such_tag"), Throws.TypeOf<KeyNotFoundException>());
    }

    [Test]
    public void Catalog_empty_tag_or_duplicate_tag_in_definition_throws()
    {
        Assert.That(() => Catalog(new GameDef("a", 1, tags: new[] { "" })), Throws.ArgumentException);
        Assert.That(() => Catalog(new GameDef("a", 1, tags: new[] { "T", "T" })), Throws.ArgumentException);
    }

    // ── Non-repeatable completion ────────────────────────────────────────

    [Test]
    public void Nonrepeatable_completes_once_on_reach_and_records_timestamp()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var manager = new AchievementManager<GameDef>(catalog, utcNowTicks: () => 777);

        Assert.That(manager.Increase(0, 9), Is.EqualTo(0));
        Assert.That(manager.GetStatus(0), Is.EqualTo(AchievementStatus.Active));
        Assert.That(manager.Increase(0, 1), Is.EqualTo(1));

        ref readonly var state = ref manager.GetState(0);
        Assert.That(state.CompletedCount, Is.EqualTo(1));
        Assert.That(state.LastCompletedAtUtcTicks, Is.EqualTo(777));
        Assert.That(manager.GetStatus(0), Is.EqualTo(AchievementStatus.Completed));
    }

    [Test]
    public void Nonrepeatable_excess_increase_clamps_without_recompletion()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var manager = new AchievementManager<GameDef>(catalog);

        Assert.That(manager.Increase(0, 100), Is.EqualTo(1));
        Assert.That(manager.Increase(0, 100), Is.EqualTo(0));   // no re-completion

        ref readonly var state = ref manager.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(10));
        Assert.That(state.CompletedCount, Is.EqualTo(1));
    }

    [Test]
    public void Nonrepeatable_claim_reaches_terminal_claimed_status()
    {
        var manager = new AchievementManager<GameDef>(Catalog(new GameDef("kill", 10)));

        manager.Increase(0, 10);
        Assert.That(manager.TryClaim(0), Is.True);
        Assert.That(manager.GetStatus(0), Is.EqualTo(AchievementStatus.Claimed));
        Assert.That(manager.TryClaim(0), Is.False);             // no double claim
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(10));   // non-repeatable: no deduction
    }

    // ── Repeatable completion — accumulate + deduct on claim ─────────────

    [Test]
    public void Repeatable_multiple_completions_from_large_jump()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);

        Assert.That(manager.Increase(0, 10), Is.EqualTo(1));
        Assert.That(manager.Increase(0, 35), Is.EqualTo(3));    // 45/10 = 4 pending -> 3 new
        ref readonly var state = ref manager.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(45));            // accumulates until claimed
        Assert.That(state.CompletedCount, Is.EqualTo(4));
    }

    [Test]
    public void Repeatable_claim_deducts_target_and_supports_10_of_10_ui()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);

        manager.Increase(0, 25);                                 // 2 completions, 2 pending
        Assert.That(manager.GetStatus(0), Is.EqualTo(AchievementStatus.Completed));
        Assert.That(Math.Min(manager.GetState(0).Progress, 10), Is.EqualTo(10));   // "10/10 [Claim]" UI

        Assert.That(manager.TryClaim(0), Is.True);
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(15));
        Assert.That(manager.GetStatus(0), Is.EqualTo(AchievementStatus.Completed));   // 1 pending left

        Assert.That(manager.TryClaim(0), Is.True);
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(5));
        Assert.That(manager.GetStatus(0), Is.EqualTo(AchievementStatus.Active));      // next cycle 5/10

        Assert.That(manager.Increase(0, 5), Is.EqualTo(1));      // invariant holds -> 3rd completion
    }

    [Test]
    public void Repeatable_overflow_clamps()
    {
        var catalog = Catalog(new GameDef("inf", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);

        manager.Increase(0, long.MaxValue - 5);
        manager.Increase(0, long.MaxValue);                      // clamps, no exception
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(long.MaxValue));
    }

    // ── SetProgress ──────────────────────────────────────────────────────

    [Test]
    public void SetProgress_up_completes_and_down_keeps_completed_count()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);

        Assert.That(manager.SetProgress(0, 25), Is.EqualTo(2));
        Assert.That(manager.SetProgress(0, 3), Is.EqualTo(0));   // downward: completions stay monotonic
        ref readonly var state = ref manager.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(3));
        Assert.That(state.CompletedCount, Is.EqualTo(2));
    }

    [Test]
    public void Nonrepeatable_SetProgress_saturation_clamps_to_target()
    {
        var manager = new AchievementManager<GameDef>(Catalog(new GameDef("gold", 1_000)));

        Assert.That(manager.SetProgress(0, long.MaxValue), Is.EqualTo(1));   // currency saturation scenario
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(1_000));
    }

    [Test]
    public void Increase_and_SetProgress_negative_throws()
    {
        var manager = new AchievementManager<GameDef>(Catalog(new GameDef("a", 1, tags: new[] { "T" })));
        Assert.That(() => manager.Increase(0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => manager.SetProgress(0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => manager.IncreaseByTag(0, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    // ── Availability ─────────────────────────────────────────────────────

    [Test]
    public void Initial_availability_follows_definition_and_locked_ready_ignore_progress()
    {
        var catalog = Catalog(
            new GameDef("locked", 10, initialAvailability: AchievementStatus.Locked),
            new GameDef("ready", 10, initialAvailability: AchievementStatus.Ready));
        var dirtyCount = 0;
        var manager = new AchievementManager<GameDef>(catalog, () => dirtyCount++);

        Assert.That(manager.GetStatus(0), Is.EqualTo(AchievementStatus.Locked));
        Assert.That(manager.GetStatus(1), Is.EqualTo(AchievementStatus.Ready));
        Assert.That(manager.Increase(0, 10), Is.EqualTo(0));     // no-op
        Assert.That(manager.SetProgress(1, 10), Is.EqualTo(0));
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(0));
        Assert.That(dirtyCount, Is.EqualTo(0));
    }

    [Test]
    public void Unlock_Activate_Lock_transitions_and_dirty()
    {
        var catalog = Catalog(new GameDef("a", 10, initialAvailability: AchievementStatus.Locked));
        var dirtyCount = 0;
        var manager = new AchievementManager<GameDef>(catalog, () => dirtyCount++);

        Assert.That(manager.Unlock(0), Is.True);                 // Locked -> Ready
        Assert.That(manager.Unlock(0), Is.False);                // no-op when already Ready
        Assert.That(manager.Activate(0), Is.True);               // Ready -> Active
        Assert.That(manager.Activate(0), Is.False);
        Assert.That(manager.Increase(0, 10), Is.EqualTo(1));     // now progresses
        Assert.That(manager.Lock(0), Is.True);                   // rotated out — counters kept
        Assert.That(manager.GetState(0).CompletedCount, Is.EqualTo(1));
        Assert.That(dirtyCount, Is.EqualTo(4));                  // Unlock + Activate + completion + Lock
    }

    [Test]
    public void Claim_is_allowed_while_locked()
    {
        var manager = new AchievementManager<GameDef>(Catalog(new GameDef("weekly", 10)));

        manager.Increase(0, 10);
        manager.Lock(0);                                         // closed by weekly rotation
        Assert.That(manager.TryClaim(0), Is.True);               // unclaimed rewards still claimable
    }

    // ── Tag routing ──────────────────────────────────────────────────────

    [Test]
    public void IncreaseByTag_applies_independently_to_active_only()
    {
        var catalog = Catalog(
            new GameDef("kill_10", 10, tags: new[] { "KILL" }),
            new GameDef("kill_100", 100, tags: new[] { "KILL" }),
            new GameDef("kill_locked", 10, initialAvailability: AchievementStatus.Locked, tags: new[] { "KILL" }));
        var manager = new AchievementManager<GameDef>(catalog);
        var kill = catalog.GetTagIndex("KILL");

        Assert.That(manager.IncreaseByTag(kill, 10), Is.EqualTo(1));   // only kill_10 completes
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(10));
        Assert.That(manager.GetState(1).Progress, Is.EqualTo(10));     // same amount on independent counters
        Assert.That(manager.GetState(2).Progress, Is.EqualTo(0));      // Locked skipped
    }

    [Test]
    public void IncreaseByTag_filter_overload_gates_by_condition_value()
    {
        var catalog = Catalog(
            new GameDef("grade1", 1, tags: new[] { "GOT" }, minGrade: 1),
            new GameDef("grade5", 1, tags: new[] { "GOT" }, minGrade: 5));
        var manager = new AchievementManager<GameDef>(catalog);
        var got = catalog.GetTagIndex("GOT");

        Assert.That(manager.IncreaseByTag(got, 1, 3, static (def, grade) => def.MinGrade <= grade), Is.EqualTo(1));
        Assert.That(manager.GetState(0).CompletedCount, Is.EqualTo(1));
        Assert.That(manager.GetState(1).Progress, Is.EqualTo(0));      // filtered out
    }

    [Test]
    public void Inclusive_tiers_progress_together_when_all_active()
    {
        var catalog = Catalog(
            new GameDef("ruby_1", 1, tags: new[] { "RUBY" }),
            new GameDef("ruby_10", 10, tags: new[] { "RUBY" }),
            new GameDef("ruby_100", 100, tags: new[] { "RUBY" }));
        var manager = new AchievementManager<GameDef>(catalog);

        Assert.That(manager.IncreaseByTag(catalog.GetTagIndex("RUBY"), 100), Is.EqualTo(3));   // 100 at once completes all 3 tiers
    }

    [Test]
    public void Fresh_accumulation_tiers_start_at_zero_via_chained_activate()
    {
        var catalog = Catalog(
            new GameDef("kill_1", 1, tags: new[] { "KILL" }),
            new GameDef("kill_10", 10, initialAvailability: AchievementStatus.Locked, tags: new[] { "KILL" }));
        var manager = new AchievementManager<GameDef>(catalog);
        var next = catalog.GetIndex("kill_10");
        manager.OnCompleted = (index, _, _) => { if (index == 0) manager.Activate(next); };

        manager.IncreaseByTag(catalog.GetTagIndex("KILL"), 1000);      // bulk event

        Assert.That(manager.GetStatus(0), Is.EqualTo(AchievementStatus.Completed));
        Assert.That(manager.GetStatus(next), Is.EqualTo(AchievementStatus.Active));
        Assert.That(manager.GetState(next).Progress, Is.EqualTo(0));   // starts fresh, no carry-over
    }

    // ── Claim ────────────────────────────────────────────────────────────

    [Test]
    public void Claim_succeeds_only_up_to_completed_count()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);

        Assert.That(manager.TryClaim(0), Is.False);              // not completed yet
        manager.Increase(0, 30);                                 // 3 completions
        Assert.That(manager.GetClaimableCount(0), Is.EqualTo(3));
        var claimed = 0;
        while (manager.TryClaim(0)) claimed++;                   // claim-all pattern
        Assert.That(claimed, Is.EqualTo(3));
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(0));
    }

    // ── Reset (daily/weekly cycles) ──────────────────────────────────────

    [Test]
    public void Reset_allows_repeatable_recompletion()
    {
        var catalog = Catalog(new GameDef("daily_kill", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);

        Assert.That(manager.Increase(0, 10), Is.EqualTo(1));     // day 1 completion
        manager.TryClaim(0);
        manager.Reset(0);                                        // midnight reset

        ref readonly var state = ref manager.GetState(0);
        Assert.That(state.Progress, Is.EqualTo(0));
        Assert.That(state.CompletedCount, Is.EqualTo(0));
        Assert.That(state.Availability, Is.EqualTo(AchievementStatus.Active));   // availability kept
        Assert.That(manager.Increase(0, 10), Is.EqualTo(1));     // day 2 re-completion
    }

    [Test]
    public void Reset_allows_nonrepeatable_recompletion()
    {
        var manager = new AchievementManager<GameDef>(Catalog(new GameDef("daily_once", 5)));

        manager.Increase(0, 5);
        manager.Reset(0);
        Assert.That(manager.Increase(0, 5), Is.EqualTo(1));
    }

    [Test]
    public void Reset_marks_dirty_only_on_change_and_never_fires_hook()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var dirtyCount = 0;
        var manager = new AchievementManager<GameDef>(catalog, () => dirtyCount++);

        manager.Reset(0);                                        // already 0 — no change
        Assert.That(dirtyCount, Is.EqualTo(0));

        manager.Increase(0, 10);
        dirtyCount = 0;
        manager.OnCompleted = (_, _, _) => Assert.Fail("Reset fired hook");
        manager.Reset(0);
        Assert.That(dirtyCount, Is.EqualTo(1));
    }

    // ── Restore ──────────────────────────────────────────────────────────

    [Test]
    public void Restore_fires_neither_hook_nor_dirty()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var dirtyCount = 0;
        var manager = new AchievementManager<GameDef>(catalog, () => dirtyCount++);
        manager.OnCompleted = (_, _, _) => Assert.Fail("Restore fired hook");

        manager.Restore(0, new AchievementState
        {
            Progress = 10, CompletedCount = 1, ClaimedCount = 1, LastCompletedAtUtcTicks = 5,
            Availability = AchievementStatus.Active,
        });

        Assert.That(dirtyCount, Is.EqualTo(0));
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(10));
        Assert.That(manager.GetStatus(0), Is.EqualTo(AchievementStatus.Claimed));
    }

    [Test]
    public void Restore_invariant_violation_throws()
    {
        var manager = new AchievementManager<GameDef>(Catalog(new GameDef("a", 10)));

        Assert.That(() => manager.Restore(0, new AchievementState { Progress = -1 }), Throws.ArgumentException);
        Assert.That(() => manager.Restore(0, new AchievementState { ClaimedCount = 1, CompletedCount = 0 }), Throws.ArgumentException);
        Assert.That(() => manager.Restore(0, new AchievementState { Progress = 10, CompletedCount = 2 }), Throws.ArgumentException);   // non-repeatable multi-completion
        Assert.That(() => manager.Restore(0, new AchievementState { Availability = AchievementStatus.Completed }), Throws.ArgumentException);   // derived status must not be persisted
    }

    [Test]
    public void Restore_clamps_nonrepeatable_progress_on_lowered_target_and_reevaluates_via_increase_zero()
    {
        var catalog = Catalog(new GameDef("kill", 10));          // target lowered from 100 to 10 since save
        var manager = new AchievementManager<GameDef>(catalog);

        manager.Restore(0, new AchievementState { Progress = 42, Availability = AchievementStatus.Active });
        Assert.That(manager.GetState(0).Progress, Is.EqualTo(10));
        Assert.That(manager.Increase(0, 0), Is.EqualTo(1));      // re-evaluation fires completion
    }

    // ── dirty integration ────────────────────────────────────────────────

    [Test]
    public void OnDirty_fires_only_on_actual_change()
    {
        var catalog = Catalog(new GameDef("a", 10));
        var dirtyCount = 0;
        var manager = new AchievementManager<GameDef>(catalog, () => dirtyCount++);

        manager.Increase(0, 3);
        Assert.That(dirtyCount, Is.EqualTo(1));
        manager.Increase(0, 0);                                  // no change
        manager.SetProgress(0, 3);                               // same value
        Assert.That(dirtyCount, Is.EqualTo(1));
        manager.Increase(0, 7);                                  // completes
        Assert.That(dirtyCount, Is.EqualTo(2));
        manager.Increase(0, 5);                                  // clamped, no change
        Assert.That(dirtyCount, Is.EqualTo(2));
        manager.TryClaim(0);
        Assert.That(dirtyCount, Is.EqualTo(3));
    }

    // ── OnCompleted chaining ─────────────────────────────────────────────

    [Test]
    public void OnCompleted_hook_can_progress_other_achievements()
    {
        var catalog = Catalog(new GameDef("tier1", 10), new GameDef("meta", 2));
        var manager = new AchievementManager<GameDef>(catalog);
        var metaIdx = catalog.GetIndex("meta");
        manager.OnCompleted = (index, def, count) =>
        {
            if (index != metaIdx) manager.Increase(metaIdx, count);   // meta chaining is game code's job
        };

        manager.Increase(0, 10);
        Assert.That(manager.GetState(metaIdx).Progress, Is.EqualTo(1));
    }

    [Test]
    public void OnCompleted_receives_index_definition_and_new_completion_count()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);
        (int index, string id, int count)? seen = null;
        manager.OnCompleted = (index, def, count) => seen = (index, def.Id, count);

        manager.Increase(0, 25);

        Assert.That(seen, Is.EqualTo((0, "daily", 2)));
    }
}
