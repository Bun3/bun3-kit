#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tests
{
    /// <summary>
    /// Determinism oracle — a pure function that assembles a tagless mini universe (attributes,
    /// stacks, periods, chains, conditions only) in code, runs interleaved apply/dispel/tick with a
    /// fixed seed, and folds the final state into an FNV-1a 64-bit hash.
    /// Linked into both the Unity player assembly (Bun3.Gameplay.Runtime.Tests) and the .NET test
    /// assembly, so it uses only framework runtime types — no authoring-only loaders
    /// (TagCatalogJson etc.), NUnit, or EffectTestKit.
    /// </summary>
    internal static class EffectScenario
    {
        private const ushort Hp = 1;
        private const ushort MaxHp = 2;
        private const ushort Attack = 3;

        private const int TargetCount = 3;

        /// <summary>Assembles the mini universe with a fixed seed, runs it for the given ticks, and returns the FNV-1a 64-bit hash of the final state.</summary>
        /// <param name="seed">Seed for the pipeline RNG and scenario driver RNG.</param>
        /// <param name="ticks">Number of pipeline ticks to advance.</param>
        internal static ulong Run(int seed, int ticks)
        {
            var world = BuildWorld(seed);
            RunTicks(world, ticks);
            return HashState(world.Targets);
        }

        /// <summary>Assembles the world without advancing it — exposed so snapshot/restore round-trip tests can intervene midway.</summary>
        internal static World BuildWorld(int seed)
        {
            var tagCatalog = TagCatalog.Create(new List<string>(), new List<TagCatalog.RedirectDefinition>());

            var registryBuilder = new AttributeRegistryBuilder();
            registryBuilder.Register(MaxHp, min: Operand.Constant(BigNum.FromParts(1, 0)));
            registryBuilder.Register(Hp, min: Operand.Constant(BigNum.Zero), max: Operand.Attribute(MaxHp));
            registryBuilder.Register(Attack, min: Operand.Constant(BigNum.Zero));
            var registry = registryBuilder.Build();

            var catalogBuilder = new EffectCatalogBuilder();
            AddSpecs(catalogBuilder);
            var seams = new SeamRegistryBuilder().Build(tagCatalog);
            var catalog = catalogBuilder.Build(tagCatalog, seams, registry);

            var targets = new EffectTarget[TargetCount];
            var targetIds = new List<TargetId>(TargetCount);
            Span<ushort> attributeIds = stackalloc ushort[] { Hp, MaxHp, Attack };
            for (var i = 0; i < TargetCount; i++)
            {
                var target = new EffectTarget(new TargetId((ulong)(i + 1)), registry, attributeIds, tagCatalog);
                target.Attributes.SetBase(MaxHp, BigNum.FromParts(1000, 0));
                target.Attributes.SetBase(Hp, BigNum.FromParts(1000, 0));
                target.Attributes.SetBase(Attack, BigNum.FromParts(50, 0));
                targets[i] = target;
                targetIds.Add(target.Id);
            }

            var resolver = new DictionaryResolver(targets, targetIds);
            var pipelineRngSeed = NonZero(unchecked((ulong)seed) + 1);
            var pipeline = new EffectPipeline(catalog, resolver, new XorShiftRng(pipelineRngSeed));
            var driverRng = new XorShiftRng(NonZero(unchecked((ulong)seed)));

            return new World(catalog, resolver, targets, pipeline, driverRng);
        }

        /// <summary>Advances the world by the given ticks. Each tick mixes in one random apply/dispel action,
        /// calls Tick(), then drains the event/change buffers.</summary>
        internal static void RunTicks(World world, int ticks)
        {
            for (var t = 0; t < ticks; t++)
            {
                DriveOneTick(world);
                world.Pipeline.Tick();

                for (var i = 0; i < world.Targets.Length; i++)
                {
                    world.Targets[i].Attributes.ClearChanges();
                    world.Targets[i].ClearEffectEvents();
                }
            }
        }

        /// <summary>Folds the targets' final state (attribute Base/Current, active instance fields) into FNV-1a 64-bit.</summary>
        internal static ulong HashState(EffectTarget[] targets)
        {
            var hash = FnvOffset;
            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                hash = FoldAttribute(hash, target, Hp);
                hash = FoldAttribute(hash, target, MaxHp);
                hash = FoldAttribute(hash, target, Attack);

                var active = target.ActiveEffects;
                for (var k = 0; k < active.Count; k++)
                {
                    var instance = active[k];
                    hash = Fold(hash, instance.SpecId);
                    hash = Fold(hash, instance.RemainingTicks);
                    hash = Fold(hash, instance.PeriodCountdown);
                    hash = Fold(hash, instance.Stack);
                    hash = Fold(hash, instance.Level);
                    hash = Fold(hash, instance.Enabled ? 1 : 0);
                }
            }

            return hash;
        }

        private static void DriveOneTick(World world)
        {
            var roll = world.DriverRng.NextUInt32() % 4;
            if (roll == 0)
            {
                var specId = world.DriverRng.NextUInt32() % (uint)world.Catalog.Count;
                var source = world.Targets[world.DriverRng.NextUInt32() % TargetCount];
                var target = world.Targets[world.DriverRng.NextUInt32() % TargetCount];
                var level = 1 + (int)(world.DriverRng.NextUInt32() % 3);
                world.Pipeline.EnqueueApply((int)specId, source.Id, target.Id, level);
            }
            else if (roll == 1)
            {
                var target = world.Targets[world.DriverRng.NextUInt32() % TargetCount];
                var active = target.ActiveEffects;
                if (active.Count > 0)
                {
                    var index = (int)(world.DriverRng.NextUInt32() % (uint)active.Count);
                    world.Pipeline.RemoveById(target.Id, active[index].Id);
                }
            }
            // roll 2-3: advance this tick with no apply/dispel — mixes in settle periods.
        }

        private static ulong FoldAttribute(ulong hash, EffectTarget target, ushort attributeId)
        {
            var b = target.Attributes.GetBase(attributeId);
            var c = target.Attributes.GetCurrent(attributeId);
            hash = Fold(hash, b.Mantissa);
            hash = Fold(hash, b.Exponent);
            hash = Fold(hash, c.Mantissa);
            hash = Fold(hash, c.Exponent);
            return hash;
        }

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static ulong Fold(ulong hash, long value)
        {
            var v = unchecked((ulong)value);
            for (var i = 0; i < 8; i++)
            {
                hash ^= v & 0xFF;
                hash *= FnvPrime;
                v >>= 8;
            }

            return hash;
        }

        private static ulong NonZero(ulong value) => value == 0 ? 1UL : value;

        // Six kinds (chains use trigger + follower specs) — instant damage, duration buff with
        // stacks, poison (duration + period), on-apply chain (follower fires on apply), ongoing
        // condition (attack bonus only at low health), chain bomb (explosion damage on duration
        // expiry). All tagless — GrantedTags/AssetTags/ImmunityTags are empty lists.
        private static void AddSpecs(EffectCatalogBuilder builder)
        {
            builder.Add(new EffectSpec
            {
                Name = "InstantDamage",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-30)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "Buff",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 20,
                Stack = new StackPolicy
                {
                    MaxStack = 5,
                    OnReapply = StackReapply.AddStack,
                    AddStackCount = 1,
                    RefreshDurationOnReapply = true,
                    OnExpiration = StackExpiration.ClearAll,
                    OnOverflow = StackOverflow.Deny,
                },
                Modifiers = { AddModifier(Attack, Constant(5)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "Poison",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 30,
                PeriodTicks = 5,
                Modifiers = { AddModifier(Hp, Constant(-3)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "ChainTrigger",
                DurationType = EffectDurationType.Instant,
                Chains =
                {
                    new ChainEdgeDef
                    {
                        Trigger = ChainTrigger.OnApplication,
                        EffectName = "ChainFollow",
                        LevelRule = ChainLevelRule.Inherit,
                    },
                },
            });

            builder.Add(new EffectSpec
            {
                Name = "ChainFollow",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-5)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "OngoingBuff",
                DurationType = EffectDurationType.Infinite,
                OngoingConditions =
                {
                    new ConditionDef
                    {
                        Left = Operand.Attribute(Hp),
                        Op = ComparisonOp.Less,
                        Right = Operand.Attribute(MaxHp, BigNum.FromParts(5, -1)),
                    },
                },
                Modifiers = { AddModifier(Attack, Constant(10)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "Bomb",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 10,
                Chains =
                {
                    new ChainEdgeDef
                    {
                        Trigger = ChainTrigger.OnCompleteNormal,
                        EffectName = "BombBlast",
                        LevelRule = ChainLevelRule.Inherit,
                    },
                },
            });

            builder.Add(new EffectSpec
            {
                Name = "BombBlast",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-20)) },
            });
        }

        private static Operand Constant(long value) => Operand.Constant(BigNum.FromParts(value, 0));

        private static ModifierDef AddModifier(ushort attributeId, Operand baseValue) => new ModifierDef
        {
            AttributeId = attributeId,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = baseValue },
        };

        private sealed class DictionaryResolver : IEffectTargetResolver
        {
            private readonly EffectTarget[] _targets;
            private readonly List<TargetId> _ids;

            internal DictionaryResolver(EffectTarget[] targets, List<TargetId> ids)
            {
                _targets = targets;
                _ids = ids;
            }

            public bool TryResolve(TargetId id, out EffectTarget? target)
            {
                for (var i = 0; i < _targets.Length; i++)
                {
                    if (_targets[i].Id == id)
                    {
                        target = _targets[i];
                        return true;
                    }
                }

                target = null;
                return false;
            }

            public IReadOnlyList<TargetId> TargetIds => _ids;
        }

        /// <summary>Assembled mini universe — exposes its values so snapshot/restore tests can intervene midway.
        /// <see cref="DriverRng"/> exposes an XorShiftRng (class, shared reference) field — callers
        /// must explicitly use <see cref="XorShiftRng.Clone"/> to preserve a stream state independently.
        /// <see cref="Pipeline"/> has an open setter so snapshot-restore replay tests can swap in a
        /// fresh pipeline with no queue residue (the pending queue is outside snapshot scope).</summary>
        internal sealed class World
        {
            internal World(
                EffectCatalog catalog, IEffectTargetResolver resolver, EffectTarget[] targets,
                EffectPipeline pipeline, XorShiftRng driverRng)
            {
                Catalog = catalog;
                Resolver = resolver;
                Targets = targets;
                Pipeline = pipeline;
                DriverRng = driverRng;
            }

            internal EffectCatalog Catalog { get; }
            internal IEffectTargetResolver Resolver { get; }
            internal EffectTarget[] Targets { get; }
            internal EffectPipeline Pipeline { get; set; }
            internal XorShiftRng DriverRng;
        }
    }
}
