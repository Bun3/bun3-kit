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
        return new World(kit.Database(abilities), new XorShiftRng(12345));
    }

    [Test]
    public void Create_unit_applies_the_archetypes_bases_and_starting_abilities()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInstant("bolt"));
        var slash = new AbilityDef { Name = "slash", MaxLevel = 3 };
        slash.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(slash);
        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        var warrior = new ArchetypeDef { Name = "warrior" };
        warrior.Attributes.Add(new ArchetypeAttributeDef { AttributeId = EffectTestKit.Hp, Base = 80 });
        warrior.Attributes.Add(new ArchetypeAttributeDef { AttributeId = EffectTestKit.MaxHp, Base = 100 });
        warrior.Abilities.Add(new ArchetypeAbilityDef { AbilityName = "slash", Level = 2 });
        var archetypeBuilder = new ArchetypeCatalogBuilder();
        archetypeBuilder.Add(warrior);
        var database = kit.Database(abilities, archetypeBuilder.Build(kit.Attributes, abilities));
        var world = new World(database, new XorShiftRng(1));

        var unit = world.CreateUnit(new TargetId(1), database.Archetypes.GetRequiredId("warrior"), ownerId: 7);

        Assert.That(world.UnitCount, Is.Zero, "created, not yet spawned");
        Assert.That(unit.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)80), "authored before its MaxHp bound, still not clamped");
        Assert.That(unit.Attributes.Has(EffectTestKit.Mp), Is.False);
        Assert.That(unit.Abilities.GetLevel(abilities.GetRequiredId("slash")), Is.EqualTo(2));
        Assert.That(unit.Abilities.GetGrant(0).Source, Is.EqualTo(GrantSource.Archetype()));
        Assert.That(unit.OwnerId, Is.EqualTo(7ul));
        Assert.That(() => world.CreateUnit(new TargetId(2), 5), Throws.InstanceOf<ArgumentOutOfRangeException>());

        world.Spawn(unit);
        world.Tick();
        Assert.That(world.UnitCount, Is.EqualTo(1));
        Assert.That(world.Database, Is.SameAs(database));
    }

    [Test]
    public void Units_of_one_archetype_share_one_attribute_layout()
    {
        var kit = EffectTestKit.Create();
        var abilities = new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        var warrior = new ArchetypeDef { Name = "warrior" };
        warrior.Attributes.Add(new ArchetypeAttributeDef { AttributeId = EffectTestKit.Hp, Base = 80 });
        warrior.Attributes.Add(new ArchetypeAttributeDef { AttributeId = EffectTestKit.MaxHp, Base = 100 });
        var archetypes = new ArchetypeCatalogBuilder();
        archetypes.Add(warrior);
        var database = kit.Database(abilities, archetypes.Build(kit.Attributes, abilities));
        var world = new World(database, new XorShiftRng(1));

        var first = world.CreateUnit(new TargetId(1), 0);
        var second = world.CreateUnit(new TargetId(2), 0);

        Assert.That(first.Attributes.Layout, Is.SameAs(second.Attributes.Layout));
        Assert.That(second.Attributes.GetCurrent(EffectTestKit.Hp), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)80));
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
    public void Target_at_an_index_is_the_target_resolved_from_the_id_at_that_index()
    {
        var kit = EffectTestKit.Create();
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 30));
        world.Spawn(MakeUnit(kit, 10));
        world.Spawn(MakeUnit(kit, 20));
        world.Tick();

        for (var i = 0; i < world.TargetIds.Count; i++)
        {
            world.TryResolve(world.TargetIds[i], out var resolved);
            Assert.That(world.TargetAt(i), Is.SameAs(resolved));
        }
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
        var world = new World(kit.Database(abilities), new XorShiftRng(12345), 2);
        for (ulong id = 1; id <= 3; id++) world.Spawn(MakeUnit(kit, id));
        world.Tick();

        for (var i = 0; i < 7; i++) world.EnqueueApply(kit.EffectId("noop"), new TargetId(1), new TargetId(1));
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
        world.EnqueueApply(kit.EffectId("hit"), new TargetId(0), unit.Id);
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

        world.EnqueueApply(kit.EffectId("aura"), unit.Id, unit.Id);
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
    public void Snapshot_round_trip_keeps_duration_totals()
    {
        var kit = EffectTestKit.Create();
        var slow = EffectTestKit.MinimalDuration("slow", ticks: 10);
        slow.DurationScale = new MagnitudeDef
        {
            Base = Bun3.Gameplay.Attributes.Operand.Constant(Bun3.Gameplay.Numerics.BigNum.FromParts(2, 0)),
        };
        kit.AddEffect(slow);
        var world = MakeWorld(kit);
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.EnqueueApply(kit.EffectId("slow"), unit.Id, unit.Id);
        world.Tick();
        world.Tick();
        var before = unit.Effects.GetActiveEffect(0);
        var snapshot = world.CreateSnapshot();
        world.Tick();

        world.RestoreSnapshot(snapshot);

        var after = unit.Effects.GetActiveEffect(0);
        Assert.That(after.DurationTicks, Is.EqualTo(20));
        Assert.That(after.RemainingTicks, Is.EqualTo(before.RemainingTicks));
        Assert.That(after.InstanceId, Is.EqualTo(before.InstanceId));
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

        // Direct pipeline enqueue on purpose: the only way an application is still pending outside a tick.
        world.Pipeline.EnqueueApply(kit.EffectId("noop"), new TargetId(1), new TargetId(1));

        Assert.That(() => world.CreateSnapshot(), Throws.InvalidOperationException);
    }

    [Test]
    public void Removal_and_base_write_commands_run_inside_the_next_tick()
    {
        var kit = EffectTestKit.Create();
        foreach (var name in new[] { "aura.a", "aura.b" })
        {
            var aura = EffectTestKit.MinimalInfinite(name);
            aura.AssetTags.Add("effect.frost");
            kit.AddEffect(aura);
        }

        var world = MakeWorld(kit);
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.EnqueueApply(kit.EffectId("aura.a"), unit.Id, unit.Id);
        world.EnqueueApply(kit.EffectId("aura.b"), unit.Id, unit.Id);
        world.Tick();
        var second = unit.Effects.GetActiveEffect(1).InstanceId;

        world.EnqueueRemoveById(unit.Id, second);
        world.EnqueueSetBase(unit.Id, EffectTestKit.Attack, 42);
        Assert.That(unit.Effects.ActiveEffectCount, Is.EqualTo(2), "nothing runs outside the tick");
        Assert.That(unit.Attributes.GetBase(EffectTestKit.Attack), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)0));
        world.Tick();

        Assert.That(unit.Effects.ActiveEffectCount, Is.EqualTo(1));
        Assert.That(unit.Attributes.GetBase(EffectTestKit.Attack), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)42));

        world.EnqueueRemoveByTag(unit.Id, kit.Tag("effect.frost"));
        world.Tick();
        Assert.That(unit.Effects.ActiveEffectCount, Is.Zero);
    }

    [Test]
    public void Commands_for_unknown_units_or_undeclared_attributes_are_ignored()
    {
        var kit = EffectTestKit.Create();
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 1));
        world.Tick();

        world.EnqueueSetBase(new TargetId(1), attributeId: 99, value: 1);
        world.EnqueueSetBase(new TargetId(7), EffectTestKit.Attack, 1);
        world.EnqueueRemoveById(new TargetId(7), 1);
        world.EnqueueRemoveByTag(new TargetId(7), kit.Tag("effect.frost"));

        Assert.That(() => world.Tick(), Throws.Nothing);
    }

    [Test]
    public void Observation_buffers_hold_exactly_the_last_ticks_output()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInfinite("aura"));
        var abilityDef = new AbilityDef { Name = "cast", MaxLevel = 1 };
        abilityDef.Costs.Add(new CostDef
        {
            AttributeId = EffectTestKit.Mp,
            Magnitude = new MagnitudeDef { Base = Bun3.Gameplay.Attributes.Operand.Constant(5) },
        });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(abilityDef);
        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        var world = new World(kit.Database(abilities), new XorShiftRng(12345));
        var unit = MakeUnit(kit, 1);
        unit.Attributes.SetBase(EffectTestKit.Mp, 50);
        world.Spawn(unit);
        unit.Abilities.Grant(abilities.GetRequiredId("cast"), GrantSource.Archetype(), level: 1);
        world.Tick();
        Assert.That(world.PendingAttributeChanges.Length, Is.Zero, "pre-spawn initialization is not an event");

        world.EnqueueApply(kit.EffectId("aura"), unit.Id, unit.Id);
        world.EnqueueActivation(unit.Id, abilities.GetRequiredId("cast"));
        world.Tick();
        Assert.That(world.PendingEffectEvents.Length, Is.EqualTo(1), "aura Applied");
        Assert.That(world.PendingEffectEvents[0].Target, Is.EqualTo(unit.Id));
        Assert.That(world.PendingAttributeChanges.Length, Is.EqualTo(1), "Mp cost paid inside the tick");
        Assert.That(world.PendingAttributeChanges[0].Target, Is.EqualTo(unit.Id));
        Assert.That(world.PendingAttributeChanges[0].AttributeId, Is.EqualTo(EffectTestKit.Mp));
        Assert.That(world.PendingActivations.Length, Is.EqualTo(1));

        world.Tick();
        Assert.That(world.PendingEffectEvents.Length, Is.Zero);
        Assert.That(world.PendingAttributeChanges.Length, Is.Zero);
        Assert.That(world.PendingActivations.Length, Is.Zero);
        Assert.That(world.Pipeline.PendingGameplayEvents.Length, Is.Zero);
    }

    [Test]
    public void Attribute_changes_of_several_units_arrive_in_tick_order_with_their_targets()
    {
        var kit = EffectTestKit.Create();
        var world = new World(kit.Database(new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog)), new XorShiftRng(1));
        var first = MakeUnit(kit, 1);
        var second = MakeUnit(kit, 2);
        world.Spawn(first);
        world.Spawn(second);
        world.Tick();

        world.EnqueueSetBase(second.Id, EffectTestKit.Mp, 5);
        world.EnqueueSetBase(first.Id, EffectTestKit.Mp, 9);
        world.Tick();

        var changes = world.PendingAttributeChanges;
        Assert.That(changes.Length, Is.EqualTo(2));
        Assert.That(changes[0].Target, Is.EqualTo(second.Id));
        Assert.That(changes[0].NewCurrent, Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)5));
        Assert.That(changes[1].Target, Is.EqualTo(first.Id));
    }

    [Test]
    public void A_unit_spawned_this_tick_records_the_commands_of_the_same_tick()
    {
        var kit = EffectTestKit.Create();
        var world = new World(kit.Database(new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog)), new XorShiftRng(1));
        var unit = MakeUnit(kit, 1);

        world.Spawn(unit);
        world.EnqueueSetBase(unit.Id, EffectTestKit.Mp, 4);
        world.Tick();

        Assert.That(world.PendingAttributeChanges.Length, Is.EqualTo(1));
        Assert.That(world.PendingAttributeChanges[0].Target, Is.EqualTo(unit.Id));
    }

    [Test]
    public void A_despawned_unit_no_longer_records_into_the_world()
    {
        var kit = EffectTestKit.Create();
        var world = new World(kit.Database(new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog)), new XorShiftRng(1));
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();
        world.Despawn(unit.Id);
        world.Tick();

        unit.Attributes.SetBase(EffectTestKit.Mp, 3);

        Assert.That(world.PendingAttributeChanges.Length, Is.Zero);
    }

    [Test]
    public void Gameplay_events_are_readable_from_the_world()
    {
        var kit = EffectTestKit.Create();
        var bolt = EffectTestKit.MinimalInstant("bolt");
        bolt.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = Bun3.Gameplay.Attributes.AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Bun3.Gameplay.Attributes.Operand.Constant(-5) },
        });
        bolt.RaiseEvents.Add(new RaiseEventDef { Tag = "event.damage.taken", AttributeId = EffectTestKit.Hp });
        kit.AddEffect(bolt);
        var world = MakeWorld(kit);
        var unit = MakeUnit(kit, 1);
        unit.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unit.Attributes.SetBase(EffectTestKit.Hp, 100);
        world.Spawn(unit);

        world.EnqueueApply(kit.EffectId("bolt"), unit.Id, unit.Id);
        world.Tick();

        Assert.That(world.PendingGameplayEvents.Length, Is.EqualTo(1));
        Assert.That(world.PendingGameplayEvents[0].Target, Is.EqualTo(unit.Id));
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
        world = new World(kit.Database(abilities), new XorShiftRng(12345));
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
