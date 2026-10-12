#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

/// <summary>Timed instances observed across long stretches in which nothing is due.</summary>
[TestFixture]
public sealed class EffectDueTickTests
{
    [Test]
    public void Remaining_ticks_after_a_long_idle_stretch_are_exact()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("ward", ticks: 50));
        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.EffectId("ward"), kit.Attacker.Id, kit.Defender.Id);

        for (var tick = 0; tick < 30; tick++) pipeline.Tick();

        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(21));   // created on tick 0, advanced on 1..29
        for (var tick = 0; tick < 20; tick++) pipeline.Tick();
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(1));
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero, "expires on its last tick");
    }

    [Test]
    public void A_target_with_nothing_due_is_not_advanced()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("ward", ticks: 100));
        var pipeline = kit.BuildPipeline();
        pipeline.EnqueueApply(kit.EffectId("ward"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        var visits = pipeline.AdvanceVisitCount;

        for (var tick = 0; tick < 50; tick++) pipeline.Tick();

        Assert.That(pipeline.AdvanceVisitCount, Is.EqualTo(visits), "the ward is next due on tick 100");
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(50));
    }

    private sealed class PeekCalc : Bun3.Gameplay.Seams.IMagnitudeCalc
    {
        internal EffectTarget? Peek;

        public BigNum Calculate(in Bun3.Gameplay.Seams.MagnitudeContext ctx)
        {
            if (Peek != null && Peek.ActiveEffectCount > 0) Peek.GetActiveEffect(0);
            return BigNum.Zero;
        }
    }

    [Test]
    public void A_lookup_during_the_time_step_does_not_rewind_an_advanced_target()
    {
        var kit = EffectTestKit.Create();
        var peek = new PeekCalc();
        kit.RegisterMagnitudeCalc("calc.magnitude.x", peek);
        var bleed = EffectTestKit.MinimalInfinite("bleed");
        bleed.PeriodTicks = 3;
        bleed.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-1) },
        });
        kit.AddEffect(bleed);
        var probe = EffectTestKit.MinimalInfinite("probe");
        probe.PeriodTicks = 1;
        probe.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Mp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { CalcTag = "calc.magnitude.x" },
        });
        kit.AddEffect(probe);
        var pipeline = kit.BuildPipeline();
        kit.Attacker.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Attacker.Attributes.SetBase(EffectTestKit.Hp, 100);
        peek.Peek = kit.Attacker;   // the attacker advances before the defender, whose period peeks at it
        pipeline.EnqueueApply(kit.EffectId("bleed"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.EnqueueApply(kit.EffectId("probe"), kit.Defender.Id, kit.Defender.Id);

        for (var tick = 0; tick < 31; tick++) pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)90));
    }

    [Test]
    public void A_periodic_effect_fires_exactly_on_its_period_through_idle_ticks()
    {
        var kit = EffectTestKit.Create();
        var bleed = EffectTestKit.MinimalInfinite("bleed");
        bleed.PeriodTicks = 7;
        bleed.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-1) },
        });
        kit.AddEffect(bleed);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);
        pipeline.EnqueueApply(kit.EffectId("bleed"), kit.Attacker.Id, kit.Defender.Id);

        for (var tick = 0; tick < 40; tick++)
        {
            pipeline.Tick();
            Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)(100 - tick / 7)), $"after tick {tick}");
        }
    }

    [Test]
    public void A_target_taken_over_by_another_pipeline_keeps_the_time_already_elapsed()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("ward", ticks: 50));
        var first = kit.BuildPipeline();
        first.EnqueueApply(kit.EffectId("ward"), kit.Attacker.Id, kit.Defender.Id);
        for (var tick = 0; tick < 11; tick++) first.Tick();   // created on tick 0, advanced on 1..10

        var second = kit.BuildPipeline();
        second.Tick();   // a takeover keeps counting down on the new owner's very first time step

        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(39));
    }

    [Test]
    public void A_pipeline_taking_over_a_target_advances_it_from_its_first_tick()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("ward", ticks: 50));
        var first = kit.BuildPipeline();
        for (var tick = 0; tick < 3; tick++) first.Tick();
        first.EnqueueApply(kit.EffectId("ward"), kit.Attacker.Id, kit.Defender.Id);
        for (var tick = 0; tick < 8; tick++) first.Tick();   // created on tick 3, advanced on 4..10

        var second = kit.BuildPipeline();
        for (var tick = 0; tick < 2; tick++) second.Tick();

        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(41));
    }

    private sealed class ListResolver : IEffectTargetResolver
    {
        internal readonly System.Collections.Generic.List<EffectTarget> Targets = new System.Collections.Generic.List<EffectTarget>();
        private readonly System.Collections.Generic.List<TargetId> _ids = new System.Collections.Generic.List<TargetId>();

        public System.Collections.Generic.IReadOnlyList<TargetId> TargetIds => _ids;

        internal void Add(EffectTarget target)
        {
            Targets.Add(target);
            _ids.Add(target.Id);
        }

        public bool TryResolve(TargetId id, out EffectTarget? target)
        {
            target = Targets.Find(t => t.Id == id);
            return target != null;
        }

        public EffectTarget TargetAt(int index) => Targets[index];
    }

    [Test]
    public void A_takeover_by_a_pipeline_whose_clock_is_ahead_advances_on_the_first_tick()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("ward", ticks: 50));
        var first = kit.BuildPipeline();
        first.EnqueueApply(kit.EffectId("ward"), kit.Attacker.Id, kit.Defender.Id);
        for (var tick = 0; tick < 11; tick++) first.Tick();   // 40 remain; next due on the first clock's tick 50

        var resolver = new ListResolver();
        var second = new EffectPipeline(kit.Catalog, resolver, new XorShiftRng(1));
        for (var tick = 0; tick < 20; tick++) second.Tick();
        resolver.Add(kit.Defender);
        second.Tick();

        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(39));
    }

    [Test]
    public void A_unit_moved_to_a_world_with_an_earlier_clock_keeps_counting_down()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("ward", ticks: 50));
        var abilities = new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        var early = new World(kit.Database(abilities), new XorShiftRng(1));
        var late = new World(kit.Database(abilities), new XorShiftRng(1));
        ReadOnlySpan<ushort> attributes = stackalloc ushort[] { EffectTestKit.Hp, EffectTestKit.MaxHp };
        var unit = new Unit(new TargetId(1), kit.Attributes, attributes, kit.TagCatalog);
        for (var tick = 0; tick < 20; tick++) late.Tick();
        late.Spawn(unit);
        late.EnqueueApply(kit.EffectId("ward"), unit.Id, unit.Id);
        for (var tick = 0; tick < 5; tick++) late.Tick();      // created on tick 20, advanced on 21..24
        late.Despawn(unit.Id);
        late.Tick();

        early.Spawn(unit);
        for (var tick = 0; tick < 5; tick++) early.Tick();

        Assert.That(unit.Effects.GetActiveEffect(0).RemainingTicks, Is.EqualTo(41));
    }

    [Test]
    public void Time_does_not_pass_for_a_despawned_unit()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("ward", ticks: 40));
        var world = new World(kit.Database(new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog)), new XorShiftRng(1));
        ReadOnlySpan<ushort> attributes = stackalloc ushort[] { EffectTestKit.Hp, EffectTestKit.MaxHp };
        var unit = new Unit(new TargetId(1), kit.Attributes, attributes, kit.TagCatalog);
        world.Spawn(unit);
        world.EnqueueApply(kit.EffectId("ward"), unit.Id, unit.Id);
        for (var tick = 0; tick < 6; tick++) world.Tick();   // created on tick 0, advanced on 1..5

        world.Despawn(unit.Id);
        for (var tick = 0; tick < 10; tick++) world.Tick();
        world.Spawn(unit);
        world.Tick();                                         // advanced once more after returning

        Assert.That(unit.Effects.GetActiveEffect(0).RemainingTicks, Is.EqualTo(34));
    }

    [Test]
    public void A_merge_that_shortens_the_remaining_time_expires_on_time()
    {
        var kit = EffectTestKit.Create();
        var slow = EffectTestKit.MinimalDuration("slow", ticks: 10);
        slow.DurationScale = new MagnitudeDef { Base = Operand.Attribute(EffectTestKit.Resistance) };
        kit.AddEffect(slow);
        var pipeline = kit.BuildPipeline();
        kit.Defender.Attributes.SetBase(EffectTestKit.Resistance, 5);
        pipeline.EnqueueApply(kit.EffectId("slow"), kit.Attacker.Id, kit.Defender.Id);
        for (var tick = 0; tick < 6; tick++) pipeline.Tick();

        kit.Defender.Attributes.SetBase(EffectTestKit.Resistance, 1);
        pipeline.EnqueueApply(kit.EffectId("slow"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();                                      // refreshed to 10, advanced once in the same tick
        Assert.That(kit.Defender.GetActiveEffect(0).RemainingTicks, Is.EqualTo(9));
        for (var tick = 0; tick < 8; tick++) pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));
        pipeline.Tick();
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero, "expires on the shortened schedule, not the original one");
    }

    [Test]
    public void Cooldown_remaining_after_an_idle_stretch_is_exact()
    {
        var kit = EffectTestKit.Create();
        var cooldown = EffectTestKit.MinimalDuration("cd", ticks: 50);
        cooldown.GrantedTags.Add("cooldown.fireball");
        kit.AddEffect(cooldown);
        kit.AddEffect(EffectTestKit.MinimalInstant("bolt"));
        var builder = new AbilityCatalogBuilder();
        var fireball = new AbilityDef { Name = "fireball", MaxLevel = 1, CooldownEffect = "cd" };
        fireball.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
        builder.Add(fireball);
        var abilities = builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        var world = new World(kit.Database(abilities), new XorShiftRng(1));
        ReadOnlySpan<ushort> attributes = stackalloc ushort[] { EffectTestKit.Hp, EffectTestKit.MaxHp };
        var unit = new Unit(new TargetId(1), kit.Attributes, attributes, kit.TagCatalog);
        unit.Abilities.Grant(abilities.GetRequiredId("fireball"), GrantSource.Code(), 1);
        world.Spawn(unit);
        world.Tick();

        world.EnqueueActivation(unit.Id, abilities.GetRequiredId("fireball"));
        for (var tick = 0; tick < 20; tick++) world.Tick();

        Assert.That(world.TryGetCooldown(unit.Id, abilities.GetRequiredId("fireball"), out var remaining, out var duration), Is.True);
        Assert.That(remaining, Is.EqualTo(31));   // applied on the activation tick, advanced on the 19 after it
        Assert.That(duration, Is.EqualTo(50));
    }

    [Test]
    public void A_snapshot_taken_after_an_idle_stretch_resumes_identically()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("ward", ticks: 60));
        var world = new World(kit.Database(new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog)), new XorShiftRng(1));
        ReadOnlySpan<ushort> attributes = stackalloc ushort[] { EffectTestKit.Hp, EffectTestKit.MaxHp };
        var unit = new Unit(new TargetId(1), kit.Attributes, attributes, kit.TagCatalog);
        world.Spawn(unit);
        world.EnqueueApply(kit.EffectId("ward"), unit.Id, unit.Id);
        for (var tick = 0; tick < 25; tick++) world.Tick();

        var snapshot = world.CreateSnapshot();
        for (var tick = 0; tick < 10; tick++) world.Tick();
        var expected = unit.Effects.GetActiveEffect(0).RemainingTicks;

        world.RestoreSnapshot(snapshot);
        Assert.That(unit.Effects.GetActiveEffect(0).RemainingTicks, Is.EqualTo(expected + 10));
        for (var tick = 0; tick < 10; tick++) world.Tick();
        Assert.That(unit.Effects.GetActiveEffect(0).RemainingTicks, Is.EqualTo(expected));
    }

    [Test]
    public void Restoring_one_tick_back_rewinds_the_advanced_through_clock()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("ward", ticks: 60));
        var world = new World(kit.Database(new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog)), new XorShiftRng(1));
        ReadOnlySpan<ushort> attributes = stackalloc ushort[] { EffectTestKit.Hp, EffectTestKit.MaxHp };
        var unit = new Unit(new TargetId(1), kit.Attributes, attributes, kit.TagCatalog);
        world.Spawn(unit);
        world.EnqueueApply(kit.EffectId("ward"), unit.Id, unit.Id);
        for (var tick = 0; tick < 5; tick++) world.Tick();
        var snapshot = world.CreateSnapshot();
        world.Tick();

        world.RestoreSnapshot(snapshot);
        unit.Effects.SyncTime();

        Assert.That(unit.Effects.AdvancedThrough, Is.EqualTo(world.CurrentTick - 1), "between ticks, phase 2 last ran on the previous tick");
    }
}
