#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class WorldTests
{
    private static Unit MakeUnit(EffectTestKit kit, ulong id, ulong ownerId = 0)
    {
        ReadOnlySpan<ushort> attributes = stackalloc ushort[]
        {
            EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Mp,
            EffectTestKit.Attack, EffectTestKit.Resistance,
        };
        return new Unit(new TargetId(id), EffectTestKit.BuildAttributeRegistry(), attributes, kit.TagCatalog, ownerId);
    }

    private static World MakeWorld(EffectTestKit kit)
    {
        var abilities = new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        return new World(kit.Catalog, abilities, new XorShiftRng(12345));
    }

    [Test]
    public void Spawn_is_visible_only_after_the_next_tick()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);

        world.Spawn(MakeUnit(kit, 1));

        Assert.That(world.UnitCount, Is.Zero, "spawn must not mutate the registry mid-frame");
        world.Tick();
        Assert.That(world.UnitCount, Is.EqualTo(1));
        Assert.That(world.TryGetUnit(new TargetId(1), out _), Is.True);
    }

    [Test]
    public void Registry_and_target_ids_stay_in_ascending_order()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);

        world.Spawn(MakeUnit(kit, 30));
        world.Spawn(MakeUnit(kit, 10));
        world.Spawn(MakeUnit(kit, 20));
        world.Tick();

        Assert.That(world.TargetIds.Count, Is.EqualTo(3));
        for (var i = 1; i < world.TargetIds.Count; i++)
        {
            Assert.That(world.TargetIds[i], Is.GreaterThan(world.TargetIds[i - 1]));
        }
    }

    [Test]
    public void Despawn_is_visible_only_after_the_next_tick()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 1));
        world.Tick();

        world.Despawn(new TargetId(1));

        Assert.That(world.UnitCount, Is.EqualTo(1));
        world.Tick();
        Assert.That(world.UnitCount, Is.Zero);
        Assert.That(world.TryResolve(new TargetId(1), out _), Is.False);
    }

    [Test]
    public void Resolver_returns_the_units_effect_target()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();

        Assert.That(world.TryResolve(new TargetId(1), out var resolved), Is.True);
        Assert.That(resolved, Is.SameAs(unit.Effects));
    }

    [Test]
    public void Spawning_a_registered_id_throws_at_the_call_and_the_world_keeps_ticking()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 1));
        world.Tick();

        Assert.That(() => world.Spawn(MakeUnit(kit, 1)), Throws.InvalidOperationException);
        Assert.That(() => world.Tick(), Throws.Nothing);
        Assert.That(world.UnitCount, Is.EqualTo(1));
    }

    [Test]
    public void Spawning_an_id_already_queued_this_frame_throws()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);

        world.Spawn(MakeUnit(kit, 5));

        Assert.That(() => world.Spawn(MakeUnit(kit, 5)), Throws.InvalidOperationException);
    }

    [Test]
    public void Apply_budget_scales_with_the_registered_unit_count()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var abilities = new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        var world = new World(kit.Catalog, abilities, new XorShiftRng(12345), 2);
        for (ulong id = 1; id <= 3; id++) world.Spawn(MakeUnit(kit, id));
        world.Tick();

        for (var i = 0; i < 7; i++) world.Pipeline.EnqueueApply(kit.EffectId("noop"), new TargetId(1), new TargetId(1));
        world.Tick();

        Assert.That(world.Pipeline.PendingApplyCount, Is.EqualTo(1), "2 per unit x 3 units drains 6 of 7");
    }

    [Test]
    public void World_tick_advances_the_pipeline_tick_counter_once()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);

        Assert.That(world.CurrentTick, Is.Zero);
        world.Tick();
        world.Tick();
        Assert.That(world.CurrentTick, Is.EqualTo(2));
        Assert.That(world.CurrentTick, Is.EqualTo(world.Pipeline.CurrentTick));
    }

    [Test]
    public void Effects_applied_to_a_spawned_unit_run_through_the_worlds_pipeline()
    {
        var kit = EffectTestKit.Create();
        var damage = EffectTestKit.MinimalInstant("hit");
        damage.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = Bun3.Gameplay.Attributes.AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Bun3.Gameplay.Attributes.Operand.Constant(-30) },
        });
        kit.AddEffect(damage);
        var world = MakeWorld(kit);

        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();

        unit.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unit.Attributes.SetBase(EffectTestKit.Hp, 100);
        world.Pipeline.EnqueueApply(kit.EffectId("hit"), new TargetId(0), unit.Id);
        world.Tick();

        Assert.That(unit.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)70));
    }

    [Test]
    public void World_snapshot_round_trip_restores_units_and_counters()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInfinite("aura"));
        var world = MakeWorld(kit);
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();
        unit.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unit.Attributes.SetBase(EffectTestKit.Hp, 80);

        world.Pipeline.EnqueueApply(kit.EffectId("aura"), unit.Id, unit.Id);
        world.Tick();
        var snapshot = world.CreateSnapshot();
        var tickAtSnapshot = world.CurrentTick;

        unit.Attributes.SetBase(EffectTestKit.Hp, 5);
        world.Tick();
        world.Tick();

        world.RestoreSnapshot(snapshot);

        Assert.That(unit.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)80));
        Assert.That(world.CurrentTick, Is.EqualTo(tickAtSnapshot));
        Assert.That(unit.Effects.ActiveEffectCount, Is.EqualTo(1));
    }

    [Test]
    public void Mismatched_unit_id_in_restore_snapshot_leaves_every_unit_untouched()
    {
        var kit = EffectTestKit.Create();
        var world = MakeWorld(kit);
        var unitA = MakeUnit(kit, 1);
        var unitB = MakeUnit(kit, 2);
        world.Spawn(unitA);
        world.Spawn(unitB);
        world.Tick();
        unitA.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unitA.Attributes.SetBase(EffectTestKit.Hp, 10);
        unitB.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unitB.Attributes.SetBase(EffectTestKit.Hp, 20);

        var snapshot = world.CreateSnapshot();

        // Mutate past the snapshot, then swap unit B out for a different id so the snapshot's
        // second entry (id 2) no longer lines up with the registry's second entry.
        unitA.Attributes.SetBase(EffectTestKit.Hp, 99);
        world.Despawn(unitB.Id);
        var unitC = MakeUnit(kit, 3);
        unitC.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unitC.Attributes.SetBase(EffectTestKit.Hp, 77);
        world.Spawn(unitC);
        world.Tick();

        Assert.That(() => world.RestoreSnapshot(snapshot), Throws.ArgumentException);

        // Unit A's id matched at its index, but the whole batch is validated before any unit is
        // restored -- its post-snapshot mutation must survive the rejected restore.
        Assert.That(unitA.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)99));
        Assert.That(unitC.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)77));
    }

    [Test]
    public void Snapshotting_with_pending_applications_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 1));
        world.Tick();

        world.Pipeline.EnqueueApply(kit.EffectId("noop"), new TargetId(1), new TargetId(1));

        Assert.That(() => world.CreateSnapshot(), Throws.InvalidOperationException);
    }

    [Test]
    public void Snapshotting_with_pending_commands_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 1));
        world.Tick();

        world.EnqueueApply(kit.EffectId("noop"), new TargetId(1), new TargetId(1));

        Assert.That(() => world.CreateSnapshot(), Throws.InvalidOperationException);
    }

    [Test]
    public void A_command_enqueued_during_command_processing_runs_on_the_next_tick()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInfinite("mark"));
        kit.AddEffect(EffectTestKit.MinimalInfinite("mark.echo"));   // distinct id: a same-id apply would merge
        var abilityDef = new AbilityDef { Name = "echo", MaxLevel = 1 };
        abilityDef.Effects.Add(new AbilityEffectDef { EffectName = "mark", SelectorTag = "selector.team" });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(abilityDef);
        World? world = null;
        var echo = new EchoSelector(() => world!);
        kit.RegisterSelector("selector.team", echo);
        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        world = new World(kit.Catalog, abilities, new XorShiftRng(12345));
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();
        var id = abilities.GetRequiredId("echo");
        unit.Abilities.Grant(id, GrantSource.Archetype(), level: 1);
        echo.EffectId = kit.EffectId("mark.echo");

        world.EnqueueActivation(unit.Id, id);
        world.Tick();
        Assert.That(world.PendingCommandCount, Is.EqualTo(1), "the selector's enqueue waits for the next tick");
        Assert.That(unit.Effects.ActiveEffectCount, Is.EqualTo(1));

        world.Tick();
        Assert.That(world.PendingCommandCount, Is.Zero);
        Assert.That(unit.Effects.ActiveEffectCount, Is.EqualTo(2));
    }

    private sealed class EchoSelector : Bun3.Gameplay.Seams.ITargetSelector
    {
        private readonly Func<World> _world;
        internal EchoSelector(Func<World> world) => _world = world;
        internal int EffectId { get; set; }

        public int Select(in Bun3.Gameplay.Seams.SelectorContext ctx, Span<TargetId> results)
        {
            _world().EnqueueApply(EffectId, ctx.Source, ctx.Source);
            results[0] = ctx.Source;
            return 1;
        }
    }

    [Test]
    public void Snapshotting_with_pending_structural_changes_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);

        world.Spawn(MakeUnit(kit, 1));

        Assert.That(() => world.CreateSnapshot(), Throws.InvalidOperationException);
    }

    [Test]
    public void A_same_frame_spawn_then_despawn_of_the_same_id_cancels_out()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);

        world.Spawn(MakeUnit(kit, 5));
        world.Despawn(new TargetId(5));
        world.Tick();

        Assert.That(world.TryGetUnit(new TargetId(5), out _), Is.False);
    }

    [Test]
    public void A_same_frame_despawn_then_spawn_of_the_same_id_registers_the_new_unit()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 5, ownerId: 1));
        world.Tick();

        world.Despawn(new TargetId(5));
        world.Spawn(MakeUnit(kit, 5, ownerId: 2));
        world.Tick();

        Assert.That(world.TryGetUnit(new TargetId(5), out var unit), Is.True);
        Assert.That(unit!.OwnerId, Is.EqualTo(2ul));
    }

    [Test]
    public void Despawning_the_same_id_twice_in_one_frame_is_harmless()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 5));
        world.Tick();

        world.Despawn(new TargetId(5));
        world.Despawn(new TargetId(5));

        Assert.That(() => world.Tick(), Throws.Nothing);
        Assert.That(world.TryGetUnit(new TargetId(5), out _), Is.False);
    }

    [Test]
    public void A_rejected_duplicate_spawn_keeps_the_valid_queued_spawns()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 1));
        world.Tick();

        world.Spawn(MakeUnit(kit, 2));
        Assert.That(() => world.Spawn(MakeUnit(kit, 1)), Throws.InvalidOperationException);
        world.Tick();

        Assert.That(world.UnitCount, Is.EqualTo(2));
        Assert.That(world.TryGetUnit(new TargetId(2), out _), Is.True);
    }
}
