# 슬라이스 3 개정 — 틱 경계 규칙과 관찰 API 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** World 틱 밖에서는 명령 적재와 조회만 가능하게 하고, 게임이 틱 결과(발동 결과·활성 이펙트·쿨다운·전투 이벤트·부여 어빌리티)를 무할당으로 읽을 수 있게 한다.

**Architecture:** `World`에 명령 큐 하나(FIFO)를 두고 `Tick()`의 ⓪-2 단계에서 처리한다. 관찰 버퍼(이펙트·속성 이벤트, GameplayEvent, 발동 결과)는 ⓪-1에서 프레임워크가 비운다. 조회는 값 복사 구조체로 돌려주고 풀링 인스턴스를 노출하지 않는다.

**Tech Stack:** C# 9 / netstandard2.1(`Bun3.Gameplay`), NUnit(net10 테스트 프로젝트), Unity UPM 패키지(`.meta` 필요).

**Spec:** `docs/superpowers/specs/2026-08-20-gameplay-slice3-ability-unit-world-design.md` §16 (근거: `docs/research/2026-09-29-gameplay-observation-api.md`)

## Global Constraints

- netstandard2.1 + C#9 블록 네임스페이스, 모든 public 멤버 영어 XML 문서, 빌드 경고 0.
- 코드 파일에 한글·레거시 이름 금지, 주석은 코드가 못 보여주는 계약만.
- 틱 핫패스 무할당(클로저·LINQ·스냅샷 복사·문자열 생성 금지). 명령 처리·버퍼 비움·조회도 무할당.
- 새 `.cs` 파일은 같은 폴더에 `.meta`(`fileFormatVersion: 2` + 32자리 hex `guid`)를 함께 만든다. 가능하면 기존 파일에 형제 타입으로 넣는다.
- 버전은 `Bun3.Gameplay` 0.14.0 유지(미퍼블리시). World·어빌리티 API 변경은 파괴 변경이어도 소비자 없음.
- 결정론 골든 해시(`EffectDeterminismTests`)는 의도된 의미 변경일 때만 같은 커밋에서 갱신한다.
- 검증: `dotnet build Bun3.sln`(경고 0), `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`.
- 커밋: gitmoji 제목 + Why/Changes/Impact/Results 본문, 트레일러 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`, `git commit -m "<제목>" -m "<본문>" -m "<트레일러>"`.

## Review Focus

1. **알 수 없는 유닛·미선언 속성에 대한 명령** — 처리 중 예외로 틱이 죽지 않아야 한다. 발동은 `UnknownUnit`, 코스트 속성 미선언은 `CostInsufficient`, Base 쓰기·제거는 무시. (Task 1·4 테스트)
2. **틱 도중(시섬 등) 적재된 명령** — 다음 틱에 정확히 한 번 처리된다. (Task 1 테스트)
3. **같은 프레임 Despawn 후 명령** — ⓪ 구조 변경이 먼저라 `UnknownUnit`/무시, 예외 없음. (Task 1 테스트)
4. **`CanActivateAbility`의 무부작용** — Base·태그·활성 이펙트·RNG·결과 버퍼 어느 것도 바뀌지 않는다. (Task 1 테스트)
5. **스냅샷 경계** — 명령이 남아 있으면 스냅샷 거부, 복원 뒤 `DurationTicks`·쿨다운 조회가 복원 전과 같다. (Task 1·2 테스트)

---

### Task 1: World 명령 큐 + 발동 적재·결과 버퍼·무부작용 조회

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/World.Commands.cs` (+ `World.Commands.cs.meta`)
- Modify: `common/src/com.bun3.gameplay/Runtime/World.cs` (Tick 순서, 스냅샷 가드, 부분 구성 문서)
- Modify: `common/src/com.bun3.gameplay/Runtime/World.Ability.cs` (`TryActivateAbility` 제거 → `EvaluateActivation(commit)`)
- Modify: `common/src/com.bun3.gameplay/Runtime/Abilities/AbilityActivationResult.cs` (`AbilityActivation` 구조체 추가, `CostInsufficient` 문서 Base로 정정)
- Modify: `common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.cs` (`Tick()` = 비공개 `RunPhases()` 호출로 분리)
- Modify tests: `AbilityActivationTests.cs`, `AbilityGrantSourceTests.cs`, `WorldTests.cs`, `AllocationSmokeTests.cs`, `common/src/com.bun3.gameplay/Tests/Runtime/EffectScenario.cs`

**Interfaces:**
- Produces:
  - `public void World.EnqueueApply(int effectId, TargetId source, TargetId target, int level = 1)`
  - `public void World.EnqueueActivation(TargetId unitId, int abilityId)`
  - `public AbilityActivationResult World.CanActivateAbility(TargetId unitId, int abilityId)`
  - `public ReadOnlySpan<AbilityActivation> World.PendingActivations`
  - `public int World.PendingCommandCount`
  - `public readonly struct AbilityActivation { TargetId UnitId; int AbilityId; AbilityActivationResult Result; }`
  - `internal void EffectPipeline.RunPhases()` (World가 명령 처리 뒤 호출)
  - private `World.Command` 구조체와 `CommandKind`(Task 4가 종류를 추가)

- [ ] **Step 1: 실패 테스트 작성** — `AbilityActivationTests`에 헬퍼와 새 테스트를 추가한다. 기존 `TryActivateAbility` 호출은 헬퍼로 바꾼다.

```csharp
    private static AbilityActivationResult Activate(Fixture f, TargetId unitId, int abilityId)
    {
        f.World.EnqueueActivation(unitId, abilityId);
        f.World.Tick();
        var results = f.World.PendingActivations;
        return results[results.Length - 1].Result;
    }

    [Test]
    public void Activation_is_queued_and_reported_after_the_tick()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.EnqueueActivation(f.Caster.Id, id);
        Assert.That(f.Victim.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100), "nothing runs outside the tick");
        Assert.That(f.World.PendingCommandCount, Is.EqualTo(1));

        f.World.Tick();

        Assert.That(f.World.PendingActivations.Length, Is.EqualTo(1));
        Assert.That(f.World.PendingActivations[0].UnitId, Is.EqualTo(f.Caster.Id));
        Assert.That(f.World.PendingActivations[0].AbilityId, Is.EqualTo(id));
        Assert.That(f.World.PendingActivations[0].Result, Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.PendingCommandCount, Is.Zero);
    }

    [Test]
    public void Can_activate_reports_the_gate_result_without_side_effects()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            ability.Costs.Add(new CostDef { AttributeId = EffectTestKit.Mp, Magnitude = new MagnitudeDef { Base = Operand.Constant(30) } });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.CanActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.CanActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)50));
        Assert.That(f.Caster.Effects.ActiveEffectCount, Is.Zero);
        Assert.That(f.World.PendingCommandCount, Is.Zero);
        Assert.That(f.World.Pipeline.PendingApplyCount, Is.Zero);
    }

    [Test]
    public void Commands_keep_call_order_across_kinds()
    {
        var f = Build((kit, b) =>
        {
            kit.AddEffect(EffectTestKit.MinimalInfinite("mark.a"));
            kit.AddEffect(EffectTestKit.MinimalInfinite("mark.b"));
            var ability = new AbilityDef { Name = "mark", MaxLevel = 1 };
            ability.Effects.Add(new AbilityEffectDef { EffectName = "mark.a" });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("mark");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.EnqueueApply(f.Kit.EffectId("mark.b"), f.Caster.Id, f.Caster.Id);
        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.Tick();

        var active = f.Caster.Effects.ActiveEffects;
        Assert.That(active[0].EffectId, Is.EqualTo(f.Kit.EffectId("mark.b")), "applied first, so the lower instance id");
        Assert.That(active[1].EffectId, Is.EqualTo(f.Kit.EffectId("mark.a")));
    }

    [Test]
    public void Activation_for_a_unit_despawned_this_frame_reports_unknown_unit()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.Despawn(f.Caster.Id);
        Assert.That(Activate(f, f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.UnknownUnit));
    }

    [Test]
    public void A_cost_on_an_attribute_the_unit_does_not_declare_is_insufficient_not_an_exception()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.Costs.Add(new CostDef { AttributeId = EffectTestKit.Resistance, Magnitude = new MagnitudeDef { Base = Operand.Constant(1) } });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        ReadOnlySpan<ushort> declared = stackalloc ushort[] { EffectTestKit.Hp, EffectTestKit.MaxHp };
        var bare = new Unit(new TargetId(3), EffectTestKit.BuildAttributeRegistry(), declared, f.Kit.TagCatalog);
        f.World.Spawn(bare);
        bare.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(Activate(f, bare.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
    }
```

(`AbilityCatalogBuilder`는 코스트 속성이 레지스트리에 등록됐는지만 본다. `Resistance`는 등록돼 있고 `bare`는 선언하지 않는다. 지금 코드에서는 `GetBase`가 `ArgumentOutOfRangeException`을 던진다.)

`WorldTests`에는 다음을 추가한다.

```csharp
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
        abilityDef.Effects.Add(new AbilityEffectDef { EffectName = "mark", SelectorTag = "selector.echo" });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(abilityDef);
        World? world = null;
        var echo = new EchoSelector(() => world!);
        kit.RegisterSelector("selector.echo", echo);
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
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilityActivationTests|FullyQualifiedName~WorldTests"`
Expected: 컴파일 오류(`EnqueueActivation`, `PendingActivations`, `CanActivateAbility`, `PendingCommandCount`, `World.EnqueueApply` 없음).

- [ ] **Step 3: 구현**

`AbilityActivationResult.cs`에 형제 구조체 추가, `CostInsufficient` 문서를 "exceeded the unit's Base value"로 정정:

```csharp
    /// <summary>Result of one queued ability activation, readable after the tick that processed it.</summary>
    public readonly struct AbilityActivation
    {
        internal AbilityActivation(TargetId unitId, int abilityId, AbilityActivationResult result)
        {
            UnitId = unitId;
            AbilityId = abilityId;
            Result = result;
        }

        /// <summary>Unit that requested the activation.</summary>
        public TargetId UnitId { get; }

        /// <summary>Requested ability id.</summary>
        public int AbilityId { get; }

        /// <summary>Gate outcome; <see cref="AbilityActivationResult.Success"/> means costs were paid and effects enqueued.</summary>
        public AbilityActivationResult Result { get; }
    }
```

`World.Commands.cs`(첫 줄 역할 주석, 네임스페이스 `Bun3.Gameplay`):

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;

namespace Bun3.Gameplay
{
    // Command queue: the only way to change simulation state from outside Tick.
    public sealed partial class World
    {
        private enum CommandKind : byte
        {
            Apply,
            Activate,
        }

        private struct Command
        {
            internal CommandKind Kind;
            internal TargetId Unit;
            internal TargetId Source;
            internal int Id;
            internal int Level;
        }

        private readonly List<Command> _commands = new List<Command>();
        private AbilityActivation[] _activations = new AbilityActivation[8];
        private int _activationCount;

        /// <summary>Commands waiting for the next <see cref="Tick"/>.</summary>
        public int PendingCommandCount => _commands.Count;

        /// <summary>Activation results recorded by the last <see cref="Tick"/>, in request order. Cleared
        /// at the start of the next tick.</summary>
        public ReadOnlySpan<AbilityActivation> PendingActivations => _activations.AsSpan(0, _activationCount);

        /// <summary>Queues an effect application. It enters the pipeline's apply queue during the next
        /// <see cref="Tick"/>, in call order relative to every other queued command.</summary>
        /// <param name="effectId">Effect id to apply.</param>
        /// <param name="source">Caster id.</param>
        /// <param name="target">Receiving unit id.</param>
        /// <param name="level">Effect level.</param>
        public void EnqueueApply(int effectId, TargetId source, TargetId target, int level = 1) =>
            _commands.Add(new Command { Kind = CommandKind.Apply, Unit = target, Source = source, Id = effectId, Level = level });

        /// <summary>Queues an ability activation. The result appears in <see cref="PendingActivations"/>
        /// after the next <see cref="Tick"/>.</summary>
        /// <param name="unitId">Activating unit.</param>
        /// <param name="abilityId">Compiled ability id.</param>
        public void EnqueueActivation(TargetId unitId, int abilityId) =>
            _commands.Add(new Command { Kind = CommandKind.Activate, Unit = unitId, Id = abilityId });

        // Commands queued while processing (e.g. from a selector) wait for the next tick.
        private void ProcessCommands()
        {
            var count = _commands.Count;
            for (var i = 0; i < count; i++)
            {
                var command = _commands[i];
                switch (command.Kind)
                {
                    case CommandKind.Apply:
                        Pipeline.EnqueueApply(command.Id, command.Source, command.Unit, command.Level);
                        break;
                    case CommandKind.Activate:
                        RecordActivation(command.Unit, command.Id, EvaluateActivation(command.Unit, command.Id, commit: true));
                        break;
                }
            }

            _commands.RemoveRange(0, count);
        }

        private void RecordActivation(TargetId unitId, int abilityId, AbilityActivationResult result)
        {
            if (_activationCount == _activations.Length) Array.Resize(ref _activations, _activations.Length * 2);
            _activations[_activationCount++] = new AbilityActivation(unitId, abilityId, result);
        }
    }
}
```

`World.Commands.cs.meta`:

```
fileFormatVersion: 2
guid: 4f6d2c8a91b34e0f8a7c5d1e2b3f6a90
```

`World.Ability.cs` — `TryActivateAbility`를 지우고 게이트 평가를 공유한다. 파일 머리 역할 주석은 한 줄로 줄인다.

```csharp
    // Ability activation gates, cost payment and effect dispatch.
    public sealed partial class World
    {
        /// <summary>Evaluates every activation gate without side effects — no cost is paid, no cooldown
        /// applied, no effect enqueued, no random number drawn. Safe to call outside <see cref="Tick"/>.</summary>
        /// <param name="unitId">Unit that would activate.</param>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <returns>The result a queued activation would get if processed now.</returns>
        public AbilityActivationResult CanActivateAbility(TargetId unitId, int abilityId) =>
            EvaluateActivation(unitId, abilityId, commit: false);

        private AbilityActivationResult EvaluateActivation(TargetId unitId, int abilityId, bool commit)
        {
            if (!TryGetUnit(unitId, out var unit) || unit is null) return AbilityActivationResult.UnknownUnit;
            if (!_abilities.Contains(abilityId)) return AbilityActivationResult.UnknownAbility;

            var level = unit.Abilities.GetLevel(abilityId);
            if (level == 0) return AbilityActivationResult.NotGranted;

            var ability = _abilities.GetAbility(abilityId);

            var required = ability.RequiredTags;
            for (var i = 0; i < required.Length; i++)
            {
                if (!unit.Tags.Has(required[i])) return AbilityActivationResult.MissingTag;
            }

            var blocked = ability.BlockedTags;
            for (var i = 0; i < blocked.Length; i++)
            {
                if (unit.Tags.Has(blocked[i])) return AbilityActivationResult.BlockedByTag;
            }

            if (!Pipeline.CheckConditions(ability.ActivationConditions, unit.Effects, unit.Effects, hasSource: true))
            {
                return AbilityActivationResult.ConditionFailed;
            }

            var costs = ability.Costs;
            Span<BigNum> amounts = stackalloc BigNum[costs.Length];
            for (var i = 0; i < costs.Length; i++)
            {
                if (!unit.Attributes.Has(costs[i].AttributeId)) return AbilityActivationResult.CostInsufficient;
                amounts[i] = Pipeline.ComputeMagnitude(
                    costs[i].Magnitude, unit.Effects, unit.Effects, hasSource: true, level);
                if (unit.Attributes.GetBase(costs[i].AttributeId) < amounts[i])
                {
                    return AbilityActivationResult.CostInsufficient;
                }
            }

            if (!commit) return AbilityActivationResult.Success;

            for (var i = 0; i < costs.Length; i++)
            {
                unit.Attributes.AddBase(costs[i].AttributeId, -amounts[i]);
            }

            if (ability.CooldownEffectId >= 0)
            {
                Pipeline.ApplyImmediate(ability.CooldownEffectId, unitId, unitId, level);
            }

            ApplyAbilityEffects(ability, unitId, level);
            return AbilityActivationResult.Success;
        }
```

`EffectPipeline.cs` — `Tick()` 본문을 `internal void RunPhases()`로 옮기고 `public void Tick() => RunPhases();`로 둔다(Task 6에서 비움이 앞에 붙는다).

`World.cs`:
- 클래스 XML 문서의 부분 구성을 "this file plus `World.Ability.cs` and `World.Commands.cs`"로, 그리고 "Outside Tick only queueing (Spawn, Despawn, Enqueue*) and read-only queries are allowed."를 덧붙인다.
- `Tick()`:

```csharp
        /// <summary>Applies queued structural changes, processes queued commands in call order, then
        /// runs the pipeline phases.</summary>
        public void Tick()
        {
            FlushStructuralChanges();
            _activationCount = 0;
            ProcessCommands();
            Pipeline.RunPhases();
        }
```

- `CreateSnapshot()`에 `if (_commands.Count > 0) throw new InvalidOperationException("Cannot snapshot a world with pending commands.");` 추가(구조 변경 검사 옆), `<exception>` 문서에 명령 큐 포함.

테스트 이주:
- `AbilityActivationTests`의 `f.World.TryActivateAbility(a, b)`를 `Activate(f, a, b)`로 바꾼다. 발동 전후 상태를 같은 틱에서 비교하던 단언은 "발동 결과가 기록된 틱 뒤" 상태로 읽는다(코스트는 차감된 채, 볼트는 적용된 채).
- `Cooldown_effect_blocks_the_next_activation_in_the_same_tick`: 두 번 적재 후 한 번 `Tick()`, `PendingActivations[0].Result == Success`, `[1].Result == BlockedByTag`.
- `Cost_is_deducted_immediately_so_repeat_activation_in_one_tick_fails`: 같은 방식(두 번 적재, 한 틱, `[1] == CostInsufficient`, Mp 20).
- `EffectScenario.DriveOneWorldTick`: `world.TryActivateAbility(...)` → `world.EnqueueActivation(...)`, `world.Pipeline.EnqueueApply(...)` → `world.EnqueueApply(...)`. (`RemoveById`는 Task 4에서 이주.)
- `AbilityGrantSourceTests`, `AllocationSmokeTests`에 `TryActivateAbility`가 있으면 같은 방식으로 이주한다.

- [ ] **Step 4: 통과 확인**

Run: `dotnet build Bun3.sln` → 경고 0. Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj` → 전체 통과, `EffectDeterminismTests`의 골든 해시 불변(발동이 다음 틱 ⓪-2에서 처리돼도 파이프라인 페이즈 기준 순서는 같다). 해시가 바뀌면 원인을 추적해 커밋 본문에 적고, 의도된 의미 변경일 때만 상수를 갱신한다.

- [ ] **Step 5: 커밋** — `✨ World 명령 큐: 발동을 틱 안에서 처리하고 결과를 PendingActivations로 보고`

---

### Task 2: 전체 지속시간 저장 + ActiveEffectInfo 조회

**Files:**
- Modify: `Runtime/Effects/EffectInstance.cs` (`DurationTicks`, `ActiveEffectInfo` 형제 구조체)
- Modify: `Runtime/Effects/EffectPipeline.cs` (지속시간 재설정 4곳)
- Modify: `Runtime/Effects/EffectTarget.cs` (`GetActiveEffect`, `TryGetActiveEffect`, 스냅샷 생성·복원)
- Modify: `Runtime/Effects/EffectTargetSnapshot.cs` (`InstanceRow.DurationTicks`)
- Test: `common/tests/Bun3.Gameplay.Tests/EffectDurationScaleTests.cs`, `WorldTests.cs`

**Interfaces:**
- Produces: `public int EffectInstance.DurationTicks`, `public readonly struct ActiveEffectInfo`(`InstanceId`, `EffectId`, `Source`, `Level`, `Stack`, `RemainingTicks`, `DurationTicks`, `Enabled`), `public ActiveEffectInfo EffectTarget.GetActiveEffect(int index)`, `public bool EffectTarget.TryGetActiveEffect(ulong instanceId, out ActiveEffectInfo info)`.

- [ ] **Step 1: 실패 테스트** (`EffectDurationScaleTests`)

```csharp
    [Test]
    public void Duration_ticks_keep_the_scaled_total_while_remaining_counts_down()
    {
        var kit = EffectTestKit.Create();
        var def = EffectTestKit.MinimalDuration("slow", ticks: 10);
        def.DurationScale = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, 0)) };
        kit.AddEffect(def);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("slow"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.Tick();

        var info = kit.Defender.GetActiveEffect(0);
        Assert.That(info.DurationTicks, Is.EqualTo(20));
        Assert.That(info.RemainingTicks, Is.EqualTo(19));
        Assert.That(info.EffectId, Is.EqualTo(kit.EffectId("slow")));
        Assert.That(info.Source, Is.EqualTo(kit.Attacker.Id));
        Assert.That(kit.Defender.TryGetActiveEffect(info.InstanceId, out var byId), Is.True);
        Assert.That(byId.DurationTicks, Is.EqualTo(20));
        Assert.That(kit.Defender.TryGetActiveEffect(info.InstanceId + 1000, out _), Is.False);
    }

    [Test]
    public void Extending_a_duration_sets_the_total_to_the_new_remaining()
    {
        var kit = EffectTestKit.Create();
        var def = EffectTestKit.MinimalDuration("slow", ticks: 10);
        def.Stack.OnReapply = StackReapply.ExtendCapped;
        kit.AddEffect(def);
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("slow"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        for (var i = 0; i < 4; i++) pipeline.Tick();
        pipeline.EnqueueApply(kit.EffectId("slow"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        // remaining 6 + new 10 = 16, capped at floor(10 x 1.3) = 13; the gauge's total follows the new lifetime.
        var info = kit.Defender.GetActiveEffect(0);
        Assert.That(info.RemainingTicks, Is.EqualTo(13));
        Assert.That(info.DurationTicks, Is.EqualTo(13));
    }
```

(같은 `EffectId`의 활성 인스턴스가 있으면 항상 병합된다 — `EffectPipeline.ApplyDurationOrInfinite`. 수명은 생성 틱에 줄지 않아서 4틱 뒤 남은 값이 6이다. 병합 틱의 남은 값이 13이 아니면 `AdvanceTime`의 감소 순서(드레인 ① 뒤 시간 ②)를 읽고, 기대값과 이 주석을 함께 고친다.)

`WorldTests`의 스냅샷 왕복 테스트 끝에 `Assert.That(unit.Effects.GetActiveEffect(0).DurationTicks, Is.EqualTo(<복원 전 값>))` 형태의 단언을 추가하되, 대상은 Duration 이펙트로 바꾼 별도 테스트(`Snapshot_round_trip_keeps_duration_totals`)로 만든다.

- [ ] **Step 2: 실패 확인** — `--filter "FullyQualifiedName~EffectDurationScaleTests|FullyQualifiedName~WorldTests"` → 컴파일 오류(`GetActiveEffect` 없음).

- [ ] **Step 3: 구현**
  - `EffectInstance`: `public int DurationTicks { get; internal set; }`(문서: "Total ticks of the current Duration lifetime — the scaled/DR value set at creation and on every refresh, extension or stack-drop refresh. -1 for Infinite/Instant."). `Rent`에서 `instance.DurationTicks = remainingTicks;`.
  - `EffectPipeline`: `instance.RemainingTicks = X;`로 새 총량을 주는 세 곳(ExtendCapped 결과, Refresh, RemoveOneAndRefresh)에 `instance.DurationTicks = instance.RemainingTicks;`를 바로 뒤에 추가한다. 틱마다 감소시키는 곳은 건드리지 않는다.
  - `EffectInstance.cs`에 형제 구조체:

```csharp
    /// <summary>Value copy of one active effect instance, safe to keep after the instance is pooled.</summary>
    public readonly struct ActiveEffectInfo
    {
        internal ActiveEffectInfo(EffectInstance instance)
        {
            InstanceId = instance.Id;
            EffectId = instance.EffectId;
            Source = instance.Source;
            Level = instance.Level;
            Stack = instance.Stack;
            RemainingTicks = instance.RemainingTicks;
            DurationTicks = instance.DurationTicks;
            Enabled = instance.Enabled;
        }

        /// <summary>Instance id.</summary>
        public ulong InstanceId { get; }

        /// <summary>Effect id.</summary>
        public int EffectId { get; }

        /// <summary>Caster that applied the effect.</summary>
        public TargetId Source { get; }

        /// <summary>Effect level.</summary>
        public int Level { get; }

        /// <summary>Stack count.</summary>
        public int Stack { get; }

        /// <summary>Remaining ticks of a Duration effect; -1 otherwise.</summary>
        public int RemainingTicks { get; }

        /// <summary>Total ticks of the current Duration lifetime; -1 otherwise.</summary>
        public int DurationTicks { get; }

        /// <summary>Whether the instance currently participates in aggregation.</summary>
        public bool Enabled { get; }
    }
```

  - `EffectTarget`:

```csharp
        /// <summary>Returns a value copy of the active instance at <paramref name="index"/> (ascending instance id).</summary>
        /// <param name="index">Index in [0, <see cref="ActiveEffectCount"/>).</param>
        public ActiveEffectInfo GetActiveEffect(int index) => new ActiveEffectInfo(_activeEffects[index]);

        /// <summary>Finds an active instance by id.</summary>
        /// <param name="instanceId">Instance id.</param>
        /// <param name="info">Value copy when found.</param>
        /// <returns>True when the instance is active on this target.</returns>
        public bool TryGetActiveEffect(ulong instanceId, out ActiveEffectInfo info)
        {
            for (var i = 0; i < _activeEffects.Count; i++)
            {
                if (_activeEffects[i].Id != instanceId) continue;
                info = new ActiveEffectInfo(_activeEffects[i]);
                return true;
            }

            info = default;
            return false;
        }
```

  - 스냅샷: `InstanceRow` 생성자·속성에 `int durationTicks` 추가, `CreateSnapshot`이 `instance.DurationTicks`를 싣고 `RestoreSnapshot`이 `instance.DurationTicks = row.DurationTicks;`로 되돌린다.

- [ ] **Step 4: 통과 확인** — 빌드 경고 0, 전체 테스트 통과, 골든 해시 불변(해시는 DurationTicks를 읽지 않는다).

- [ ] **Step 5: 커밋** — `✨ 활성 이펙트 값 복사 조회(ActiveEffectInfo)와 전체 지속시간 저장`

---

### Task 3: GAS식 쿨다운 게이트 + TryGetCooldown

**Files:**
- Modify: `Runtime/Abilities/AbilityActivationResult.cs` (`OnCooldown = 8`, `BlockedByTag` 문서에서 쿨다운 언급 제거)
- Modify: `Runtime/World.Ability.cs` (게이트 4번, `TryGetCooldown`)
- Modify: `Runtime/Abilities/AbilityCatalogBuilder.cs` (쿨다운 이펙트에 GrantedTags가 없으면 Build 오류)
- Test: `AbilityActivationTests.cs`, `AbilityCatalogBuilderTests.cs`

**Interfaces:**
- Consumes: Task 1 `EvaluateActivation`, `Activate` 헬퍼; Task 2 `EffectInstance.DurationTicks`.
- Produces: `AbilityActivationResult.OnCooldown`, `public bool World.TryGetCooldown(TargetId unitId, int abilityId, out int remainingTicks, out int durationTicks)`.

- [ ] **Step 1: 실패 테스트** (`AbilityActivationTests`)

```csharp
    [Test]
    public void Cooldown_gates_on_the_cooldown_effects_granted_tags_without_blocked_tags()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.EnqueueActivation(f.Caster.Id, id);
        f.World.Tick();

        Assert.That(f.World.PendingActivations[0].Result, Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.PendingActivations[1].Result, Is.EqualTo(AbilityActivationResult.OnCooldown));
    }

    [Test]
    public void A_shared_cooldown_tag_from_another_ability_blocks_and_is_reported()
    {
        var f = Build((kit, b) =>
        {
            var meteorCooldown = EffectTestKit.MinimalDuration("cooldown.meteor", ticks: 30);
            meteorCooldown.GrantedTags.Add("cooldown.fireball");
            kit.AddEffect(meteorCooldown);

            var fireball = Fireball();
            fireball.CooldownEffect = "cooldown.fireball";
            b.Add(fireball);

            var meteor = new AbilityDef { Name = "meteor", MaxLevel = 1, CooldownEffect = "cooldown.meteor" };
            meteor.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
            b.Add(meteor);
        });
        var fireballId = f.Abilities.GetRequiredId("fireball");
        var meteorId = f.Abilities.GetRequiredId("meteor");
        f.Caster.Abilities.Grant(fireballId, GrantSource.Archetype(), level: 1);
        f.Caster.Abilities.Grant(meteorId, GrantSource.Archetype(), level: 1);

        Assert.That(Activate(f, f.Caster.Id, meteorId), Is.EqualTo(AbilityActivationResult.Success));

        Assert.That(f.World.CanActivateAbility(f.Caster.Id, fireballId), Is.EqualTo(AbilityActivationResult.OnCooldown));
        Assert.That(f.World.TryGetCooldown(f.Caster.Id, fireballId, out var remaining, out var duration), Is.True);
        Assert.That(duration, Is.EqualTo(30));
        Assert.That(remaining, Is.EqualTo(30));
    }

    [Test]
    public void Try_get_cooldown_is_false_when_ready_or_without_a_cooldown_effect()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            b.Add(ability);
            var plain = new AbilityDef { Name = "plain", MaxLevel = 1 };
            plain.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
            b.Add(plain);
        });
        var id = f.Abilities.GetRequiredId("fireball");

        Assert.That(f.World.TryGetCooldown(f.Caster.Id, id, out _, out _), Is.False);
        Assert.That(f.World.TryGetCooldown(f.Caster.Id, f.Abilities.GetRequiredId("plain"), out _, out _), Is.False);
        Assert.That(f.World.TryGetCooldown(new TargetId(999), id, out _, out _), Is.False);
    }
```

`remaining`은 발동 틱 직후 값이다. 쿨다운은 ⓪-2에서 `ApplyImmediate`로 생성되고(`CreatedTick` = 이번 틱) 수명은 생성 틱에 줄지 않으므로 30이다.

`AbilityCatalogBuilderTests`:

```csharp
    [Test]
    public void A_cooldown_effect_that_grants_no_tag_is_a_build_error()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalDuration("cooldown.silent", ticks: 5));
        var ability = new AbilityDef { Name = "silent", MaxLevel = 1, CooldownEffect = "cooldown.silent" };
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("silent").And.Message.Contains("GrantedTags"));
    }
```

기존 테스트 `Cooldown_effect_blocks_the_next_activation_in_the_same_tick`은 `BlockedTags`에도 태그를 적으므로 기대값을 `OnCooldown`으로 바꾼다(게이트 4번이 6번보다 먼저). 이 커밋의 의도된 의미 변경이다.

`AbilityDefJsonTests`(약 158행)는 `"Cooldown.Fireball"`을 태그 없는 `MinimalInstant`로 등록하므로 새 Build 검증에 걸린다. 쿨다운답게 바꾼다:

```csharp
        var cooldown = EffectTestKit.MinimalDuration("Cooldown.Fireball", ticks: 10);
        cooldown.GrantedTags.Add("cooldown.fireball");
        kit.AddEffect(cooldown);
```

다른 쿨다운 사용처(`AbilityActivationTests`의 `cooldown.fireball`·`cooldown.scaled`, `GameplayCatalogBinaryTests`, `EffectScenario`)는 이미 태그를 부여한다.

- [ ] **Step 2: 실패 확인** — 컴파일 오류(`OnCooldown`, `TryGetCooldown` 없음).

- [ ] **Step 3: 구현**
  - enum: `OnCooldown = 8` 추가(문서: "The unit owns a tag granted by the ability's cooldown effect — including one granted by another effect sharing that tag."). `BlockedByTag` 문서는 "The unit owns a tag the ability blocks on."으로.
  - `EvaluateActivation`의 NotGranted 검사 직후, `ability` 해석 뒤에:

```csharp
            if (IsOnCooldown(unit, ability)) return AbilityActivationResult.OnCooldown;
```

```csharp
        private bool IsOnCooldown(Unit unit, CompiledAbility ability)
        {
            if (ability.CooldownEffectId < 0) return false;
            var tags = _effects.GetEffect(ability.CooldownEffectId).GrantedTags;
            for (var i = 0; i < tags.Length; i++)
            {
                if (unit.Tags.Has(tags[i])) return true;
            }

            return false;
        }

        /// <summary>Reports the longest remaining cooldown among active instances granting any tag of the
        /// ability's cooldown effect — the same tags the activation gate checks.</summary>
        /// <param name="unitId">Unit to query.</param>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <param name="remainingTicks">Remaining ticks of that instance.</param>
        /// <param name="durationTicks">Total ticks of that instance's current lifetime.</param>
        /// <returns>False when the unit or ability is unknown, the ability has no cooldown effect, or no
        /// such instance is active.</returns>
        public bool TryGetCooldown(TargetId unitId, int abilityId, out int remainingTicks, out int durationTicks)
        {
            remainingTicks = 0;
            durationTicks = 0;
            if (!TryGetUnit(unitId, out var unit) || unit is null || !_abilities.Contains(abilityId)) return false;

            var cooldownEffectId = _abilities.GetAbility(abilityId).CooldownEffectId;
            if (cooldownEffectId < 0) return false;

            var cooldownTags = _effects.GetEffect(cooldownEffectId).GrantedTags;
            var tagCatalog = unit.Tags.Catalog;
            var active = unit.Effects.ActiveEffects;
            var found = false;
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (!instance.Enabled || instance.RemainingTicks <= remainingTicks) continue;
                if (!GrantsAny(_effects.GetEffect(instance.EffectId).GrantedTags, cooldownTags, tagCatalog)) continue;
                remainingTicks = instance.RemainingTicks;
                durationTicks = instance.DurationTicks;
                found = true;
            }

            return found;
        }

        private static bool GrantsAny(GameplayTag[] granted, GameplayTag[] cooldownTags, TagCatalog tagCatalog)
        {
            for (var c = 0; c < cooldownTags.Length; c++)
            {
                for (var g = 0; g < granted.Length; g++)
                {
                    if (tagCatalog.IsAncestorOrSelf(cooldownTags[c], granted[g])) return true;
                }
            }

            return false;
        }
```

  (`unit.Tags`는 `TagCountContainer`이고 `.Catalog`를 가진다. `EffectPipeline.Events.cs`의 `listener.Tags.Catalog`와 같은 경로다. `using Bun3.Gameplay.Tags;` 추가.)
  - `AbilityCatalogBuilder`: `CooldownEffect` 해석 직후, 해석된 이펙트의 `GrantedTags.Length == 0`이면 `throw new InvalidOperationException($"Ability '{def.Name}': cooldown effect '{def.CooldownEffect}' grants no tag, so it can never gate activation. Add at least one GrantedTags entry to the cooldown effect.");` (기존 예외 메시지 형식을 따른다.)

- [ ] **Step 4: 통과 확인** — 빌드 경고 0, 전체 통과. `EffectScenario`의 어빌리티가 쿨다운 이펙트를 쓰면 게이트 결과가 바뀌어 골든 해시가 바뀔 수 있다 — 바뀌면 원인이 "쿨다운 판정이 BlockedTags 경유에서 OnCooldown 경유로 바뀜"뿐인지 확인한다(판정 결과 자체는 같아야 하므로 상태 해시는 같아야 한다). 다르면 멈추고 조사한다.

- [ ] **Step 5: 커밋** — `✨ GAS식 쿨다운 게이트(OnCooldown)와 태그 기준 TryGetCooldown`

---

### Task 4: 제거·Base 쓰기 명령

**Files:**
- Modify: `Runtime/World.Commands.cs` (`RemoveByTag`, `RemoveById`, `SetBase` 종류)
- Modify: `Runtime/Effects/EffectPipeline.cs` (`internal int RemoveByTagSpan(TargetId, ReadOnlySpan<GameplayTag>)`로 `RemoveByTags` 본체 추출)
- Modify tests: `WorldTests.cs`, `AbilityGrantSourceTests.cs`, `EffectScenario.cs`

**Interfaces:**
- Consumes: Task 1 `Command`, `CommandKind`, `ProcessCommands`.
- Produces: `public void World.EnqueueRemoveByTag(TargetId unitId, GameplayTag tag)`, `public void World.EnqueueRemoveById(TargetId unitId, ulong instanceId)`, `public void World.EnqueueSetBase(TargetId unitId, ushort attributeId, BigNum value)`.

- [ ] **Step 1: 실패 테스트** (`WorldTests`)

```csharp
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
```

(같은 `EffectId`는 병합되므로 인스턴스 두 개를 만들려고 이펙트 둘을 쓴다. `effect.frost`는 `EffectVocabularyTests`가 쓰는 카탈로그 태그다. 첫 틱의 `Spawn`과 `EnqueueApply`는 같은 틱에 처리되고 ⓪이 먼저라 유닛이 해석된다. 부모 태그 매칭은 기존 `RemoveByTags` 테스트가 덮는다.)

- [ ] **Step 2: 실패 확인** — 컴파일 오류.

- [ ] **Step 3: 구현**
  - `Command`에 필드 추가: `internal ulong InstanceId; internal GameplayTag Tag; internal ushort AttributeId; internal BigNum Value;` (`using Bun3.Gameplay.Numerics; using Bun3.Gameplay.Tags;`), `CommandKind`에 `RemoveByTag, RemoveById, SetBase`.
  - 적재 메서드(XML 문서 포함):

```csharp
        public void EnqueueRemoveByTag(TargetId unitId, GameplayTag tag) =>
            _commands.Add(new Command { Kind = CommandKind.RemoveByTag, Unit = unitId, Tag = tag });

        public void EnqueueRemoveById(TargetId unitId, ulong instanceId) =>
            _commands.Add(new Command { Kind = CommandKind.RemoveById, Unit = unitId, InstanceId = instanceId });

        public void EnqueueSetBase(TargetId unitId, ushort attributeId, BigNum value) =>
            _commands.Add(new Command { Kind = CommandKind.SetBase, Unit = unitId, AttributeId = attributeId, Value = value });
```

  - `ProcessCommands` 분기:

```csharp
                    case CommandKind.RemoveByTag:
                        Span<GameplayTag> query = stackalloc GameplayTag[1];
                        query[0] = command.Tag;
                        Pipeline.RemoveByTagSpan(command.Unit, query);
                        break;
                    case CommandKind.RemoveById:
                        Pipeline.RemoveById(command.Unit, command.InstanceId);
                        break;
                    case CommandKind.SetBase:
                        if (TryGetUnit(command.Unit, out var unit) && unit != null && unit.Attributes.Has(command.AttributeId))
                            unit.Attributes.SetBase(command.AttributeId, command.Value);
                        break;
```

  (`stackalloc`을 루프 안에 두지 않도록 `Span<GameplayTag> query = stackalloc GameplayTag[1];`는 `for` 위로 올린다.)
  - `EffectPipeline.RemoveByTags`의 본체를 `internal int RemoveByTagSpan(TargetId target, ReadOnlySpan<GameplayTag> queryTags)`로 옮기고, 공개 `RemoveByTags`는 `TagContainer`를 스팬으로 복사해 위임한다.
  - `World` 클래스 문서: 스폰 전 `AttributeSet.SetBase`는 허용, 등록 유닛은 `EnqueueSetBase`.
  - 이주: `EffectScenario.DriveOneWorldTick`의 `world.Pipeline.RemoveById` → `world.EnqueueRemoveById`, `AbilityGrantSourceTests`의 `world.Pipeline.RemoveById` → `world.EnqueueRemoveById` + `world.Tick()`(단언이 즉시 상태를 보던 곳은 틱 뒤로).

- [ ] **Step 4: 통과 확인** — 빌드 경고 0, 전체 통과, 골든 해시 불변(제거가 틱 사이에서 다음 틱 ⓪-2로 옮겨졌을 뿐 파이프라인 페이즈 기준 순서는 같다).

- [ ] **Step 5: 커밋** — `✨ World 제거·Base 쓰기 명령: 등록 유닛 상태 변경을 틱 안으로`

---

### Task 5: 전투 이벤트 버퍼 + 수명 이벤트 출처·레벨

**Files:**
- Modify: `Runtime/Effects/EffectPipeline.Events.cs` (`PendingGameplayEvents`, `ClearGameplayEvents`, 적재)
- Modify: `Runtime/Effects/EffectLifecycleEvent.cs` (`Source`, `Level`)
- Modify: `Runtime/Effects/EffectPipeline.cs` (수명 이벤트 생성 5곳에 `instance.Source, instance.Level`)
- Test: `EffectEventChainTests.cs`, `EffectVocabularyTests.cs`

**Interfaces:**
- Produces: `public ReadOnlySpan<GameplayEvent> EffectPipeline.PendingGameplayEvents`, `public void EffectPipeline.ClearGameplayEvents()`, `EffectLifecycleEvent(kind, instanceId, effectId, stack, source, level)` + `Source`·`Level` 속성.

- [ ] **Step 1: 실패 테스트** (`EffectEventChainTests`)

```csharp
    [Test]
    public void Raised_events_are_readable_from_the_pipeline_buffer()
    {
        var kit = BuildReflectKit(ChainListen.AsTarget);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();
        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.EffectId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        var events = pipeline.PendingGameplayEvents;
        Assert.That(events.Length, Is.EqualTo(1));
        Assert.That(events[0].Tag, Is.EqualTo(kit.Tag("event.damage.taken")));
        Assert.That(events[0].Source, Is.EqualTo(kit.Attacker.Id));
        Assert.That(events[0].Target, Is.EqualTo(kit.Defender.Id));
        Assert.That(events[0].Magnitude, Is.EqualTo((BigNum)(-40)));

        pipeline.ClearGameplayEvents();
        Assert.That(pipeline.PendingGameplayEvents.Length, Is.Zero);
    }
```

(`Magnitude`의 부호는 `GameplayEvent.Magnitude` 문서의 "Base after − Base before" 정의를 따른다.)

`EffectVocabularyTests` 첫 테스트(RemoveOnApply)의 이벤트 루프에서 `RemovedPrematurely` 이벤트의 `Source == kit.Attacker.Id`, `Level == 1`도 단언한다.

- [ ] **Step 2: 실패 확인** — 컴파일 오류.

- [ ] **Step 3: 구현**
  - `EffectLifecycleEvent` 생성자에 `TargetId source, int level` 추가(XML param 문서), `public TargetId Source { get; }`("Caster that applied the instance."), `public int Level { get; }`("Instance level at the time of the event.").
  - 파이프라인의 `new EffectLifecycleEvent(...)` 5곳에 `instance.Source, instance.Level`(신규 적용 지점은 방금 만든 `instance`)을 넘긴다.
  - `EffectPipeline.Events.cs`:

```csharp
        private GameplayEvent[] _gameplayEvents = new GameplayEvent[16];
        private int _gameplayEventCount;

        /// <summary>Gameplay events raised since the buffer was last cleared, in raise order.</summary>
        public ReadOnlySpan<GameplayEvent> PendingGameplayEvents => _gameplayEvents.AsSpan(0, _gameplayEventCount);

        /// <summary>Clears <see cref="PendingGameplayEvents"/>.</summary>
        public void ClearGameplayEvents() => _gameplayEventCount = 0;

        private void RaiseGameplayEvent(in GameplayEvent evt)
        {
            if (_gameplayEventCount == _gameplayEvents.Length) Array.Resize(ref _gameplayEvents, _gameplayEvents.Length * 2);
            _gameplayEvents[_gameplayEventCount++] = evt;
            DispatchEvent(evt.Target, ChainListen.AsTarget, in evt);
            DispatchEvent(evt.Source, ChainListen.AsSource, in evt);
        }
```

- [ ] **Step 4: 통과 확인** — 빌드 경고 0, 전체 통과. `AllocationSmokeTests`의 이벤트 경로 무할당 테스트가 통과하는지 특히 본다(버퍼가 데워진 뒤에는 할당이 없어야 한다 — 워밍업 틱이 이벤트를 한 번 이상 내는지 확인하고, 아니면 워밍업에 추가한다).

- [ ] **Step 5: 커밋** — `✨ 전투 이벤트 드레인 버퍼(PendingGameplayEvents)와 수명 이벤트 출처·레벨`

---

### Task 6: 틱 시작 시 관찰 버퍼 비움

**Files:**
- Modify: `Runtime/Effects/EffectPipeline.cs` (`internal void ClearObservationBuffers()`, `Tick()`이 먼저 호출)
- Modify: `Runtime/World.cs` (`Tick()` ⓪-1)
- Modify tests: `EffectScenario.cs`(`ClearWorldChangeBuffers`·파이프라인 시나리오의 수동 비움 제거), `AllocationSmokeTests.cs`, `EffectDeterminismTests.cs`의 수동 `Clear*` 호출(남겨도 무해하지만 새 계약을 보여주도록 제거)
- Test: `WorldTests.cs`, `EffectRemovalTests.cs`

**Interfaces:**
- Consumes: Task 1 `RunPhases`, Task 5 `_gameplayEventCount`.
- Produces: `internal void EffectPipeline.ClearObservationBuffers()`.

- [ ] **Step 1: 실패 테스트** (`WorldTests`)

```csharp
    [Test]
    public void Observation_buffers_hold_exactly_the_last_ticks_output()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInfinite("aura"));
        var abilityDef = new AbilityDef { Name = "cast", MaxLevel = 1 };
        abilityDef.Costs.Add(new CostDef { AttributeId = EffectTestKit.Mp, Magnitude = new MagnitudeDef { Base = Bun3.Gameplay.Attributes.Operand.Constant(5) } });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(abilityDef);
        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        var world = new World(kit.Catalog, abilities, new XorShiftRng(12345));
        var unit = MakeUnit(kit, 1);
        unit.Attributes.SetBase(EffectTestKit.Mp, 50);
        world.Spawn(unit);
        unit.Abilities.Grant(abilities.GetRequiredId("cast"), GrantSource.Archetype(), level: 1);
        world.Tick();
        Assert.That(unit.Attributes.PendingChanges.Length, Is.Zero, "pre-spawn initialization is not an event");

        world.EnqueueApply(kit.EffectId("aura"), unit.Id, unit.Id);
        world.EnqueueActivation(unit.Id, abilities.GetRequiredId("cast"));
        world.Tick();
        Assert.That(unit.Effects.PendingEffectEvents.Length, Is.EqualTo(1), "aura Applied");
        Assert.That(unit.Attributes.PendingChanges.Length, Is.EqualTo(1), "Mp cost paid inside the tick");
        Assert.That(world.PendingActivations.Length, Is.EqualTo(1));

        world.Tick();
        Assert.That(unit.Effects.PendingEffectEvents.Length, Is.Zero);
        Assert.That(unit.Attributes.PendingChanges.Length, Is.Zero);
        Assert.That(world.PendingActivations.Length, Is.Zero);
        Assert.That(world.Pipeline.PendingGameplayEvents.Length, Is.Zero);
    }
```

(코스트 차감은 `AddBase` → `ReapplyFormula` → 즉시 `EmitChange` 한 번이고, aura는 수정자가 없어 속성 이벤트를 내지 않으므로 1이다. 첫 단언은 스폰 전 `SetBase`가 만든 이벤트가 ⓪-1에서 비워지는지를 본다. 비움을 ⓪ 뒤에 두어야 통과한다.)

`EffectRemovalTests`(단독 파이프라인 계약):

```csharp
    [Test]
    public void A_standalone_pipeline_clears_its_buffers_when_the_next_tick_starts()
    {
        var kit = EffectTestKit.Create();
        kit.AddEffect(EffectTestKit.MinimalInfinite("aura"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.EffectId("aura"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.PendingEffectEvents.Length, Is.EqualTo(1));

        pipeline.Tick();
        Assert.That(kit.Defender.PendingEffectEvents.Length, Is.Zero);
    }
```

- [ ] **Step 2: 실패 확인** — 두 테스트가 "Expected: 0 But was: 1" 류로 실패.

- [ ] **Step 3: 구현**
  - `EffectPipeline`:

```csharp
        /// <summary>Processes one tick. Observation buffers (lifecycle events, attribute changes, gameplay
        /// events) are cleared first, so after the call they hold exactly this tick's output.</summary>
        public void Tick()
        {
            ClearObservationBuffers();
            RunPhases();
        }

        internal void ClearObservationBuffers()
        {
            _gameplayEventCount = 0;
            var targetIds = _resolver.TargetIds;
            for (var i = 0; i < targetIds.Count; i++)
            {
                if (!_resolver.TryResolve(targetIds[i], out var target) || target is null) continue;
                target.ClearEffectEvents();
                target.Attributes.ClearChanges();
            }
        }
```

  - `RunPhases()`의 "Phase 6" 주석을 "Observation buffers are cleared at the start of the next Tick; read them after this call."로 바꾼다.
  - `World.Tick()`:

```csharp
        public void Tick()
        {
            FlushStructuralChanges();
            Pipeline.ClearObservationBuffers();
            _activationCount = 0;
            ProcessCommands();
            Pipeline.RunPhases();
        }
```

  - `EffectTarget.PendingEffectEvents`, `AttributeSet.PendingChanges`, `ClearEffectEvents`, `ClearChanges` 문서에 "Cleared automatically when the next tick starts." 추가.
  - 이주: 수동 `Clear*` 호출을 지운다. 대상은 `EffectScenario.ClearWorldChangeBuffers`와 파이프라인 시나리오의 `ClearChanges`/`ClearEffectEvents`, `AllocationSmokeTests.ClearWorldBuffers`(메서드째 삭제), `EffectDeterminismTests` 270·281행 부근이다. `AttributeSetBasicTests`(파이프라인 없이 `ClearChanges`를 직접 검증)는 그대로 둔다.

- [ ] **Step 4: 통과 확인** — 빌드 경고 0, 전체 통과, 골든 해시 불변(버퍼는 상태 해시에 들어가지 않는다), `AllocationSmokeTests` 통과(비움은 카운터 대입뿐).

- [ ] **Step 5: 커밋** — `✨ 틱 시작 시 관찰 버퍼 자동 비움: 게임은 Tick() 뒤 한 번 읽는다`

---

### Task 7: 부여 어빌리티 열거 + 이름 조회

**Files:**
- Modify: `Runtime/Abilities/AbilitySet.cs` (`GetGrant`, `Version`, `AbilityGrantInfo` 형제 구조체)
- Modify: `Runtime/Abilities/AbilityCatalog.cs` (`GetName`)
- Test: `AbilitySetTests.cs`, `AbilityCatalogBuilderTests.cs`

**Interfaces:**
- Produces: `public readonly struct AbilityGrantInfo { int AbilityId; GrantSource Source; int Level; }`, `public AbilityGrantInfo AbilitySet.GetGrant(int index)`, `public int AbilitySet.Version`, `public string AbilityCatalog.GetName(int id)`.

- [ ] **Step 1: 실패 테스트** (`AbilitySetTests`)

```csharp
    [Test]
    public void Grants_are_enumerable_in_canonical_order_and_version_tracks_changes()
    {
        var set = new AbilitySet();
        var v0 = set.Version;

        set.Grant(2, GrantSource.Archetype(), level: 1);
        set.Grant(1, GrantSource.Archetype(), level: 3);
        Assert.That(set.Version, Is.EqualTo(v0 + 2));
        Assert.That(set.GetGrant(0).AbilityId, Is.EqualTo(1));
        Assert.That(set.GetGrant(0).Level, Is.EqualTo(3));
        Assert.That(set.GetGrant(1).AbilityId, Is.EqualTo(2));
        Assert.That(set.GetGrant(1).Source, Is.EqualTo(GrantSource.Archetype()));

        var beforeNoop = set.Version;
        set.Revoke(GrantSource.Effect(999));
        Assert.That(set.Version, Is.EqualTo(beforeNoop), "a revoke that removes nothing is not a change");

        set.RestoreSnapshot(set.CreateSnapshot());
        Assert.That(set.Version, Is.GreaterThan(beforeNoop));
    }
```

(`GrantSource.Effect(ulong)`의 인자 타입은 `GrantSource.cs`를 따른다.)

`AbilityCatalogBuilderTests`: `Assert.That(catalog.GetName(catalog.GetRequiredId("fireball")), Is.EqualTo("fireball"));`를 기존 빌드 성공 테스트 하나에 추가한다.

- [ ] **Step 2: 실패 확인** — 컴파일 오류.

- [ ] **Step 3: 구현**
  - `AbilitySet`: `public int Version { get; private set; }`("Incremented whenever the grant list changes — grant, level change, revoke that removed something, or restore. Redraw an ability bar only when it changes."). `Grant`(삽입·레벨 갱신 둘 다), `Revoke`(`removed > 0`일 때), `RestoreSnapshot`에서 `Version++`.
  - `public AbilityGrantInfo GetGrant(int index)` — `_grants[index]`를 값 복사.
  - 형제 구조체 `AbilityGrantInfo`(internal 생성자, XML 문서 포함 세 속성).
  - `AbilityCatalog`: `public string GetName(int id) => _abilities[id].Name;`(문서: "Authored name of an ability; the string is built once at catalog build, so this does not allocate.").

- [ ] **Step 4: 통과 확인** — 빌드 경고 0, 전체 통과.

- [ ] **Step 5: 커밋** — `✨ 부여 어빌리티 열거(GetGrant·Version)와 AbilityCatalog.GetName`

---

### Task 8: 무할당 스모크 확장 + 마무리

**Files:**
- Modify: `common/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs`
- Modify: `docs/superpowers/specs/2026-08-20-gameplay-slice3-ability-unit-world-design.md` §12(테스트 전략에 새 테스트 반영), §14(이연: 명령 틱 번호 스탬프 → 슬라이스 5)

- [ ] **Step 1: 테스트 추가** — 워밍업 뒤 명령 처리·관찰 읽기 경로 전체가 무할당인지 본다. `BuildWorldWithEventListeners`를 확장해 어빌리티 하나를 싣고, 반환 튜플에 어빌리티 id를 더한다(기존 두 테스트의 분해 구문은 `var (world, _, _, _, _)` 형태로 맞춘다).

픽스처 확장(이펙트 빌더에 추가하고, 비어 있던 어빌리티 빌더를 채운다):

```csharp
        effectBuilder.Add(new EffectDef
        {
            Name = "StrikeCooldown",
            DurationType = EffectDurationType.Duration,
            DurationTicks = 1,
            GrantedTags = { "cooldown.fireball" },
            AssetTags = { "cooldown.fireball" },
        });
        var effects = effectBuilder.Build(tags, seams, attributes);
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(new AbilityDef
        {
            Name = "Strike",
            MaxLevel = 1,
            CooldownEffect = "StrikeCooldown",
            Costs = { new CostDef { AttributeId = EffectTestKit.Mp, Magnitude = new MagnitudeDef { Base = Operand.Constant(1) } } },
            Effects = { new AbilityEffectDef { EffectName = "Damage" } },
        });
        var abilities = abilityBuilder.Build(tags, seams, attributes, effects);
```

유닛 생성 뒤 `attacker.Attributes.SetBase(EffectTestKit.Mp, 1_000);`, 스폰 뒤 `attacker.Abilities.Grant(abilities.GetRequiredId("Strike"), GrantSource.Archetype(), level: 1);`를 넣고, 반환에 `abilities.GetRequiredId("Strike")`를 더한다.

테스트:

```csharp
    [Test]
    public void Queued_commands_and_observation_reads_allocate_nothing_in_steady_state()
    {
        var (world, damageEffectId, attackerId, defenderId, strikeId) = BuildWorldWithEventListeners();
        world.TryGetUnit(attackerId, out var attacker);
        var cooldownTag = attacker!.Tags.Catalog.GetRequired("cooldown.fireball");

        // Warm-up grows every buffer these paths touch to its steady capacity before measuring.
        for (var i = 0; i < 10; i++) DriveCommandTick(world, damageEffectId, attackerId, defenderId, strikeId, attacker, cooldownTag);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) DriveCommandTick(world, damageEffectId, attackerId, defenderId, strikeId, attacker, cooldownTag);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero, "queued commands or observation reads allocated on the heap");
    }

    private static long DriveCommandTick(
        World world, int damageEffectId, TargetId attackerId, TargetId defenderId, int strikeId, Unit attacker,
        GameplayTag cooldownTag)
    {
        world.EnqueueActivation(attackerId, strikeId);
        world.EnqueueApply(damageEffectId, attackerId, defenderId);
        world.EnqueueSetBase(attackerId, EffectTestKit.Mp, 1_000);
        world.EnqueueRemoveByTag(attackerId, cooldownTag);
        world.Tick();

        long sink = world.PendingActivations.Length + world.Pipeline.PendingGameplayEvents.Length;
        for (var i = 0; i < attacker.Effects.ActiveEffectCount; i++) sink += attacker.Effects.GetActiveEffect(i).RemainingTicks;
        world.TryGetCooldown(attackerId, strikeId, out var remaining, out _);
        sink += remaining + (int)world.CanActivateAbility(attackerId, strikeId);
        return sink;
    }
```

(`using Bun3.Gameplay.Abilities; using Bun3.Gameplay.Tags;`가 파일에 없으면 추가한다.)

- [ ] **Step 2: 실행** — 통과하면 다음 단계. 할당이 잡히면 `dotnet-counters`나 할당 위치 이분(측정 구간을 반으로 줄여가며)으로 원인을 찾아 해당 Task 코드에서 고친다.

- [ ] **Step 3: 스펙 갱신** — §12에 명령 큐·관찰 API·버퍼 비움 테스트를 한 줄씩, §14에 "명령 틱 번호 스탬프(idlez식) → 슬라이스 5 복제" 행 추가.

- [ ] **Step 4: 전체 검증** — `dotnet build Bun3.sln` 경고 0, 전체 테스트 통과, 골든 해시 불변.

- [ ] **Step 5: 커밋** — `✅ 명령 큐·관찰 API 무할당 스모크 + 스펙 §12·§14 갱신`
