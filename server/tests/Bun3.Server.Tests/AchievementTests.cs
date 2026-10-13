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
    public void Catalog_interning_id_and_definition_lookup()
    {
        var catalog = Catalog(new GameDef("a", 10), new GameDef("b", 5, repeatable: true));

        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.TryGet("a", out var a), Is.True);
        Assert.That(catalog.GetDefinition(a).Id, Is.EqualTo("a"));
        Assert.That(catalog.TryGet("missing", out var missing), Is.False);
        Assert.That(missing.IsNone, Is.True);
        Assert.That(() => catalog.GetRequired("missing"), Throws.TypeOf<AchievementCatalogException>());
        Assert.That(catalog.GetDefinition(catalog.GetRequired("b")).Id, Is.EqualTo("b"));
    }

    [Test]
    public void Catalog_none_and_out_of_range_identifiers_are_rejected()
    {
        var catalog = Catalog(new GameDef("a", 10));

        Assert.That(catalog.Contains(AchievementId.None), Is.False);
        Assert.That(() => catalog.GetDefinition(AchievementId.None), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Tag_identifier_is_distinct_from_achievement_identifier()
    {
        var catalog = Catalog(new GameDef("a", 10, tags: new[] { "daily" }));

        Assert.That(catalog.TryGetTag("daily", out var tag), Is.True);
        Assert.That(catalog.GetIndicesByTag(tag).Length, Is.EqualTo(1));
        Assert.That(catalog.GetIndicesByTag(tag)[0], Is.EqualTo(catalog.GetRequired("a")));
        Assert.That(() => catalog.GetRequiredTag("missing"), Throws.TypeOf<AchievementCatalogException>());
    }

    [TestCase("", 10)]
    [TestCase("dup", 0)]
    [TestCase("dup", -1)]
    public void Catalog_empty_id_or_nonpositive_target_throws(string id, long target)
    {
        Assert.That(() => Catalog(new GameDef(id, target)), Throws.TypeOf<AchievementCatalogException>());
    }

    [Test]
    public void Catalog_duplicate_id_throws()
    {
        Assert.That(() => Catalog(new GameDef("x", 1), new GameDef("x", 2)), Throws.TypeOf<AchievementCatalogException>());
    }

    [Test]
    public void Catalog_null_definition_throws()
    {
        Assert.That(() => new AchievementCatalog<GameDef>(new GameDef[] { null! }), Throws.TypeOf<AchievementCatalogException>());
    }

    [Test]
    public void Catalog_exceeding_max_definitions_throws()
    {
        var defs = new GameDef[AchievementCatalog<GameDef>.MaxDefinitions + 1];
        for (var i = 0; i < defs.Length; i++) defs[i] = new GameDef($"a{i}", 1);
        Assert.That(() => new AchievementCatalog<GameDef>(defs), Throws.TypeOf<AchievementCatalogException>());
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
            Throws.TypeOf<AchievementCatalogException>());
    }

    [Test]
    public void Catalog_tag_interning_and_group_lookup()
    {
        var catalog = Catalog(
            new GameDef("kill_10", 10, tags: new[] { "KILL", "DAILY" }),
            new GameDef("kill_100", 100, tags: new[] { "KILL" }),
            new GameDef("login", 1));

        Assert.That(catalog.TagCount, Is.EqualTo(2));
        Assert.That(catalog.GetIndicesByTag(catalog.GetRequiredTag("KILL")).ToArray(),
            Is.EqualTo(new[] { catalog.GetRequired("kill_10"), catalog.GetRequired("kill_100") }));
        Assert.That(catalog.GetIndicesByTag(catalog.GetRequiredTag("DAILY")).ToArray(),
            Is.EqualTo(new[] { catalog.GetRequired("kill_10") }));
        Assert.That(catalog.TryGetTag("no_such_tag", out _), Is.False);
        Assert.That(() => catalog.GetRequiredTag("no_such_tag"), Throws.TypeOf<AchievementCatalogException>());
    }

    [Test]
    public void Catalog_empty_tag_or_duplicate_tag_in_definition_throws()
    {
        Assert.That(() => Catalog(new GameDef("a", 1, tags: new[] { "" })), Throws.TypeOf<AchievementCatalogException>());
        Assert.That(() => Catalog(new GameDef("a", 1, tags: new[] { "T", "T" })), Throws.TypeOf<AchievementCatalogException>());
    }

    // ── Non-repeatable completion ────────────────────────────────────────

    [Test]
    public void Nonrepeatable_completes_once_on_reach_and_records_timestamp()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var manager = new AchievementManager<GameDef>(catalog, utcNowTicks: () => 777);
        var kill = catalog.GetRequired("kill");

        Assert.That(manager.Increase(kill, 9), Is.EqualTo(0));
        Assert.That(manager.GetStatus(kill), Is.EqualTo(AchievementStatus.Active));
        Assert.That(manager.Increase(kill, 1), Is.EqualTo(1));

        ref readonly var state = ref manager.GetState(kill);
        Assert.That(state.CompletedCount, Is.EqualTo(1));
        Assert.That(state.LastCompletedAtUtcTicks, Is.EqualTo(777));
        Assert.That(manager.GetStatus(kill), Is.EqualTo(AchievementStatus.Completed));
    }

    [Test]
    public void Nonrepeatable_excess_increase_clamps_without_recompletion()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var manager = new AchievementManager<GameDef>(catalog);
        var kill = catalog.GetRequired("kill");

        Assert.That(manager.Increase(kill, 100), Is.EqualTo(1));
        Assert.That(manager.Increase(kill, 100), Is.EqualTo(0));   // no re-completion

        ref readonly var state = ref manager.GetState(kill);
        Assert.That(state.Progress, Is.EqualTo(10));
        Assert.That(state.CompletedCount, Is.EqualTo(1));
    }

    [Test]
    public void Nonrepeatable_claim_reaches_terminal_claimed_status()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var manager = new AchievementManager<GameDef>(catalog);
        var kill = catalog.GetRequired("kill");

        manager.Increase(kill, 10);
        Assert.That(manager.TryClaim(kill), Is.True);
        Assert.That(manager.GetStatus(kill), Is.EqualTo(AchievementStatus.Claimed));
        Assert.That(manager.TryClaim(kill), Is.False);             // no double claim
        Assert.That(manager.GetState(kill).Progress, Is.EqualTo(10));   // non-repeatable: no deduction
    }

    // ── Repeatable completion — accumulate + deduct on claim ─────────────

    [Test]
    public void Repeatable_multiple_completions_from_large_jump()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);
        var daily = catalog.GetRequired("daily");

        Assert.That(manager.Increase(daily, 10), Is.EqualTo(1));
        Assert.That(manager.Increase(daily, 35), Is.EqualTo(3));    // 45/10 = 4 pending -> 3 new
        ref readonly var state = ref manager.GetState(daily);
        Assert.That(state.Progress, Is.EqualTo(45));            // accumulates until claimed
        Assert.That(state.CompletedCount, Is.EqualTo(4));
    }

    [Test]
    public void Repeatable_claim_deducts_target_and_supports_10_of_10_ui()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);
        var daily = catalog.GetRequired("daily");

        manager.Increase(daily, 25);                                 // 2 completions, 2 pending
        Assert.That(manager.GetStatus(daily), Is.EqualTo(AchievementStatus.Completed));
        Assert.That(Math.Min(manager.GetState(daily).Progress, 10), Is.EqualTo(10));   // "10/10 [Claim]" UI

        Assert.That(manager.TryClaim(daily), Is.True);
        Assert.That(manager.GetState(daily).Progress, Is.EqualTo(15));
        Assert.That(manager.GetStatus(daily), Is.EqualTo(AchievementStatus.Completed));   // 1 pending left

        Assert.That(manager.TryClaim(daily), Is.True);
        Assert.That(manager.GetState(daily).Progress, Is.EqualTo(5));
        Assert.That(manager.GetStatus(daily), Is.EqualTo(AchievementStatus.Active));      // next cycle 5/10

        Assert.That(manager.Increase(daily, 5), Is.EqualTo(1));      // invariant holds -> 3rd completion
    }

    [Test]
    public void Repeatable_overflow_clamps()
    {
        var catalog = Catalog(new GameDef("inf", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);
        var inf = catalog.GetRequired("inf");

        manager.Increase(inf, long.MaxValue - 5);
        manager.Increase(inf, long.MaxValue);                      // clamps, no exception
        Assert.That(manager.GetState(inf).Progress, Is.EqualTo(long.MaxValue));
    }

    // ── SetProgress ──────────────────────────────────────────────────────

    [Test]
    public void SetProgress_up_completes_and_down_keeps_completed_count()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);
        var daily = catalog.GetRequired("daily");

        Assert.That(manager.SetProgress(daily, 25), Is.EqualTo(2));
        Assert.That(manager.SetProgress(daily, 3), Is.EqualTo(0));   // downward: completions stay monotonic
        ref readonly var state = ref manager.GetState(daily);
        Assert.That(state.Progress, Is.EqualTo(3));
        Assert.That(state.CompletedCount, Is.EqualTo(2));
    }

    [Test]
    public void Nonrepeatable_SetProgress_saturation_clamps_to_target()
    {
        var catalog = Catalog(new GameDef("gold", 1_000));
        var manager = new AchievementManager<GameDef>(catalog);
        var gold = catalog.GetRequired("gold");

        Assert.That(manager.SetProgress(gold, long.MaxValue), Is.EqualTo(1));   // currency saturation scenario
        Assert.That(manager.GetState(gold).Progress, Is.EqualTo(1_000));
    }

    [Test]
    public void Increase_and_SetProgress_negative_throws()
    {
        var catalog = Catalog(new GameDef("a", 1, tags: new[] { "T" }));
        var manager = new AchievementManager<GameDef>(catalog);
        var a = catalog.GetRequired("a");
        var t = catalog.GetRequiredTag("T");

        Assert.That(() => manager.Increase(a, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => manager.SetProgress(a, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => manager.IncreaseByTag(t, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
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
        var locked = catalog.GetRequired("locked");
        var ready = catalog.GetRequired("ready");

        Assert.That(manager.GetStatus(locked), Is.EqualTo(AchievementStatus.Locked));
        Assert.That(manager.GetStatus(ready), Is.EqualTo(AchievementStatus.Ready));
        Assert.That(manager.Increase(locked, 10), Is.EqualTo(0));     // no-op
        Assert.That(manager.SetProgress(ready, 10), Is.EqualTo(0));
        Assert.That(manager.GetState(locked).Progress, Is.EqualTo(0));
        Assert.That(dirtyCount, Is.EqualTo(0));
    }

    [Test]
    public void Unlock_Activate_Lock_transitions_and_dirty()
    {
        var catalog = Catalog(new GameDef("a", 10, initialAvailability: AchievementStatus.Locked));
        var dirtyCount = 0;
        var manager = new AchievementManager<GameDef>(catalog, () => dirtyCount++);
        var a = catalog.GetRequired("a");

        Assert.That(manager.Unlock(a), Is.True);                 // Locked -> Ready
        Assert.That(manager.Unlock(a), Is.False);                // no-op when already Ready
        Assert.That(manager.Activate(a), Is.True);               // Ready -> Active
        Assert.That(manager.Activate(a), Is.False);
        Assert.That(manager.Increase(a, 10), Is.EqualTo(1));     // now progresses
        Assert.That(manager.Lock(a), Is.True);                   // rotated out — counters kept
        Assert.That(manager.GetState(a).CompletedCount, Is.EqualTo(1));
        Assert.That(dirtyCount, Is.EqualTo(4));                  // Unlock + Activate + completion + Lock
    }

    [Test]
    public void Claim_is_allowed_while_locked()
    {
        var catalog = Catalog(new GameDef("weekly", 10));
        var manager = new AchievementManager<GameDef>(catalog);
        var weekly = catalog.GetRequired("weekly");

        manager.Increase(weekly, 10);
        manager.Lock(weekly);                                         // closed by weekly rotation
        Assert.That(manager.TryClaim(weekly), Is.True);               // unclaimed rewards still claimable
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
        var kill = catalog.GetRequiredTag("KILL");

        Assert.That(manager.IncreaseByTag(kill, 10), Is.EqualTo(1));   // only kill_10 completes
        Assert.That(manager.GetState(catalog.GetRequired("kill_10")).Progress, Is.EqualTo(10));
        Assert.That(manager.GetState(catalog.GetRequired("kill_100")).Progress, Is.EqualTo(10));     // same amount on independent counters
        Assert.That(manager.GetState(catalog.GetRequired("kill_locked")).Progress, Is.EqualTo(0));      // Locked skipped
    }

    [Test]
    public void IncreaseByTag_filter_overload_gates_by_condition_value()
    {
        var catalog = Catalog(
            new GameDef("grade1", 1, tags: new[] { "GOT" }, minGrade: 1),
            new GameDef("grade5", 1, tags: new[] { "GOT" }, minGrade: 5));
        var manager = new AchievementManager<GameDef>(catalog);
        var got = catalog.GetRequiredTag("GOT");

        Assert.That(manager.IncreaseByTag(got, 1, 3, static (def, grade) => def.MinGrade <= grade), Is.EqualTo(1));
        Assert.That(manager.GetState(catalog.GetRequired("grade1")).CompletedCount, Is.EqualTo(1));
        Assert.That(manager.GetState(catalog.GetRequired("grade5")).Progress, Is.EqualTo(0));      // filtered out
    }

    [Test]
    public void Inclusive_tiers_progress_together_when_all_active()
    {
        var catalog = Catalog(
            new GameDef("ruby_1", 1, tags: new[] { "RUBY" }),
            new GameDef("ruby_10", 10, tags: new[] { "RUBY" }),
            new GameDef("ruby_100", 100, tags: new[] { "RUBY" }));
        var manager = new AchievementManager<GameDef>(catalog);

        Assert.That(manager.IncreaseByTag(catalog.GetRequiredTag("RUBY"), 100), Is.EqualTo(3));   // 100 at once completes all 3 tiers
    }

    [Test]
    public void Fresh_accumulation_tiers_start_at_zero_via_chained_activate()
    {
        var catalog = Catalog(
            new GameDef("kill_1", 1, tags: new[] { "KILL" }),
            new GameDef("kill_10", 10, initialAvailability: AchievementStatus.Locked, tags: new[] { "KILL" }));
        var manager = new AchievementManager<GameDef>(catalog);
        var next = catalog.GetRequired("kill_10");
        manager.OnCompleted = (id, _, _) => { if (id == catalog.GetRequired("kill_1")) manager.Activate(next); };

        manager.IncreaseByTag(catalog.GetRequiredTag("KILL"), 1000);      // bulk event

        Assert.That(manager.GetStatus(catalog.GetRequired("kill_1")), Is.EqualTo(AchievementStatus.Completed));
        Assert.That(manager.GetStatus(next), Is.EqualTo(AchievementStatus.Active));
        Assert.That(manager.GetState(next).Progress, Is.EqualTo(0));   // starts fresh, no carry-over
    }

    // ── Claim ────────────────────────────────────────────────────────────

    [Test]
    public void Claim_succeeds_only_up_to_completed_count()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);
        var daily = catalog.GetRequired("daily");

        Assert.That(manager.TryClaim(daily), Is.False);              // not completed yet
        manager.Increase(daily, 30);                                 // 3 completions
        Assert.That(manager.GetClaimableCount(daily), Is.EqualTo(3));
        var claimed = 0;
        while (manager.TryClaim(daily)) claimed++;                   // claim-all pattern
        Assert.That(claimed, Is.EqualTo(3));
        Assert.That(manager.GetState(daily).Progress, Is.EqualTo(0));
    }

    // ── Reset (daily/weekly cycles) ──────────────────────────────────────

    [Test]
    public void Reset_allows_repeatable_recompletion()
    {
        var catalog = Catalog(new GameDef("daily_kill", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);
        var dailyKill = catalog.GetRequired("daily_kill");

        Assert.That(manager.Increase(dailyKill, 10), Is.EqualTo(1));     // day 1 completion
        manager.TryClaim(dailyKill);
        manager.Reset(dailyKill);                                        // midnight reset

        ref readonly var state = ref manager.GetState(dailyKill);
        Assert.That(state.Progress, Is.EqualTo(0));
        Assert.That(state.CompletedCount, Is.EqualTo(0));
        Assert.That(state.Availability, Is.EqualTo(AchievementStatus.Active));   // availability kept
        Assert.That(manager.Increase(dailyKill, 10), Is.EqualTo(1));     // day 2 re-completion
    }

    [Test]
    public void Reset_allows_nonrepeatable_recompletion()
    {
        var catalog = Catalog(new GameDef("daily_once", 5));
        var manager = new AchievementManager<GameDef>(catalog);
        var dailyOnce = catalog.GetRequired("daily_once");

        manager.Increase(dailyOnce, 5);
        manager.Reset(dailyOnce);
        Assert.That(manager.Increase(dailyOnce, 5), Is.EqualTo(1));
    }

    [Test]
    public void Reset_marks_dirty_only_on_change_and_never_fires_hook()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var dirtyCount = 0;
        var manager = new AchievementManager<GameDef>(catalog, () => dirtyCount++);
        var daily = catalog.GetRequired("daily");

        manager.Reset(daily);                                        // already 0 — no change
        Assert.That(dirtyCount, Is.EqualTo(0));

        manager.Increase(daily, 10);
        dirtyCount = 0;
        manager.OnCompleted = (_, _, _) => Assert.Fail("Reset fired hook");
        manager.Reset(daily);
        Assert.That(dirtyCount, Is.EqualTo(1));
    }

    // ── Restore ──────────────────────────────────────────────────────────

    [Test]
    public void Restore_fires_neither_hook_nor_dirty()
    {
        var catalog = Catalog(new GameDef("kill", 10));
        var dirtyCount = 0;
        var manager = new AchievementManager<GameDef>(catalog, () => dirtyCount++);
        var kill = catalog.GetRequired("kill");
        manager.OnCompleted = (_, _, _) => Assert.Fail("Restore fired hook");

        manager.Restore(kill, new AchievementState
        {
            Progress = 10, CompletedCount = 1, ClaimedCount = 1, LastCompletedAtUtcTicks = 5,
            Availability = AchievementStatus.Active,
        });

        Assert.That(dirtyCount, Is.EqualTo(0));
        Assert.That(manager.GetState(kill).Progress, Is.EqualTo(10));
        Assert.That(manager.GetStatus(kill), Is.EqualTo(AchievementStatus.Claimed));
    }

    [Test]
    public void Restore_invariant_violation_throws()
    {
        var catalog = Catalog(new GameDef("a", 10));
        var manager = new AchievementManager<GameDef>(catalog);
        var a = catalog.GetRequired("a");

        Assert.That(() => manager.Restore(a, new AchievementState { Progress = -1 }), Throws.ArgumentException);
        Assert.That(() => manager.Restore(a, new AchievementState { ClaimedCount = 1, CompletedCount = 0 }), Throws.ArgumentException);
        Assert.That(() => manager.Restore(a, new AchievementState { Progress = 10, CompletedCount = 2 }), Throws.ArgumentException);   // non-repeatable multi-completion
        Assert.That(() => manager.Restore(a, new AchievementState { Availability = AchievementStatus.Completed }), Throws.ArgumentException);   // derived status must not be persisted
    }

    [Test]
    public void Restore_clamps_nonrepeatable_progress_on_lowered_target_and_reevaluates_via_increase_zero()
    {
        var catalog = Catalog(new GameDef("kill", 10));          // target lowered from 100 to 10 since save
        var manager = new AchievementManager<GameDef>(catalog);
        var kill = catalog.GetRequired("kill");

        manager.Restore(kill, new AchievementState { Progress = 42, Availability = AchievementStatus.Active });
        Assert.That(manager.GetState(kill).Progress, Is.EqualTo(10));
        Assert.That(manager.Increase(kill, 0), Is.EqualTo(1));      // re-evaluation fires completion
    }

    // ── dirty integration ────────────────────────────────────────────────

    [Test]
    public void OnDirty_fires_only_on_actual_change()
    {
        var catalog = Catalog(new GameDef("a", 10));
        var dirtyCount = 0;
        var manager = new AchievementManager<GameDef>(catalog, () => dirtyCount++);
        var a = catalog.GetRequired("a");

        manager.Increase(a, 3);
        Assert.That(dirtyCount, Is.EqualTo(1));
        manager.Increase(a, 0);                                  // no change
        manager.SetProgress(a, 3);                               // same value
        Assert.That(dirtyCount, Is.EqualTo(1));
        manager.Increase(a, 7);                                  // completes
        Assert.That(dirtyCount, Is.EqualTo(2));
        manager.Increase(a, 5);                                  // clamped, no change
        Assert.That(dirtyCount, Is.EqualTo(2));
        manager.TryClaim(a);
        Assert.That(dirtyCount, Is.EqualTo(3));
    }

    // ── OnCompleted chaining ─────────────────────────────────────────────

    [Test]
    public void OnCompleted_hook_can_progress_other_achievements()
    {
        var catalog = Catalog(new GameDef("tier1", 10), new GameDef("meta", 2));
        var manager = new AchievementManager<GameDef>(catalog);
        var tier1 = catalog.GetRequired("tier1");
        var metaId = catalog.GetRequired("meta");
        manager.OnCompleted = (id, def, count) =>
        {
            if (id != metaId) manager.Increase(metaId, count);   // meta chaining is game code's job
        };

        manager.Increase(tier1, 10);
        Assert.That(manager.GetState(metaId).Progress, Is.EqualTo(1));
    }

    [Test]
    public void OnCompleted_receives_index_definition_and_new_completion_count()
    {
        var catalog = Catalog(new GameDef("daily", 10, repeatable: true));
        var manager = new AchievementManager<GameDef>(catalog);
        var daily = catalog.GetRequired("daily");
        (AchievementId id, string id2, int count)? seen = null;
        manager.OnCompleted = (id, def, count) => seen = (id, def.Id, count);

        manager.Increase(daily, 25);

        Assert.That(seen, Is.EqualTo((daily, "daily", 2)));
    }
}
