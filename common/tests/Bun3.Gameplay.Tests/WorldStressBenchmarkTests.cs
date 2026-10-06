#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;
using ExecutionContext = Bun3.Gameplay.Seams.ExecutionContext;

namespace Bun3.Gameplay.Tests;

/// <summary>
/// Battle-shaped load: every unit carries passives (stats, regen, an Ongoing berserk, thorns and rage
/// listeners) and six abilities (targeted, AoE, self buff, conditional execute, cleanse). A game-side
/// AI queries gates and queues activations; dead units are restored through queued commands.
/// 20 ticks = 1 second.
/// </summary>
[TestFixture]
[Explicit("Benchmark. Run in Release: dotnet test -c Release --filter FullyQualifiedName~WorldStressBenchmarkTests")]
public sealed class WorldStressBenchmarkTests
{
    private const ushort MaxHp = 1, Hp = 2, MaxMp = 3, Mp = 4, Attack = 5, Defense = 6, Speed = 7,
        CritChance = 8, CritDamage = 9, HpRegen = 10, MpRegen = 11;

    // Registered but undeclared attributes, as in a game with several unit kinds.
    private const int UnusedAttributes = 19;

    private static readonly string[] TagNames =
    {
        "state.stunned", "state.silenced", "state.burning", "state.poisoned", "state.hasted", "state.shielded",
        "state.berserk", "effect.buff", "effect.debuff", "effect.debuff.dot", "effect.debuff.cc",
        "event.damage.taken", "calc.execution.crit", "selector.nearby", "selector.event.source", "selector.event.target",
        "cooldown.attack", "cooldown.fireball", "cooldown.nova", "cooldown.haste", "cooldown.execute",
        "cooldown.guard", "cooldown.smite",
    };

    private static readonly string[] AbilityNames = { "attack", "fireball", "nova", "haste", "execute", "guard" };

    private static readonly string[] Passives = { "passive_stats", "regen", "berserk", "thorns", "rage_listener" };

    [Test]
    public void Normal_battle_reports_tick_cost_by_unit_count()
    {
        foreach (var units in new[] { 1_000, 2_500, 5_000, 10_000, 20_000 })
            Run("normal", units, thinkInterval: 5, cooldownDivisor: 1, warmupTicks: 600, measuredTicks: 400);
    }

    [Test]
    public void Intense_battle_reports_tick_cost_by_unit_count()
    {
        foreach (var units in new[] { 1_000, 2_500, 5_000, 10_000 })
            Run("intense", units, thinkInterval: 1, cooldownDivisor: 4, warmupTicks: 600, measuredTicks: 400);
    }

    // Realistic population: per world 3 heroes (one of HeroTypes, 8 gear effects, level-ups, an ability-granting
    // buff) and 60 mobs (one of MobTypes, small kits) that are despawned on death and replaced by new ids.
    private const int HeroesPerWorld = 3, MobsPerWorld = 60, EngagedMobs = 20, HeroTypes = 20, MobTypes = 10;

    [Test]
    public void Realistic_population_memory_by_unit_kind()
    {
        foreach (var (label, heroTypes, mobTypes, cycle) in new[] { ("shared types", HeroTypes, MobTypes, false), ("unique types", 600, 4_000, true) })
        {
            var database = BuildPopulationDatabase(heroTypes, mobTypes);
            var cursor = cycle ? new ArchetypeCursor() : null;
            var populations = new Population[50];
            for (var w = 0; w < populations.Length; w++) populations[w] = new Population(database, heroTypes, mobTypes, (uint)w + 1, cursor);
            for (var t = 0; t < 900; t++)
            {
                foreach (var population in populations) population.Step();
            }

            var databaseObjects = Reachable(database, null).Keys;
            var stop = new HashSet<object>(databaseObjects, ReferenceEqualityComparer.Instance);
            foreach (var population in populations)
            {
                var heroSide = Reachable(population.Heroes[0], stop);
                foreach (var shared in Reachable(population.Mobs[0], stop).Keys.Where(heroSide.ContainsKey).ToList()) stop.Add(shared);
            }

            long databaseBytes = 0;
            foreach (var value in databaseObjects) databaseBytes += ObjectSize(value);
            TestContext.Progress.WriteLine(
                $"POPMEM == {label} / database: {databaseBytes} B for {database.Archetypes.Count} archetypes " +
                $"({databaseBytes / (double)(populations.Length * (HeroesPerWorld + MobsPerWorld)):F0} B per live unit)");
            ReportUnitBytes(label + " / heroes", populations.SelectMany(x => x.Heroes), stop);
            ReportUnitBytes(label + " / mobs", populations.SelectMany(x => x.Mobs), stop);
        }
    }

    [Test]
    public void Realistic_population_tick_and_spawn_cost()
    {
        var database = BuildPopulationDatabase(HeroTypes, MobTypes);
        foreach (var worlds in new[] { 1, 50, 200, 1_000 }) RunPopulations("shared types", database, HeroTypes, MobTypes, worlds, null);
        RunPopulations("unique types", BuildPopulationDatabase(600, 4_000), 600, 4_000, 50, new ArchetypeCursor());
    }

    private static void RunPopulations(
        string label, GameplayDatabase database, int heroTypes, int mobTypes, int worlds, ArchetypeCursor? cursor)
    {
        const int measuredRounds = 300;
        var populations = new Population[worlds];
        for (var w = 0; w < worlds; w++) populations[w] = new Population(database, heroTypes, mobTypes, (uint)w + 1, cursor);

        void Rounds(int count)
        {
            for (var r = 0; r < count; r++)
            {
                foreach (var population in populations) population.Step();
            }
        }

        var warmStart = Stopwatch.GetTimestamp();
        do Rounds(300);
        while (Stopwatch.GetTimestamp() - warmStart < Stopwatch.Frequency * 3 / 2);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long spawnsBefore = 0;
        foreach (var population in populations) spawnsBefore += population.Spawns;
        var gen0 = GC.CollectionCount(0);
        var gen2 = GC.CollectionCount(2);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        Rounds(measuredRounds);
        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        long spawns = -spawnsBefore;
        foreach (var population in populations) spawns += population.Spawns;

        var units = (double)worlds * (HeroesPerWorld + MobsPerWorld);
        TestContext.Progress.WriteLine(
            $"POP {label,-12} {worlds,5} worlds ({units,6:F0} units) | {ms * 1_000_000 / (units * measuredRounds),5:F0} ns/unit per tick " +
            $"| spawns/tick {spawns / (double)measuredRounds,6:F1} | alloc/tick {allocated / measuredRounds,8} B " +
            $"({(spawns > 0 ? allocated / spawns : 0),5} B/spawn) | GC gen0 {GC.CollectionCount(0) - gen0} gen2 {GC.CollectionCount(2) - gen2}");
    }

    private static void ReportUnitBytes(string label, IEnumerable<Unit> units, HashSet<object> stop)
    {
        var byPath = new Dictionary<string, (long Bytes, long Count)>();
        long total = 0, objects = 0, count = 0;
        foreach (var unit in units)
        {
            count++;
            foreach (var pair in Reachable(unit, stop))
            {
                var size = ObjectSize(pair.Key);
                total += size;
                objects++;
                Accumulate(byPath, pair.Value, size);
            }
        }

        var n = (double)count;
        TestContext.Progress.WriteLine($"POPMEM == {label}: {total / n:F0} B/unit, {objects / n:F1} objects/unit (units {count})");
        foreach (var row in byPath.OrderByDescending(r => r.Value.Bytes).Take(12))
            TestContext.Progress.WriteLine($"POPMEM    {row.Value.Bytes / n,7:F0} B {row.Value.Count / n,6:F1} obj  {row.Key}");
    }

    private sealed class ArchetypeCursor
    {
        internal int Hero;
        internal int Mob;
    }

    private sealed class Population
    {
        internal readonly World World;
        internal readonly Unit[] Heroes = new Unit[HeroesPerWorld];
        internal readonly Unit[] Mobs = new Unit[MobsPerWorld];
        internal long Spawns;

        private readonly int _heroTypes, _mobTypes;
        private readonly ArchetypeCursor? _cursor;
        private readonly int _attack, _fireball, _nova, _haste, _execute, _guard, _smite;
        private readonly int[] _levelUpOrder;
        private readonly int[] _heroEffects;
        private readonly int _mobPassive, _empower;
        private readonly GameplayTag _debuff;
        private readonly Dictionary<ulong, int> _mobSlotById = new Dictionary<ulong, int>();
        private readonly int[] _heroLevelUps = new int[HeroesPerWorld];
        private readonly long[] _heroRespawnedAt = new long[HeroesPerWorld];
        private ulong _nextId;
        private long _frame;
        private uint _random;

        internal Population(GameplayDatabase database, int heroTypes, int mobTypes, uint seed, ArchetypeCursor? cursor)
        {
            World = new World(database, new XorShiftRng(seed));
            _heroTypes = heroTypes;
            _mobTypes = mobTypes;
            _cursor = cursor;
            _random = seed * 2654435761u | 1u;
            var abilities = database.Abilities;
            _attack = abilities.GetRequiredId("attack");
            _fireball = abilities.GetRequiredId("fireball");
            _nova = abilities.GetRequiredId("nova");
            _haste = abilities.GetRequiredId("haste");
            _execute = abilities.GetRequiredId("execute");
            _guard = abilities.GetRequiredId("guard");
            _smite = abilities.GetRequiredId("smite");
            _levelUpOrder = new[] { _nova, _haste, _execute, _fireball, _attack, _guard };
            var effects = database.Effects;
            _heroEffects = Passives.Concat(Enumerable.Range(0, 8).Select(g => "gear_" + g)).Select(effects.GetRequiredId).ToArray();
            _mobPassive = effects.GetRequiredId("mob_passive");
            _empower = effects.GetRequiredId("empower");
            _debuff = database.Tags.GetRequired("effect.debuff");

            for (var h = 0; h < HeroesPerWorld; h++) Heroes[h] = SpawnHero();
            for (var m = 0; m < MobsPerWorld; m++) PlaceMob(m);
            World.Tick();
        }

        internal void Step()
        {
            Think();
            World.Tick();
            Observe();
        }

        private uint Next()
        {
            _random ^= _random << 13;
            _random ^= _random >> 17;
            _random ^= _random << 5;
            return _random;
        }

        private Unit Spawn(int archetype)
        {
            var unit = World.CreateUnit(new TargetId(++_nextId), archetype);
            World.Spawn(unit);
            Spawns++;
            return unit;
        }

        private Unit SpawnHero()
        {
            var archetype = _cursor != null ? _cursor.Hero++ % _heroTypes : (int)(Next() % (uint)_heroTypes);
            var hero = Spawn(archetype);
            foreach (var effect in _heroEffects) World.EnqueueApply(effect, hero.Id, hero.Id);
            return hero;
        }

        private void PlaceMob(int slot)
        {
            var archetype = _heroTypes + (_cursor != null ? _cursor.Mob++ % _mobTypes : (int)(Next() % (uint)_mobTypes));
            var mob = Spawn(archetype);
            World.EnqueueApply(_mobPassive, mob.Id, mob.Id);
            Mobs[slot] = mob;
            _mobSlotById[mob.Id.Value] = slot;
        }

        private bool Ready(TargetId self, int ability, TargetId? target) =>
            World.CanActivateAbility(self, ability, target) == AbilityActivationResult.Success;

        private void Think()
        {
            for (var h = 0; h < HeroesPerWorld; h++)
            {
                var hero = Heroes[h].Id;
                if ((_frame + h * 97) % 300 == 0)
                {
                    var step = _heroLevelUps[h]++;
                    World.EnqueueGrant(hero, _levelUpOrder[step % _levelUpOrder.Length], GrantSource.Code(), Math.Min(5, 1 + step / _levelUpOrder.Length));
                }

                if ((_frame + h * 53) % 200 == 0) World.EnqueueApply(_empower, hero, hero);
                if ((_frame + h) % 5 != 0) continue;

                var target = Mobs[(int)(Next() % MobsPerWorld)].Id;
                if (Ready(hero, _guard, null)) World.EnqueueActivation(hero, _guard);
                else if (Ready(hero, _smite, target)) World.EnqueueActivation(hero, _smite, target);
                else if (Ready(hero, _execute, target)) World.EnqueueActivation(hero, _execute, target);
                else if (Ready(hero, _nova, null)) World.EnqueueActivation(hero, _nova);
                else if (Ready(hero, _fireball, target)) World.EnqueueActivation(hero, _fireball, target);
                else if (Ready(hero, _haste, null)) World.EnqueueActivation(hero, _haste);
                else World.EnqueueActivation(hero, _attack, target);
            }

            for (var m = (int)(_frame % 20); m < EngagedMobs; m += 20)
            {
                var mob = Mobs[m].Id;
                var target = Heroes[(int)(Next() % HeroesPerWorld)].Id;
                if (Ready(mob, _fireball, target)) World.EnqueueActivation(mob, _fireball, target);
                else if (Ready(mob, _nova, null)) World.EnqueueActivation(mob, _nova);
                else World.EnqueueActivation(mob, _attack, target);
            }
        }

        private void Observe()
        {
            foreach (var evt in World.PendingGameplayEvents) Check(evt.Target);
            for (var m = (int)(_frame % 20); m < MobsPerWorld; m += 20) CheckMob(m);
            for (var h = 0; h < HeroesPerWorld; h++)
            {
                if ((_frame + h) % 20 == 0) CheckHero(h);
            }

            _frame++;
        }

        private void Check(TargetId id)
        {
            if (_mobSlotById.TryGetValue(id.Value, out var slot))
            {
                CheckMob(slot);
                return;
            }

            for (var h = 0; h < HeroesPerWorld; h++)
            {
                if (Heroes[h].Id == id) CheckHero(h);
            }
        }

        private void CheckMob(int slot)
        {
            var mob = Mobs[slot];
            if (mob.Attributes.GetCurrent(Hp) != BigNum.Zero) return;
            World.Despawn(mob.Id);
            _mobSlotById.Remove(mob.Id.Value);
            PlaceMob(slot);
        }

        private void CheckHero(int h)
        {
            if (Heroes[h].Attributes.GetCurrent(Hp) != BigNum.Zero || _heroRespawnedAt[h] == _frame + 1) return;
            _heroRespawnedAt[h] = _frame + 1;
            var hero = Heroes[h].Id;
            World.EnqueueRemoveByTag(hero, _debuff);
            World.EnqueueSetBase(hero, Hp, 5_000);
            World.EnqueueSetBase(hero, Mp, 200);
        }
    }

    private sealed class NearbyCandidatesSelector : ITargetSelector
    {
        private readonly int _count;

        internal NearbyCandidatesSelector(int count) => _count = count;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            var candidates = ctx.CandidateCount;
            if (candidates == 0) return 0;
            var start = (int)(ctx.Origin.Value % (ulong)candidates);
            var written = 0;
            for (var i = 1; i <= _count && i < candidates && written < results.Length; i++)
            {
                var candidate = ctx.CandidateAt((start + i) % candidates);
                if (candidate != ctx.Origin) results[written++] = candidate;
            }

            return written;
        }
    }

    private static GameplayDatabase BuildPopulationDatabase(int heroTypes, int mobTypes)
    {
        var tags = LoadTags();
        var seams = new SeamRegistryBuilder();
        seams.RegisterExecutionCalc(tags.GetRequired("calc.execution.crit"), new CritCalc());
        seams.RegisterTargetSelector(tags.GetRequired("selector.nearby"), new NearbyCandidatesSelector(count: 8));

        var effects = Effects(cooldownDivisor: 1);
        var gearAttributes = new[] { Attack, Defense, MaxHp, CritChance, Speed, HpRegen, MpRegen };
        for (var g = 0; g < 10; g++)
        {
            var gear = Def("gear_" + g, EffectDurationType.Infinite);
            for (var m = 0; m < 2 + g % 2; m++) gear.Modifiers.Add(Add(gearAttributes[(g + m * 3) % gearAttributes.Length], 3 + g));
            effects.Add(gear);
        }

        var mobPassive = Def("mob_passive", EffectDurationType.Infinite);
        mobPassive.Modifiers.Add(Add(Attack, 5));
        mobPassive.Modifiers.Add(Add(Defense, 2));
        effects.Add(mobPassive);
        var empower = Def("empower", EffectDurationType.Duration, ticks: 100);
        empower.Modifiers.Add(Add(Attack, 10));
        empower.GrantedAbilities.Add("smite");
        empower.AssetTags.Add("effect.buff");
        effects.Add(empower);
        var smiteCooldown = Def("cd_smite", EffectDurationType.Duration, ticks: 60);
        smiteCooldown.GrantedTags.Add("cooldown.smite");
        effects.Add(smiteCooldown);

        var abilities = Abilities();
        foreach (var ability in abilities) ability.MaxLevel = 5;
        var smite = new AbilityDef { Name = "smite", MaxLevel = 5, Target = AbilityTargetMode.Required, CooldownEffect = "cd_smite" };
        smite.BlockedTags.Add("state.stunned");
        smite.Effects.Add(new AbilityEffectDef { EffectName = "hit", Origin = AbilityEffectOrigin.Target });
        smite.Effects.Add(new AbilityEffectDef { EffectName = "hit", Origin = AbilityEffectOrigin.Target });
        abilities.Add(smite);

        var archetypes = new List<ArchetypeDef>();
        var heroExtras = new[] { "nova", "haste", "execute" };
        for (var h = 0; h < heroTypes; h++)
        {
            var hero = new ArchetypeDef { Name = "hero_" + h };
            void Base(ushort attribute, BigNum value) => hero.Attributes.Add(new ArchetypeAttributeDef { AttributeId = attribute, Base = value });
            Base(MaxHp, 5_000 + h);
            Base(Hp, 5_000 + h);
            Base(MaxMp, 200);
            Base(Mp, 200);
            Base(Attack, 30 + h % 20);
            Base(Defense, 10);
            Base(Speed, 100);
            Base(CritChance, 0);
            Base(CritDamage, 150);
            Base(HpRegen, 15);
            Base(MpRegen, 8);
            Base((ushort)(100 + h % 19), 1);
            Base((ushort)(100 + (h + 7) % 19), 1);
            foreach (var name in new[] { "attack", "fireball", "guard", heroExtras[h % 3] })
                hero.Abilities.Add(new ArchetypeAbilityDef { AbilityName = name });
            archetypes.Add(hero);
        }

        for (var m = 0; m < mobTypes; m++)
        {
            var mob = new ArchetypeDef { Name = "mob_" + m };
            void Base(ushort attribute, BigNum value) => mob.Attributes.Add(new ArchetypeAttributeDef { AttributeId = attribute, Base = value });
            Base(MaxHp, 200 + m % 50 * 10);
            Base(Hp, 200 + m % 50 * 10);
            Base(MaxMp, 100);
            Base(Mp, 100);
            Base(Attack, 10 + m % 10);
            Base(Defense, 2);
            Base(Speed, 80);
            Base(CritChance, 0);
            Base(CritDamage, 150);
            mob.Abilities.Add(new ArchetypeAbilityDef { AbilityName = "attack" });
            if (m % 3 == 1) mob.Abilities.Add(new ArchetypeAbilityDef { AbilityName = "fireball" });
            if (m % 3 == 2)
            {
                mob.Abilities.Add(new ArchetypeAbilityDef { AbilityName = "nova" });
                mob.Abilities.Add(new ArchetypeAbilityDef { AbilityName = "haste" });
            }

            archetypes.Add(mob);
        }

        return GameplayDatabase.Build(tags, seams.Build(tags), BuildRegistry(), effects, abilities, archetypes);
    }

    [Test]
    public void Unit_memory_breakdown_by_path_and_type()
    {
        var database = BuildDatabase(cooldownDivisor: 1);
        ReportFootprint("fresh: archetype + 5 passives", new Battle(database, 200, thinkInterval: 5, seed: 7).Units);
        var fought = new Battle(database, 2_000, thinkInterval: 5, seed: 7);
        for (var t = 0; t < 600; t++) fought.Step();
        ReportFootprint("after 600 battle ticks", fought.Units);
    }

    [Test]
    public void Many_small_worlds_report_tick_cost()
    {
        var database = BuildDatabase(cooldownDivisor: 1);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            foreach (var worlds in new[] { 1, 20, 100, 200, 1_000 })
                RunWorlds(database, worlds, unitsPerWorld: 50, threads: 1, pinned: true);
            foreach (var threads in new[] { 4, 8 })
            {
                RunWorlds(database, 1_000, unitsPerWorld: 50, threads, pinned: true);
                RunWorlds(database, 1_000, unitsPerWorld: 50, threads, pinned: false);
            }
        }
    }

    // pinned: each thread owns a fixed slice of worlds; otherwise every round redistributes worlds dynamically.
    private static void RunWorlds(GameplayDatabase database, int worlds, int unitsPerWorld, int threads, bool pinned)
    {
        const int warmupRounds = 200, measuredRounds = 200;
        var warmupFloor = Stopwatch.Frequency * 3 / 2;
        var battles = new Battle[worlds];
        for (var w = 0; w < worlds; w++) battles[w] = new Battle(database, unitsPerWorld, thinkInterval: 5, seed: (uint)w + 1);

        void Round(int thread)
        {
            for (var w = thread; w < worlds; w += threads) battles[w].Step();
        }

        long RunRounds(int rounds)
        {
            var start = Stopwatch.GetTimestamp();
            if (threads == 1)
            {
                for (var r = 0; r < rounds; r++) Round(0);
            }
            else if (pinned)
            {
                using var barrier = new Barrier(threads);
                var workers = new Thread[threads];
                for (var t = 0; t < threads; t++)
                {
                    var thread = t;
                    workers[t] = new Thread(() =>
                    {
                        for (var r = 0; r < rounds; r++)
                        {
                            Round(thread);
                            barrier.SignalAndWait();
                        }
                    });
                    workers[t].Start();
                }

                foreach (var worker in workers) worker.Join();
            }
            else
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
                for (var r = 0; r < rounds; r++) Parallel.For(0, worlds, options, w => battles[w].Step());
            }

            return Stopwatch.GetTimestamp() - start;
        }

        for (long warmed = 0; warmed < warmupFloor;) warmed += RunRounds(warmupRounds);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var collections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        var elapsed = RunRounds(measuredRounds);
        collections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2) - collections;
        var ms = elapsed * 1000.0 / Stopwatch.Frequency;
        var units = (double)worlds * unitsPerWorld;
        var coreNsPerUnit = ms * 1_000_000 * threads / (units * measuredRounds);
        var mode = threads == 1 ? "" : pinned ? "pinned" : "dynamic";
        TestContext.Progress.WriteLine(
            $"WORLDS {worlds,5} x {unitsPerWorld} units, {threads} thread(s) {mode,-7} " +
            $"round {ms / measuredRounds,7:F2} ms | {coreNsPerUnit,6:F0} core-ns/unit per tick | total units {units,7:F0} | GCs {collections}");
    }

    private static readonly MethodInfo RawDataSize =
        typeof(RuntimeHelpers).GetMethod("GetRawObjectDataSize", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static long ObjectSize(object value) =>
        Math.Max(24, (long)(ulong)(nuint)RawDataSize.Invoke(null, new[] { value })! + 2 * IntPtr.Size);

    // Objects reachable from two different units (catalogs, registries) are shared and not charged to a unit.
    private static void ReportFootprint(string label, Unit[] units)
    {
        var first = Reachable(units[0], null);
        var second = Reachable(units[1], null);
        var shared = new HashSet<object>(first.Keys.Where(second.ContainsKey), ReferenceEqualityComparer.Instance);

        var byPath = new Dictionary<string, (long Bytes, long Count)>();
        var byType = new Dictionary<string, (long Bytes, long Count)>();
        long total = 0, objects = 0;
        foreach (var unit in units)
        {
            foreach (var pair in Reachable(unit, shared))
            {
                var size = ObjectSize(pair.Key);
                total += size;
                objects++;
                Accumulate(byPath, pair.Value, size);
                Accumulate(byType, Friendly(pair.Key.GetType()), size);
            }
        }

        var n = (double)units.Length;
        TestContext.Progress.WriteLine($"FOOT == {label}: {total / n:F0} B/unit, {objects / n:F1} objects/unit (units {units.Length})");
        foreach (var row in byPath.OrderByDescending(r => r.Value.Bytes).Take(30))
            TestContext.Progress.WriteLine($"FOOT path {row.Value.Bytes / n,7:F0} B {row.Value.Count / n,6:F1} obj  {100.0 * row.Value.Bytes / total,5:F1}%  {row.Key}");
        foreach (var row in byType.OrderByDescending(r => r.Value.Bytes).Take(20))
            TestContext.Progress.WriteLine($"FOOT type {row.Value.Bytes / n,7:F0} B {row.Value.Count / n,6:F1} obj  {100.0 * row.Value.Bytes / total,5:F1}%  {row.Key}");
    }

    private static void Accumulate(Dictionary<string, (long Bytes, long Count)> map, string key, long size)
    {
        map.TryGetValue(key, out var current);
        map[key] = (current.Bytes + size, current.Count + 1);
    }

    private static Dictionary<object, string> Reachable(object root, HashSet<object>? stop)
    {
        var seen = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Value, string Path)>();
        queue.Enqueue((root, root.GetType().Name));
        while (queue.Count > 0)
        {
            var (value, path) = queue.Dequeue();
            if (seen.ContainsKey(value) || (stop != null && stop.Contains(value))) continue;
            seen.Add(value, path);
            var type = value.GetType();
            if (type.IsArray)
            {
                var element = type.GetElementType()!;
                if (element.IsPrimitive || element.IsEnum) continue;
                foreach (var item in (Array)value) Enqueue(item, path + "[]", queue);
            }
            else if (type != typeof(string))
            {
                foreach (var field in InstanceFields(type)) Enqueue(field.GetValue(value), path + "." + Clean(field.Name), queue);
            }
        }

        return seen;
    }

    private static void Enqueue(object? value, string path, Queue<(object Value, string Path)> queue)
    {
        if (value is null) return;
        var type = value.GetType();
        if (!type.IsValueType)
        {
            queue.Enqueue((value, path));
            return;
        }

        if (type.IsPrimitive || type.IsEnum) return;
        foreach (var field in InstanceFields(type)) Enqueue(field.GetValue(value), path + "." + Clean(field.Name), queue);
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return field;
        }
    }

    private static string Clean(string field) =>
        field.StartsWith("<", StringComparison.Ordinal) ? field.Substring(1, field.IndexOf('>') - 1) : field;

    private static string Friendly(Type type)
    {
        if (type.IsArray) return Friendly(type.GetElementType()!) + "[]";
        if (!type.IsGenericType) return type.Name;
        return type.Name.Substring(0, type.Name.IndexOf('`')) + "<" + string.Join(",", type.GetGenericArguments().Select(Friendly)) + ">";
    }

    private static void Run(string profile, int units, int thinkInterval, int cooldownDivisor, int warmupTicks, int measuredTicks)
    {
        var battle = new Battle(BuildDatabase(cooldownDivisor), units, thinkInterval, seed: 7);
        for (var t = 0; t < warmupTicks; t++) battle.Step();

        battle.ResetCounters();
        var tickTimes = new long[measuredTicks];
        long frameTotal = 0;
        var gen0 = GC.CollectionCount(0);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < measuredTicks; t++)
        {
            var frameStart = Stopwatch.GetTimestamp();
            battle.Think();
            var tickStart = Stopwatch.GetTimestamp();
            battle.World.Tick();
            tickTimes[t] = Stopwatch.GetTimestamp() - tickStart;
            battle.Observe();
            frameTotal += Stopwatch.GetTimestamp() - frameStart;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var collections = GC.CollectionCount(0) - gen0;

        long tickTotal = 0;
        foreach (var time in tickTimes) tickTotal += time;
        Array.Sort(tickTimes);
        var ms = 1000.0 / Stopwatch.Frequency;
        var tickAvg = tickTotal * ms / measuredTicks;
        var effects = 0;
        foreach (var unit in battle.Units) effects += unit.Effects.ActiveEffectCount;

        TestContext.Progress.WriteLine(
            $"STRESS {profile,-7} N={units,6} tick avg {tickAvg,7:F2} ms p95 {tickTimes[measuredTicks * 95 / 100] * ms,7:F2} " +
            $"max {tickTimes[measuredTicks - 1] * ms,7:F2} | frame avg {frameTotal * ms / measuredTicks,7:F2} ms " +
            $"| {tickAvg * 1_000_000 / units,6:F0} ns/unit | per tick: act {battle.Activations / measuredTicks,6} " +
            $"ok {battle.Successes / measuredTicks,6} events {battle.Events / measuredTicks,6} " +
            $"respawn {battle.Respawns / (double)measuredTicks,6:F1} | effects/unit {effects / (double)units,4:F1} " +
            $"| alloc {allocated} B gen0 {collections}");
    }

    private sealed class Battle
    {
        internal readonly World World;
        internal readonly Unit[] Units;
        internal long Activations, Successes, Events, Respawns;

        private readonly int _thinkInterval;
        private readonly int _attack, _fireball, _nova, _haste, _execute, _guard;
        private readonly GameplayTag _debuff;
        private readonly long[] _respawnedAt;
        private long _frame;

        internal Battle(GameplayDatabase database, int units, int thinkInterval, uint seed)
        {
            _thinkInterval = thinkInterval;
            World = new World(database, new XorShiftRng(seed));

            var fighter = database.Archetypes.GetRequiredId("fighter");
            Units = new Unit[units];
            for (var i = 0; i < units; i++)
            {
                Units[i] = World.CreateUnit(new TargetId((ulong)i + 1), fighter);
                World.Spawn(Units[i]);
            }

            World.Tick();
            foreach (var passive in Passives)
            {
                var effectId = database.Effects.GetRequiredId(passive);
                foreach (var unit in Units) World.EnqueueApply(effectId, unit.Id, unit.Id);
            }

            World.Tick();

            var abilities = database.Abilities;
            _attack = abilities.GetRequiredId("attack");
            _fireball = abilities.GetRequiredId("fireball");
            _nova = abilities.GetRequiredId("nova");
            _haste = abilities.GetRequiredId("haste");
            _execute = abilities.GetRequiredId("execute");
            _guard = abilities.GetRequiredId("guard");
            _debuff = database.Tags.GetRequired("effect.debuff");
            _respawnedAt = new long[units];
        }

        internal void ResetCounters() => Activations = Successes = Events = Respawns = 0;

        internal void Step()
        {
            Think();
            World.Tick();
            Observe();
        }

        // Game-side AI: each unit thinks every _thinkInterval ticks, querying gates in priority order.
        internal void Think()
        {
            var units = Units.Length;
            for (var i = (int)(_frame % _thinkInterval); i < units; i += _thinkInterval)
            {
                var self = Units[i].Id;
                var target = new TargetId(PickEnemy(i, units));
                if (Ready(self, _guard, null)) World.EnqueueActivation(self, _guard);
                else if (Ready(self, _execute, target)) World.EnqueueActivation(self, _execute, target);
                else if ((i & 3) == 0 && Ready(self, _nova, null)) World.EnqueueActivation(self, _nova);
                else if (Ready(self, _fireball, target)) World.EnqueueActivation(self, _fireball, target);
                else if (Ready(self, _haste, null)) World.EnqueueActivation(self, _haste);
                else World.EnqueueActivation(self, _attack, target);
            }

            for (var i = (int)(_frame % 20); i < units; i += 20)
            {
                if (Units[i].Attributes.GetCurrent(Hp) == BigNum.Zero) Respawn(i);
            }
        }

        internal void Observe()
        {
            foreach (var activation in World.PendingActivations)
            {
                Activations++;
                if (activation.Result == AbilityActivationResult.Success) Successes++;
            }

            var events = World.PendingGameplayEvents;
            Events += events.Length;
            foreach (var evt in events)
            {
                var index = (int)evt.Target.Value - 1;
                if (Units[index].Attributes.GetCurrent(Hp) == BigNum.Zero) Respawn(index);
            }

            _frame++;
        }

        private bool Ready(TargetId self, int ability, TargetId? target) =>
            World.CanActivateAbility(self, ability, target) == AbilityActivationResult.Success;

        private ulong PickEnemy(int index, int units)
        {
            var pick = (ulong)((index * 7_919L + _frame * 104_729L) % units);
            if (pick == (ulong)index) pick = (pick + 1) % (ulong)units;
            return pick + 1;
        }

        private void Respawn(int index)
        {
            if (_respawnedAt[index] == _frame + 1) return;
            _respawnedAt[index] = _frame + 1;
            var id = Units[index].Id;
            World.EnqueueRemoveByTag(id, _debuff);
            World.EnqueueSetBase(id, Hp, 1_000);
            World.EnqueueSetBase(id, Mp, 200);
            Respawns++;
        }
    }

    private sealed class CritCalc : IExecutionCalc
    {
        public void Execute(ref ExecutionContext ctx)
        {
            if (!ctx.HasSource) return;
            var hp = ctx.TargetAttr(Hp);
            if ((BigNum)(int)(ctx.Rng.NextUInt32() % 100) < ctx.SourceAttr(CritChance))
                ctx.WriteTarget(Hp, hp - ctx.SourceAttr(Attack));
            else
                ctx.WriteTarget(Hp, hp + ctx.TargetAttr(Defense) / 2);
        }
    }

    private sealed class NearbySelector : ITargetSelector
    {
        private readonly int _radius;

        internal NearbySelector(int radius) => _radius = radius;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            var count = (ulong)ctx.CandidateCount;
            var origin = ctx.Origin.Value - 1;
            var written = 0;
            for (var d = 1UL; d <= (ulong)_radius && written + 1 < results.Length; d++)
            {
                results[written++] = new TargetId((origin + d) % count + 1);
                results[written++] = new TargetId((origin + count - d) % count + 1);
            }

            return written;
        }
    }

    private static GameplayDatabase BuildDatabase(int cooldownDivisor)
    {
        var tags = LoadTags();
        var seams = new SeamRegistryBuilder();
        seams.RegisterExecutionCalc(tags.GetRequired("calc.execution.crit"), new CritCalc());
        seams.RegisterTargetSelector(tags.GetRequired("selector.nearby"), new NearbySelector(radius: 4));
        return GameplayDatabase.Build(
            tags, seams.Build(tags), BuildRegistry(), Effects(cooldownDivisor), Abilities(), new[] { Fighter() });
    }

    private static TagCatalog LoadTags()
    {
        var json = new StringBuilder("{\"schemaVersion\":1,\"tags\":[");
        for (var i = 0; i < TagNames.Length; i++)
            json.Append(i == 0 ? "" : ",").Append("{\"name\":\"").Append(TagNames[i]).Append("\"}");
        json.Append("]}");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToString()));
        return TagCatalogJson.Load(stream);
    }

    private static AttributeRegistry BuildRegistry()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(MaxHp, "MaxHp", min: Operand.Constant(1));
        builder.Register(Hp, "Hp", min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        builder.Register(MaxMp, "MaxMp", min: Operand.Constant(0));
        builder.Register(Mp, "Mp", min: Operand.Constant(0), max: Operand.Attribute(MaxMp));
        builder.Register(Attack, "Attack", min: Operand.Constant(0));
        builder.Register(Defense, "Defense", min: Operand.Constant(0));
        builder.Register(Speed, "Speed", min: Operand.Constant(0));
        builder.Register(CritChance, "CritChance", min: Operand.Constant(0), max: Operand.Constant(100));
        builder.Register(CritDamage, "CritDamage");
        builder.Register(HpRegen, "HpRegen");
        builder.Register(MpRegen, "MpRegen");
        for (var i = 0; i < UnusedAttributes; i++) builder.Register((ushort)(100 + i), "Unused" + i);
        return builder.Build();
    }

    private static ArchetypeDef Fighter()
    {
        var fighter = new ArchetypeDef { Name = "fighter" };
        void Base(ushort attribute, BigNum value) =>
            fighter.Attributes.Add(new ArchetypeAttributeDef { AttributeId = attribute, Base = value });
        Base(MaxHp, 1_000);
        Base(Hp, 1_000);
        Base(MaxMp, 200);
        Base(Mp, 200);
        Base(Attack, 30);
        Base(Defense, 10);
        Base(Speed, 100);
        Base(CritChance, 0);
        Base(CritDamage, 150);
        Base(HpRegen, 15);
        Base(MpRegen, 8);
        foreach (var ability in AbilityNames) fighter.Abilities.Add(new ArchetypeAbilityDef { AbilityName = ability });
        return fighter;
    }

    private static ModifierDef Modifier(ushort attribute, AttributeModifierOp op, Operand magnitude) =>
        new ModifierDef { AttributeId = attribute, Op = op, Magnitude = new MagnitudeDef { Base = magnitude } };

    private static ModifierDef Add(ushort attribute, BigNum value) =>
        Modifier(attribute, AttributeModifierOp.Add, Operand.Constant(value));

    private static EffectDef Def(string name, EffectDurationType type, int ticks = 0, int period = 0) =>
        new EffectDef { Name = name, DurationType = type, DurationTicks = ticks, PeriodTicks = period };

    private static ConditionDef Below(ushort attribute, ushort bound, BigNum fraction) => new ConditionDef
    {
        Left = Operand.Attribute(attribute), Op = ComparisonOp.Less, Right = Operand.Attribute(bound, fraction),
    };

    private static ChainEdgeDef OnDamageTaken(string effect, string? selector = null) => new ChainEdgeDef
    {
        Trigger = ChainTrigger.OnEvent, EffectName = effect, EventTag = "event.damage.taken",
        Listen = ChainListen.AsTarget, SelectorTag = selector,
    };

    private static List<EffectDef> Effects(int cooldownDivisor)
    {
        var effects = new List<EffectDef>();

        var stats = Def("passive_stats", EffectDurationType.Infinite);
        stats.Modifiers.Add(Add(Attack, 10));
        stats.Modifiers.Add(Add(Defense, 5));
        stats.Modifiers.Add(Add(CritChance, 10));
        effects.Add(stats);

        var regen = Def("regen", EffectDurationType.Infinite, period: 20);
        regen.Modifiers.Add(Modifier(Hp, AttributeModifierOp.Add, Operand.Attribute(HpRegen)));
        regen.Modifiers.Add(Modifier(Mp, AttributeModifierOp.Add, Operand.Attribute(MpRegen)));
        effects.Add(regen);

        var berserk = Def("berserk", EffectDurationType.Infinite);
        berserk.OngoingConditions.Add(Below(Hp, MaxHp, BigNum.FromParts(3, -1)));
        berserk.Modifiers.Add(Modifier(Attack, AttributeModifierOp.Multiply, Operand.Constant(BigNum.FromParts(5, -1))));
        berserk.GrantedTags.Add("state.berserk");
        effects.Add(berserk);

        var thorns = Def("thorns", EffectDurationType.Infinite);
        thorns.Chains.Add(OnDamageTaken("thorns_hit", BuiltInSelectors.EventSourceTag));
        effects.Add(thorns);
        var thornsHit = Def("thorns_hit", EffectDurationType.Instant);
        thornsHit.Modifiers.Add(Add(Hp, -2));
        effects.Add(thornsHit);

        var rageListener = Def("rage_listener", EffectDurationType.Infinite);
        rageListener.Chains.Add(OnDamageTaken("rage"));
        effects.Add(rageListener);
        var rage = Def("rage", EffectDurationType.Duration, ticks: 100);
        rage.Stack = new StackPolicyDef { MaxStack = 5, OnReapply = StackReapply.AddStack };
        rage.Modifiers.Add(Add(Attack, 2));
        rage.AssetTags.Add("effect.buff");
        effects.Add(rage);

        var hit = Def("hit", EffectDurationType.Instant);
        hit.Modifiers.Add(Modifier(Hp, AttributeModifierOp.Add, Operand.SourceAttribute(Attack, -1)));
        hit.Executions.Add(new ExecutionDef { CalcTag = "calc.execution.crit" });
        hit.RaiseEvents.Add(new RaiseEventDef { Tag = "event.damage.taken", AttributeId = Hp });
        effects.Add(hit);

        var burn = Def("burn", EffectDurationType.Duration, ticks: 100, period: 20);
        burn.Stack = new StackPolicyDef { MaxStack = 3, OnReapply = StackReapply.AddStack };
        burn.Modifiers.Add(Modifier(Hp, AttributeModifierOp.Add, Operand.SourceAttribute(Attack, BigNum.FromParts(-2, -1))));
        burn.GrantedTags.Add("state.burning");
        burn.AssetTags.Add("effect.debuff.dot");
        effects.Add(burn);

        var poison = Def("poison", EffectDurationType.Duration, ticks: 160, period: 20);
        poison.Stack = new StackPolicyDef { MaxStack = 5, OnReapply = StackReapply.AddStack };
        poison.Modifiers.Add(Add(Hp, -3));
        poison.GrantedTags.Add("state.poisoned");
        poison.AssetTags.Add("effect.debuff.dot");
        effects.Add(poison);

        var stun = Def("stun", EffectDurationType.Duration, ticks: 30);
        stun.ChanceToApply = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, -1)) };
        stun.GrantedTags.Add("state.stunned");
        stun.AssetTags.Add("effect.debuff.cc");
        effects.Add(stun);

        var haste = Def("haste", EffectDurationType.Duration, ticks: 200);
        haste.Modifiers.Add(Modifier(Speed, AttributeModifierOp.Multiply, Operand.Constant(BigNum.FromParts(3, -1))));
        haste.Modifiers.Add(Add(Attack, 5));
        haste.GrantedTags.Add("state.hasted");
        haste.AssetTags.Add("effect.buff");
        effects.Add(haste);

        var shield = Def("shield", EffectDurationType.Duration, ticks: 100);
        shield.Modifiers.Add(Add(Defense, 50));
        shield.GrantedTags.Add("state.shielded");
        shield.ImmunityTags.Add("effect.debuff.cc");
        shield.AssetTags.Add("effect.buff");
        effects.Add(shield);

        var cleanse = Def("cleanse", EffectDurationType.Instant);
        cleanse.RemoveOnApplyTags.Add("effect.debuff");
        effects.Add(cleanse);

        foreach (var (name, ticks) in new[] { ("attack", 20), ("fireball", 100), ("nova", 200), ("haste", 300), ("execute", 160), ("guard", 240) })
        {
            var cooldown = Def("cd_" + name, EffectDurationType.Duration, ticks: Math.Max(1, ticks / cooldownDivisor));
            cooldown.GrantedTags.Add("cooldown." + name);
            effects.Add(cooldown);
        }

        return effects;
    }

    private static List<AbilityDef> Abilities()
    {
        AbilityEffectDef On(string effect, AbilityEffectOrigin origin) =>
            new AbilityEffectDef { EffectName = effect, Origin = origin };
        AbilityEffectDef Around(string effect) => new AbilityEffectDef { EffectName = effect, SelectorTag = "selector.nearby" };
        CostDef Mana(int amount) => new CostDef { AttributeId = Mp, Magnitude = new MagnitudeDef { Base = Operand.Constant(amount) } };

        var attack = new AbilityDef { Name = "attack", MaxLevel = 1, Target = AbilityTargetMode.Required, CooldownEffect = "cd_attack" };
        attack.BlockedTags.Add("state.stunned");
        attack.Effects.Add(On("hit", AbilityEffectOrigin.Target));

        var fireball = new AbilityDef { Name = "fireball", MaxLevel = 1, Target = AbilityTargetMode.Required, CooldownEffect = "cd_fireball" };
        fireball.BlockedTags.AddRange(new[] { "state.stunned", "state.silenced" });
        fireball.Costs.Add(Mana(20));
        fireball.Effects.Add(On("hit", AbilityEffectOrigin.Target));
        fireball.Effects.Add(On("burn", AbilityEffectOrigin.Target));

        var nova = new AbilityDef { Name = "nova", MaxLevel = 1, CooldownEffect = "cd_nova" };
        nova.BlockedTags.AddRange(new[] { "state.stunned", "state.silenced" });
        nova.Costs.Add(Mana(40));
        nova.Effects.Add(Around("hit"));
        nova.Effects.Add(Around("poison"));
        nova.Effects.Add(Around("stun"));

        var haste = new AbilityDef { Name = "haste", MaxLevel = 1, CooldownEffect = "cd_haste" };
        haste.BlockedTags.AddRange(new[] { "state.stunned", "state.hasted" });
        haste.Costs.Add(Mana(15));
        haste.Effects.Add(On("haste", AbilityEffectOrigin.Caster));

        var execute = new AbilityDef { Name = "execute", MaxLevel = 1, Target = AbilityTargetMode.Required, CooldownEffect = "cd_execute" };
        execute.BlockedTags.Add("state.stunned");
        execute.TargetConditions.Add(Below(Hp, MaxHp, BigNum.FromParts(3, -1)));
        execute.Costs.Add(Mana(10));
        execute.Effects.Add(On("hit", AbilityEffectOrigin.Target));
        execute.Effects.Add(On("hit", AbilityEffectOrigin.Target));

        var guard = new AbilityDef { Name = "guard", MaxLevel = 1, CooldownEffect = "cd_guard" };
        guard.RequiredTags.Add("state.poisoned");
        guard.Costs.Add(Mana(25));
        guard.Effects.Add(On("cleanse", AbilityEffectOrigin.Caster));
        guard.Effects.Add(On("shield", AbilityEffectOrigin.Caster));

        return new List<AbilityDef> { attack, fireball, nova, haste, execute, guard };
    }
}
