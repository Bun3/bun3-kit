#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
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
        internal static ScenarioWorld BuildWorld(int seed)
        {
            var tagCatalog = TagCatalog.Create(new List<string>(), new List<TagCatalog.ParsedRedirect>());

            var registryBuilder = new AttributeRegistryBuilder();
            registryBuilder.Register(MaxHp, min: Operand.Constant(BigNum.FromParts(1, 0)));
            registryBuilder.Register(Hp, min: Operand.Constant(BigNum.Zero), max: Operand.Attribute(MaxHp));
            registryBuilder.Register(Attack, min: Operand.Constant(BigNum.Zero));
            var registry = registryBuilder.Build();

            var catalogBuilder = new EffectCatalogBuilder();
            AddEffects(catalogBuilder);
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

            return new ScenarioWorld(catalog, resolver, targets, pipeline, driverRng);
        }

        /// <summary>Advances the world by the given ticks. Each tick mixes in one random apply/dispel action,
        /// calls Tick(), then drains the event/change buffers.</summary>
        internal static void RunTicks(ScenarioWorld world, int ticks)
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
                    hash = Fold(hash, instance.EffectId);
                    hash = Fold(hash, instance.RemainingTicks);
                    hash = Fold(hash, instance.PeriodCountdown);
                    hash = Fold(hash, instance.Stack);
                    hash = Fold(hash, instance.Level);
                    hash = Fold(hash, instance.Enabled ? 1 : 0);
                }
            }

            return hash;
        }

        private static void DriveOneTick(ScenarioWorld world)
        {
            var roll = world.DriverRng.NextUInt32() % 4;
            if (roll == 0)
            {
                var effectId = world.DriverRng.NextUInt32() % (uint)world.Catalog.Count;
                var source = world.Targets[world.DriverRng.NextUInt32() % TargetCount];
                var target = world.Targets[world.DriverRng.NextUInt32() % TargetCount];
                var level = 1 + (int)(world.DriverRng.NextUInt32() % 3);
                world.Pipeline.EnqueueApply((int)effectId, source.Id, target.Id, level);
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

        // ---- World-based scenario (real Bun3.Gameplay.World/Unit — distinct from ScenarioWorld
        // above) — exercises ability activation, event-driven chains, and ability grants through
        // effects, which the tagless mini universe above cannot reach. Shared by RunHash (built
        // from caller-supplied catalogs, so it works identically over a JSON/builder catalog and a
        // binary-round-tripped one) and by ScenarioKit (its own catalogs, for the reflection/grant
        // round-trip oracles).

        private const ushort Mp = 4;
        private const string EventHitTag = "event.hit";
        private const string CooldownTag = "cooldown.strike";
        private const string ArmedTag = "state.armed";
        private const string SlashAbilityName = "Slash";
        private const string WardAbilityName = "Ward";
        private const int ScenarioWorldTicks = 200;

        /// <summary>Assembles the World-based scenario catalogs and units, and returns them together
        /// with the world driving them. Used directly by the reflection/grant round-trip oracles, and
        /// as the catalog source for the binary-artifact scenario-hash equivalence check.</summary>
        internal static Kit ScenarioKit()
        {
            var tags = BuildScenarioTagCatalog();
            var attributes = BuildScenarioAttributeRegistry();
            var seams = new SeamRegistryBuilder().Build(tags);

            var effectBuilder = new EffectCatalogBuilder();
            AddScenarioEffects(effectBuilder);
            var effects = effectBuilder.Build(tags, seams, attributes);

            var abilityBuilder = new AbilityCatalogBuilder();
            AddScenarioAbilities(abilityBuilder);
            var abilities = abilityBuilder.Build(tags, seams, attributes, effects);

            var world = new World(effects, abilities, new XorShiftRng(NonZero(1)));
            var unitIds = SpawnScenarioUnits(world, attributes, tags);
            GrantScenarioBaseline(world, tags, abilities, unitIds, out var slashAbilityId);

            return new Kit(
                world, effects, abilities, tags, attributes, seams, unitIds, slashAbilityId,
                effects.GetRequiredId("Mark"), effects.GetRequiredId("Reflect"));
        }

        /// <summary>Runs the fixed World-based scenario (spawn 3 units, 200 ticks of deterministic
        /// ability activation / effect application / dispel) over caller-supplied catalogs and folds
        /// the final state into an FNV-1a 64-bit hash. Building the catalogs is the caller's job —
        /// the same catalog contents must come out of both the code-builder path and a
        /// binary-round-tripped one for the two hashes to agree.</summary>
        /// <param name="effects">Effect catalog to run against.</param>
        /// <param name="abilities">Ability catalog to run against.</param>
        /// <param name="tags">Tag catalog the units count owned tags with.</param>
        /// <param name="seed">Rng seed; the same seed must always produce the same hash.</param>
        /// <returns>State hash after the scenario.</returns>
        internal static ulong RunHash(EffectCatalog effects, AbilityCatalog abilities, TagCatalog tags, ulong seed)
        {
            var attributes = BuildScenarioAttributeRegistry();
            var world = new World(effects, abilities, new XorShiftRng(NonZero(seed + 1)));
            var unitIds = SpawnScenarioUnits(world, attributes, tags);
            GrantScenarioBaseline(world, tags, abilities, unitIds, out var slashAbilityId);

            var driverRng = new XorShiftRng(NonZero(seed));
            for (var t = 0; t < ScenarioWorldTicks; t++)
            {
                DriveOneWorldTick(world, driverRng, unitIds, slashAbilityId, effects.Count);
                world.Tick();
                ClearWorldChangeBuffers(world, unitIds);
            }

            return HashWorldState(world, abilities, unitIds);
        }

        /// <summary>Captures every unit's attribute Base and Current values, in a fixed order
        /// comparable across calls. Current is included deliberately - a modifier-only Duration/
        /// Infinite effect never touches Base, so Base alone cannot detect a stuck modifier left
        /// behind by a broken removal.</summary>
        internal static BigNum[] CaptureAttributes(Kit kit)
        {
            var result = new BigNum[kit.UnitIds.Length * ScenarioAttributeIds.Length * 2];
            var index = 0;
            for (var i = 0; i < kit.UnitIds.Length; i++)
            {
                kit.World.TryGetUnit(kit.UnitIds[i], out var unit);
                for (var a = 0; a < ScenarioAttributeIds.Length; a++)
                {
                    result[index++] = unit!.Attributes.GetBase(ScenarioAttributeIds[a]);
                    result[index++] = unit.Attributes.GetCurrent(ScenarioAttributeIds[a]);
                }
            }

            return result;
        }

        /// <summary>Captures every unit's effective level for every catalog ability, in a fixed order comparable across calls.</summary>
        internal static int[] CaptureGrants(Kit kit)
        {
            var result = new int[kit.UnitIds.Length * kit.Abilities.Count];
            var index = 0;
            for (var i = 0; i < kit.UnitIds.Length; i++)
            {
                kit.World.TryGetUnit(kit.UnitIds[i], out var unit);
                for (var abilityId = 0; abilityId < kit.Abilities.Count; abilityId++)
                {
                    result[index++] = unit!.Abilities.GetLevel(abilityId);
                }
            }

            return result;
        }

        /// <summary>Applies the "Reflect" effect from unit 0 onto unit 1 and from unit 1 onto unit 0
        /// (modifier-only, so it never touches Base), then ticks the world forward.</summary>
        internal static void RunMutualReflection(Kit kit, int ticks)
        {
            var pipeline = kit.World.Pipeline;
            var a = kit.UnitIds[0];
            var b = kit.UnitIds[1];
            pipeline.EnqueueApply(kit.ReflectEffectId, a, b);
            pipeline.EnqueueApply(kit.ReflectEffectId, b, a);

            for (var i = 0; i < ticks; i++) kit.World.Tick();
        }

        /// <summary>Dispels every active effect instance on every unit.</summary>
        internal static void RemoveAllEffects(Kit kit)
        {
            var pipeline = kit.World.Pipeline;
            for (var i = 0; i < kit.UnitIds.Length; i++)
            {
                kit.World.TryGetUnit(kit.UnitIds[i], out var unit);
                var active = unit!.Effects.ActiveEffects;
                while (active.Count > 0)
                {
                    pipeline.RemoveById(unit.Id, active[0].Id);
                }
            }
        }

        /// <summary>Applies the "Mark" effect (grants the Ward ability) to the third unit and removes
        /// it again — the ability-grant hook must revoke exactly what it granted.</summary>
        internal static void ApplyGrantingEffectsAndRemoveThem(Kit kit)
        {
            var pipeline = kit.World.Pipeline;
            var target = kit.UnitIds[2];

            pipeline.EnqueueApply(kit.MarkEffectId, target, target);
            kit.World.Tick();

            kit.World.TryGetUnit(target, out var unit);
            var active = unit!.Effects.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].EffectId != kit.MarkEffectId) continue;
                pipeline.RemoveById(target, active[i].Id);
                break;
            }
        }

        private static readonly ushort[] ScenarioAttributeIds = { Hp, MaxHp, Attack, Mp };

        /// <summary>Name -> id map for <see cref="ScenarioAttributeIds"/>, for JSON-authored variants
        /// of the scenario catalogs (the binary-artifact equivalence test's JSON leg).</summary>
        internal static readonly IReadOnlyDictionary<string, ushort> ScenarioAttributeNames =
            new Dictionary<string, ushort> { ["Hp"] = Hp, ["MaxHp"] = MaxHp, ["Attack"] = Attack, ["Mp"] = Mp };

        private static TagCatalog BuildScenarioTagCatalog()
        {
            var names = new List<string> { EventHitTag, BuiltInSelectors.EventSourceTag, CooldownTag, ArmedTag };
            return TagCatalog.Create(names, new List<TagCatalog.ParsedRedirect>());
        }

        private static AttributeRegistry BuildScenarioAttributeRegistry()
        {
            var builder = new AttributeRegistryBuilder();
            builder.Register(MaxHp, min: Operand.Constant(BigNum.FromParts(1, 0)));
            builder.Register(Hp, min: Operand.Constant(BigNum.Zero), max: Operand.Attribute(MaxHp));
            builder.Register(Attack, min: Operand.Constant(BigNum.Zero));
            builder.Register(Mp, min: Operand.Constant(BigNum.Zero));
            return builder.Build();
        }

        // Damage/StrikeBack/Retaliate form the event-driven chain (application → RaiseEvents →
        // OnEvent chain → application); Cooldown/Buff/Regen exercise duration ticking, stacking, and
        // periodic Base changes; Reflect is modifier-only (Duration/Infinite never touch Base) for the
        // no-residue oracle; Mark grants an ability through an effect instance.
        private static void AddScenarioEffects(EffectCatalogBuilder builder)
        {
            builder.Add(new EffectDef
            {
                Name = "Strike",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-8)) },
                RaiseEvents = { new RaiseEventDef { Tag = EventHitTag, AttributeId = Hp } },
            });

            builder.Add(new EffectDef
            {
                Name = "StrikeBack",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-4)) },
            });

            builder.Add(new EffectDef
            {
                Name = "Retaliate",
                DurationType = EffectDurationType.Infinite,
                Chains =
                {
                    new ChainEdgeDef
                    {
                        Trigger = ChainTrigger.OnEvent,
                        EffectName = "StrikeBack",
                        EventTag = EventHitTag,
                        Listen = ChainListen.AsTarget,
                        SelectorTag = BuiltInSelectors.EventSourceTag,
                        LevelRule = ChainLevelRule.Inherit,
                    },
                },
            });

            builder.Add(new EffectDef
            {
                Name = "Cooldown",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 15,
                GrantedTags = { CooldownTag },
            });

            builder.Add(new EffectDef
            {
                Name = "Regen",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 40,
                PeriodTicks = 4,
                Modifiers = { AddModifier(Hp, Constant(3)) },
            });

            builder.Add(new EffectDef
            {
                Name = "Buff",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 25,
                Stack = new StackPolicyDef
                {
                    MaxStack = 4,
                    OnReapply = StackReapply.AddStack,
                    AddStackCount = 1,
                    RefreshDurationOnReapply = true,
                    OnExpiration = StackExpiration.ClearAll,
                    OnOverflow = StackOverflow.Deny,
                },
                Modifiers = { AddModifier(Attack, Constant(2)) },
            });

            builder.Add(new EffectDef
            {
                Name = "Reflect",
                DurationType = EffectDurationType.Infinite,
                Modifiers = { AddModifier(Attack, Constant(6)) },
            });

            builder.Add(new EffectDef
            {
                Name = "Mark",
                DurationType = EffectDurationType.Infinite,
                GrantedAbilities = { WardAbilityName },
            });
        }

        private static void AddScenarioAbilities(AbilityCatalogBuilder builder)
        {
            var slash = new AbilityDef { Name = SlashAbilityName, MaxLevel = 1, CooldownEffect = "Cooldown" };
            slash.RequiredTags.Add(ArmedTag);
            slash.BlockedTags.Add(CooldownTag);
            slash.Costs.Add(new CostDef { AttributeId = Mp, Magnitude = new MagnitudeDef { Base = Constant(5) } });
            slash.Effects.Add(new AbilityEffectDef { EffectName = "Strike" });
            builder.Add(slash);

            builder.Add(new AbilityDef { Name = WardAbilityName, MaxLevel = 1 });
        }

        private static TargetId[] SpawnScenarioUnits(World world, AttributeRegistry attributes, TagCatalog tags)
        {
            var ids = new TargetId[TargetCount];
            Span<ushort> attributeIds = stackalloc ushort[] { Hp, MaxHp, Attack, Mp };
            for (var i = 0; i < TargetCount; i++)
            {
                var id = new TargetId((ulong)(i + 1));
                var unit = new Unit(id, attributes, attributeIds, tags);
                unit.Attributes.SetBase(MaxHp, BigNum.FromParts(1000, 0));
                unit.Attributes.SetBase(Hp, BigNum.FromParts(1000, 0));
                unit.Attributes.SetBase(Attack, BigNum.FromParts(20, 0));
                unit.Attributes.SetBase(Mp, BigNum.FromParts(500, 0));
                world.Spawn(unit);
                ids[i] = id;
            }

            world.Tick();
            return ids;
        }

        private static void GrantScenarioBaseline(
            World world, TagCatalog tags, AbilityCatalog abilities, TargetId[] unitIds, out int slashAbilityId)
        {
            var armedTag = tags.GetRequired(ArmedTag);
            slashAbilityId = abilities.GetRequiredId(SlashAbilityName);
            for (var i = 0; i < unitIds.Length; i++)
            {
                world.TryGetUnit(unitIds[i], out var unit);
                unit!.Tags.Add(armedTag);
                unit.Abilities.Grant(slashAbilityId, GrantSource.Archetype(), level: 1);
            }
        }

        private static void DriveOneWorldTick(
            World world, XorShiftRng rng, TargetId[] unitIds, int slashAbilityId, int effectCount)
        {
            var unitCount = (uint)unitIds.Length;
            var roll = rng.NextUInt32() % 4;
            if (roll == 0)
            {
                world.TryActivateAbility(unitIds[rng.NextUInt32() % unitCount], slashAbilityId);
            }
            else if (roll == 1)
            {
                var effectId = (int)(rng.NextUInt32() % (uint)effectCount);
                var source = unitIds[rng.NextUInt32() % unitCount];
                var target = unitIds[rng.NextUInt32() % unitCount];
                var level = 1 + (int)(rng.NextUInt32() % 2);
                world.Pipeline.EnqueueApply(effectId, source, target, level);
            }
            else if (roll == 2)
            {
                var unitId = unitIds[rng.NextUInt32() % unitCount];
                world.TryGetUnit(unitId, out var unit);
                var active = unit!.Effects.ActiveEffects;
                if (active.Count > 0)
                {
                    var index = (int)(rng.NextUInt32() % (uint)active.Count);
                    world.Pipeline.RemoveById(unitId, active[index].Id);
                }
            }
            // roll == 3: no action this tick — lets duration/period timers advance untouched.
        }

        private static void ClearWorldChangeBuffers(World world, TargetId[] unitIds)
        {
            for (var i = 0; i < unitIds.Length; i++)
            {
                world.TryGetUnit(unitIds[i], out var unit);
                unit!.Attributes.ClearChanges();
                unit.Effects.ClearEffectEvents();
            }
        }

        private static ulong HashWorldState(World world, AbilityCatalog abilities, TargetId[] unitIds)
        {
            var hash = FnvOffset;
            for (var i = 0; i < unitIds.Length; i++)
            {
                world.TryGetUnit(unitIds[i], out var unit);
                var u = unit!;
                for (var a = 0; a < ScenarioAttributeIds.Length; a++)
                {
                    var b = u.Attributes.GetBase(ScenarioAttributeIds[a]);
                    var c = u.Attributes.GetCurrent(ScenarioAttributeIds[a]);
                    hash = Fold(hash, b.Mantissa);
                    hash = Fold(hash, b.Exponent);
                    hash = Fold(hash, c.Mantissa);
                    hash = Fold(hash, c.Exponent);
                }

                // Ascending instance id: List<T>.Add appends (ids are assigned monotonically) and
                // RemoveActive uses List<T>.Remove (shifts, never swap-removes), so the live list
                // stays a subsequence of creation order — no extra sort needed to keep the fold order
                // canonical.
                var active = u.Effects.ActiveEffects;
                for (var k = 0; k < active.Count; k++)
                {
                    var instance = active[k];
                    hash = Fold(hash, unchecked((long)instance.Id));
                    hash = Fold(hash, instance.EffectId);
                    hash = Fold(hash, instance.Level);
                    hash = Fold(hash, instance.Stack);
                    hash = Fold(hash, instance.RemainingTicks);
                    hash = Fold(hash, instance.PeriodCountdown);
                    hash = Fold(hash, instance.Enabled ? 1 : 0);
                }

                for (var abilityId = 0; abilityId < abilities.Count; abilityId++)
                {
                    hash = Fold(hash, u.Abilities.GetLevel(abilityId));
                }
            }

            return hash;
        }

        /// <summary>Assembled World-based scenario — catalogs, seams, attribute registry, the world
        /// driving them, and the unit/effect/ability ids the oracles above operate on.</summary>
        internal sealed class Kit
        {
            internal Kit(
                World world, EffectCatalog effects, AbilityCatalog abilities, TagCatalog tags,
                AttributeRegistry attributes, SeamRegistry seams, TargetId[] unitIds, int slashAbilityId,
                int markEffectId, int reflectEffectId)
            {
                World = world;
                Effects = effects;
                Abilities = abilities;
                Tags = tags;
                Attributes = attributes;
                Seams = seams;
                UnitIds = unitIds;
                SlashAbilityId = slashAbilityId;
                MarkEffectId = markEffectId;
                ReflectEffectId = reflectEffectId;
            }

            internal World World { get; }
            internal EffectCatalog Effects { get; }
            internal AbilityCatalog Abilities { get; }
            internal TagCatalog Tags { get; }
            internal AttributeRegistry Attributes { get; }
            internal SeamRegistry Seams { get; }
            internal TargetId[] UnitIds { get; }
            internal int SlashAbilityId { get; }
            internal int MarkEffectId { get; }
            internal int ReflectEffectId { get; }
        }

        // Six kinds (chains use trigger + follower effects) — instant damage, duration buff with
        // stacks, poison (duration + period), on-apply chain (follower fires on apply), ongoing
        // condition (attack bonus only at low health), chain bomb (explosion damage on duration
        // expiry). All tagless — GrantedTags/AssetTags/ImmunityTags are empty lists.
        private static void AddEffects(EffectCatalogBuilder builder)
        {
            builder.Add(new EffectDef
            {
                Name = "InstantDamage",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-30)) },
            });

            builder.Add(new EffectDef
            {
                Name = "Buff",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 20,
                Stack = new StackPolicyDef
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

            builder.Add(new EffectDef
            {
                Name = "Poison",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 30,
                PeriodTicks = 5,
                Modifiers = { AddModifier(Hp, Constant(-3)) },
            });

            builder.Add(new EffectDef
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

            builder.Add(new EffectDef
            {
                Name = "ChainFollow",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-5)) },
            });

            builder.Add(new EffectDef
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

            builder.Add(new EffectDef
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

            builder.Add(new EffectDef
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
        internal sealed class ScenarioWorld
        {
            internal ScenarioWorld(
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
