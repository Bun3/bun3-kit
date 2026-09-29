using System;
using System.Runtime.CompilerServices;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class AllocationSmokeTests
{
    [Test]
    public void BigNum_ops_and_format_do_not_allocate()
    {
        var a = BigNum.FromParts(37, 28);
        var b = BigNum.FromParts(15, -1);
        Span<char> buffer = stackalloc char[64];

        // Warm-up (exclude JIT/static-init allocations)
        var warm = a * b + a - b / 3;
        warm.TryFormat(buffer, out _, BigNumFormat.Korean);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            var x = a * b + a - b / 3;
            x.TryFormat(buffer, out _, BigNumFormat.Korean);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, "heap allocation in BigNum op/format path");
    }

    [Test]
    public void Tag_queries_do_not_allocate()
    {
        var catalog = TagCatalogTestData.Load();
        var ghost = catalog.GetRequired("State.Dead.Ghost");
        var dead = catalog.GetRequired("State.Dead");
        var set = catalog.CreateContainer(8);
        var counts = catalog.CreateCountContainer(8);
        set.Add(ghost);
        counts.Add(ghost, 2);

        _ = MeasureTagQueries(set, counts, dead, 1, out _);
        var allocated = MeasureTagQueries(set, counts, dead, 100_000, out var hits);

        Assert.That(allocated, Is.Zero);
        Assert.That(hits, Is.EqualTo(400_000));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureTagQueries(
        TagContainer tags,
        TagCountContainer counts,
        GameplayTag query,
        int iterations,
        out int hits)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        hits = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (tags.Has(query)) hits++;
            if (counts.Has(query)) hits++;
            hits += counts.Count(query);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public void Exact_state_copy_does_not_allocate()
    {
        var catalog = TagCatalogTestData.Load();
        var ghost = catalog.GetRequired("State.Dead.Ghost");
        var rooted = catalog.GetRequired("State.Rooted");
        var tags = catalog.CreateContainer(2);
        var counts = catalog.CreateCountContainer(2);
        tags.Add(rooted);
        tags.Add(ghost);
        counts.Add(rooted, 4);
        counts.Add(ghost, 2);

        _ = MeasureExactCopies(tags, counts, 1, out _);
        var allocated = MeasureExactCopies(tags, counts, 100_000, out var copied);

        Assert.That(allocated, Is.Zero);
        Assert.That(copied, Is.EqualTo(400_000));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureExactCopies(
        TagContainer tags, TagCountContainer counts, int iterations, out int copied)
    {
        Span<GameplayTag> tagBuffer = stackalloc GameplayTag[64];
        Span<TagCountEntry> countBuffer = stackalloc TagCountEntry[64];
        var before = GC.GetAllocatedBytesForCurrentThread();
        copied = 0;
        for (var i = 0; i < iterations; i++)
        {
            copied += tags.CopyExactTags(tagBuffer);
            copied += counts.CopyExactEntries(countBuffer);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public void Reserved_tag_mutations_do_not_allocate()
    {
        var catalog = TagCatalogTestData.Load(TagCatalogTestData.BuildChainCatalog(8, 16, false));
        var leaves = new GameplayTag[8];
        for (var i = 0; i < leaves.Length; i++)
            leaves[i] = catalog.GetRequired(TagCatalogTestData.ChainLeaf(i, 16));
        var tags = catalog.CreateContainer(8);
        var counts = catalog.CreateCountContainer(8);

        RunCycles(tags, counts, leaves, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        RunCycles(tags, counts, leaves, 100);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(tags.ExactKindCount, Is.Zero);
        Assert.That(counts.ExactKindCount, Is.Zero);
        Assert.That(allocated, Is.Zero);
    }

    private static void RunCycles(
        TagContainer tags,
        TagCountContainer counts,
        GameplayTag[] leaves,
        int cycles)
    {
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            for (var i = 0; i < leaves.Length; i++)
            {
                tags.Add(leaves[i]);
                counts.Add(leaves[i]);
            }
            for (var i = 0; i < leaves.Length; i++)
            {
                tags.Remove(leaves[i]);
                counts.Remove(leaves[i]);
            }
        }
    }

    [Test]
    public void Settled_world_tick_with_event_listeners_allocates_nothing()
    {
        var (world, _, _, _) = BuildWorldWithEventListeners();

        // Settle: both units already carry an active "Listener" instance from setup; run enough
        // idle ticks that every phase (RebuildAll/EvaluateOngoing/RebuildToggled) has walked them
        // at least once before measuring, so no first-touch lazy state shows up as a false positive.
        for (var i = 0; i < 10; i++) world.Tick();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) world.Tick();
        var after = GC.GetAllocatedBytesForCurrentThread();

        var allocated = after - before;
        Assert.That(allocated, Is.Zero, "idle World.Tick() with active event listeners allocated on the heap");
    }

    [Test]
    public void Raising_events_on_a_settled_world_allocates_nothing()
    {
        var (world, damageEffectId, attackerId, defenderId) = BuildWorldWithEventListeners();

        // Settle by pumping the exact operation under measurement (apply -> event -> OnEvent chain
        // -> counter-application) 10 times first. Every buffer this path touches — the pipeline's
        // apply queue, and each unit's attribute-change and effect-event lists — is cleared every
        // tick but never shrunk, so 10 cycles is enough for each to reach its steady capacity; letting
        // that growth happen during the pre-measurement warm-up (rather than the measured region)
        // is exactly what keeps the measurement itself from tripping on a one-time list resize.
        for (var i = 0; i < 10; i++)
        {
            world.Pipeline.EnqueueApply(damageEffectId, attackerId, defenderId);
            world.Tick();
            ClearWorldBuffers(world, attackerId, defenderId);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            world.Pipeline.EnqueueApply(damageEffectId, attackerId, defenderId);
            world.Tick();
            ClearWorldBuffers(world, attackerId, defenderId);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        var allocated = after - before;
        Assert.That(allocated, Is.Zero, "raising a gameplay event on a settled world allocated on the heap");
    }

    // Attacker and Defender each carry an Infinite "Listener" effect (OnEvent/AsTarget) so that
    // applying Damage to either raises event.damage.taken and fires a counter-application (Echo)
    // back onto the attacker via the built-in EventSource selector — real event dispatch, not a
    // no-op catalog.
    private static (World World, int DamageEffectId, TargetId AttackerId, TargetId DefenderId) BuildWorldWithEventListeners()
    {
        var tags = EffectTestKit.LoadCatalog();
        var attributes = EffectTestKit.BuildAttributeRegistry();
        var seams = new SeamRegistryBuilder().Build(tags);

        var effectBuilder = new EffectCatalogBuilder();
        effectBuilder.Add(new EffectDef
        {
            Name = "Damage",
            DurationType = EffectDurationType.Instant,
            Modifiers =
            {
                new ModifierDef
                {
                    AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
                    Magnitude = new MagnitudeDef { Base = Operand.Constant(-5) },
                },
            },
            RaiseEvents = { new RaiseEventDef { Tag = "event.damage.taken", AttributeId = EffectTestKit.Hp } },
        });
        effectBuilder.Add(new EffectDef
        {
            Name = "Echo",
            DurationType = EffectDurationType.Instant,
            Modifiers =
            {
                new ModifierDef
                {
                    AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
                    Magnitude = new MagnitudeDef { Base = Operand.Constant(-1) },
                },
            },
        });
        effectBuilder.Add(new EffectDef
        {
            Name = "Listener",
            DurationType = EffectDurationType.Infinite,
            Chains =
            {
                new ChainEdgeDef
                {
                    Trigger = ChainTrigger.OnEvent,
                    EffectName = "Echo",
                    EventTag = "event.damage.taken",
                    Listen = ChainListen.AsTarget,
                    SelectorTag = BuiltInSelectors.EventSourceTag,
                    LevelRule = ChainLevelRule.Inherit,
                },
            },
        });
        var effects = effectBuilder.Build(tags, seams, attributes);
        var abilities = new AbilityCatalogBuilder().Build(tags, seams, attributes, effects);

        var world = new World(effects, abilities, new XorShiftRng(1));
        Span<ushort> attributeIds = stackalloc ushort[]
        {
            EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Attack, EffectTestKit.Mp, EffectTestKit.Resistance,
        };
        var attacker = new Unit(new TargetId(1), attributes, attributeIds, tags);
        var defender = new Unit(new TargetId(2), attributes, attributeIds, tags);
        attacker.Attributes.SetBase(EffectTestKit.MaxHp, 10_000);
        attacker.Attributes.SetBase(EffectTestKit.Hp, 10_000);
        defender.Attributes.SetBase(EffectTestKit.MaxHp, 10_000);
        defender.Attributes.SetBase(EffectTestKit.Hp, 10_000);
        world.Spawn(attacker);
        world.Spawn(defender);
        world.Tick();

        var listenerEffectId = effects.GetRequiredId("Listener");
        world.Pipeline.EnqueueApply(listenerEffectId, attacker.Id, attacker.Id);
        world.Pipeline.EnqueueApply(listenerEffectId, defender.Id, defender.Id);
        world.Tick();

        return (world, effects.GetRequiredId("Damage"), attacker.Id, defender.Id);
    }

    private static void ClearWorldBuffers(World world, TargetId attackerId, TargetId defenderId)
    {
        world.TryGetUnit(attackerId, out var attacker);
        world.TryGetUnit(defenderId, out var defender);
        attacker!.Attributes.ClearChanges();
        attacker.Effects.ClearEffectEvents();
        defender!.Attributes.ClearChanges();
        defender.Effects.ClearEffectEvents();
        world.Pipeline.ClearGameplayEvents();
    }
}
