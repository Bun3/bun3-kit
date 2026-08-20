# Naming refactor report — Effect domain `<Domain>Def` / `Compiled<Domain>` convention

Branch `Bun3/gameplay-slice3-ability`, base HEAD `eb6df46`.

## Commit 1 — pure mechanical rename

### File renames (via `git mv`, `.cs.meta` sidecars moved with GUIDs unchanged)

| From | To |
|---|---|
| `common/src/com.bun3.gameplay/Runtime/Effects/EffectSpec.cs` (+ `.meta`) | `EffectDef.cs` |
| `common/src/com.bun3.gameplay/Catalog/EffectSpecJson.cs` (+ `.meta`) | `EffectDefJson.cs` |
| `common/tests/Bun3.Gameplay.Tests/EffectSpecJsonTests.cs` | `EffectDefJsonTests.cs` |

`git status --short` after the three `git mv` calls showed all three as clean `R` (rename, 100% for `.meta`, ~89%/96%/82% for the `.cs` files since content also changed in the same commit).

### Type renames

| From | To |
|---|---|
| `EffectSpec` | `EffectDef` |
| `StackPolicy` | `StackPolicyDef` |
| `LevelKey` | `LevelKeyDef` |
| `CompiledEffectSpec` | `CompiledEffect` |
| `CompiledChain` | `CompiledChainEdge` |
| `EffectSpecJson` | `EffectDefJson` |
| `EffectSpecJsonTests` (test class) | `EffectDefJsonTests` |

Applied via ordered, word-boundary `sed` passes (longest/most-specific identifier first, e.g. `CompiledEffectSpec` before `EffectSpec`) so no partial-substring corruption occurred. Verified with `dotnet build` (0 warnings/errors) after each pass.

### Every `Spec`-bearing member, parameter, and local — enumerated and decided

**Public/internal members (mandatory rename — no `Spec` survives):**

| Symbol | Decision |
|---|---|
| `EffectCatalog.GetSpec(int)` | → `GetEffect(int)` (mirrors `AbilityCatalog.GetAbility`) |
| `EffectInstance.SpecId` | → `EffectId` |
| `EffectLifecycleEvent.SpecId` | → `EffectId` |
| `EffectPipeline.PendingApply.SpecId` (internal field) | → `EffectId` |
| `EffectTargetSnapshot`'s nested row `SpecId` | → `EffectId` (not in the original "at minimum" list but is `internal`, so mandatory) |
| `EffectTestKit.AddSpec(EffectDef)` | → `AddEffect(EffectDef)` |
| `EffectTestKit.SpecId(string)` | → `EffectId(string)` |
| `EffectCatalogBuilder.CompileSpec(...)` (private) | → `Compile(...)` (mirrors `AbilityCatalogBuilder.Compile`; private, not strictly mandatory, but renamed for domain consistency) |
| `EffectPipeline.FindActiveBySpec` (private) | → `FindActiveByEffectId` (private; renamed — searches by an effect id, name now matches its parameter) |
| `EffectDefJson.ReadSpec` (private) | → `ReadEffect` (mirrors `AbilityDefJson.ReadAbility`) |
| `EffectScenario.AddSpecs` / `AddScenarioSpecs` (test-support, effectively internal via same assembly) | → `AddEffects` / `AddScenarioEffects` |
| `EffectDurationScaleTests.DrCcSpec` (private test helper) | → `DrCcDef` |
| `GameplayCatalogBinaryTests.GetSpec` (private test helper) + its reflection string literal `"GetSpec"` | → `GetEffect` / `"GetEffect"` (kept in sync with the real renamed method it invokes via reflection) |

**Locals/parameters — decided per file, renamed for consistency where the containing method already existed (zero behavior change, pure identifier rename):**

- Everywhere a local/parameter held an authoring `EffectDef` (in `EffectCatalogBuilder.cs`, and across nearly every `common/tests/Bun3.Gameplay.Tests/*.cs` file that builds an effect for a test), it was renamed `spec`/`specs` → `def`/`defs`, mirroring `AbilityCatalogBuilder`'s existing `def`/`_defs`/`allDefs`/`ownerDef` convention. Field `_specs` → `_defs`, `allSpecs` → `allDefs`, `ownerSpec` → `ownerDef`, `specName` (owner-name string param) → `ownerName`.
- Everywhere a local/parameter held a compiled `CompiledEffect` (in `EffectPipeline.cs`, `EffectPipeline.Events.cs`, `AbilityCatalogBuilder.cs`, `GameplayCatalogBinary.cs`, `EffectCatalogBuilderTests.cs`), renamed `spec` → `effect` (e.g. `boltSpec`/`hastedSpec` → `boltEffect`/`hastedEffect`; `incomingSpec` → `incomingEffect`).
- `EffectCatalog`'s backing field/constructor param `_specs`/`specs` (of `CompiledEffect[]`) → `_effects`/`effects`.
- `GameplayCatalogBinary.cs`'s local `specs` (`CompiledEffect[]`) → `effects` (now mirrors the sibling `abilities` array exactly).
- Test-only locals of type `EffectDef`: `codeSpec` → `codeDef` (`EffectDefJsonTests.cs`).
- Test method names carrying dead `spec` vocabulary were also renamed (e.g. `Cycle_through_duration_spec_is_a_low_warning` → `..._effect_is_a_low_warning`, `RemoveOnApplyTags_excludes_same_spec_because_merge_wins` → `..._same_effect_...`, several `Event_magnitude_..._spec_...` test names in `EffectEventChainTests.cs`, and the four `..._spec_...` names in `EffectDefJsonTests.cs`). Full list is in the diff of those files.
- Comment/XML-doc prose mentioning "spec"/"specs" as dead vocabulary was reworded to "def"/"effect"/"definition" throughout both `common/src/com.bun3.gameplay/` and `common/tests/`.

**Deliberately left untouched (not this domain's `Spec` vocabulary):**

- `Environment.SpecialFolder` (BCL API) — 2 occurrences.
- `ConcurrentObjectPoolTests.Defaults_MatchSpec` and `OperandTests.Default_policies_match_the_spec` — generic English "spec" = specification, unrelated types (`Bun3.Common.Tests`, and an `Operand` test about matching a design spec, not an `EffectSpec`/`AbilityDef` type).
- `EffectChainConditionTests.cs` comment `"the spec's acceptance bar"` — same generic-English sense, about the design spec, not the type.
- `BigNumOverflowException`'s "suspect", `Int128Math`'s "specialization", various "inspect"/"inspecting"/"specific" words — case-insensitive substring false positives, not touched.
- **The JSON authoring key `"specs"`** (`EffectDefJson.cs`'s root array key, `RequireAllowedProperties(root, "schemaVersion", "specs")` / `RequireArray(root, "specs")`, the `"specs items must be objects."` error message, and every inline JSON test fixture in `EffectDefJsonTests.cs` that writes `"specs": [ ... ]`) — **left exactly as `"specs"`**, per the constraint that JSON authoring keys are wire format and must not change even though the compiled type name changed. Verified via `grep` that only these JSON-literal lines still contain the substring after the rest of the file was renamed.

### Commit 1 gate — test counts

Before (HEAD `eb6df46`, i.e. what the rename started from) and after commit 1, `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`:

```
통과!  - 실패: 0, 통과: 581, 건너뜀: 1, 전체: 582, 기간: 11 s - Bun3.Gameplay.Tests.dll (net10.0)
```

581 passed / 1 skipped / 582 total — unchanged, exactly as required. `dotnet build Bun3.sln` → `경고 0개, 오류 0개` (0 warnings, 0 errors) both before staging and after the rename.

Commit: `fdf5ff8` — "🏷️ Effect 도메인 명명 컨벤션 통일: EffectSpec -> EffectDef, StackPolicy -> StackPolicyDef, CompiledChain -> CompiledChainEdge"

---

## Commit 2 — close the mutable-authoring-object leak

### The bug

`CompiledEffect.Stack` was typed `StackPolicyDef` — the exact same mutable, eleven-`{ get; set; }`-property authoring class referenced from `EffectDef.Stack`. `EffectCatalogBuilder.Compile` passed `def.Stack` straight through into `new CompiledEffect(...)` without copying it, so a caller holding onto the authoring `EffectDef` after `Build()` could mutate the "immutable" compiled catalog by mutating `def.Stack`.

### The fix

Added `internal sealed class CompiledStackPolicy` in `EffectCatalog.cs`, get-only, one property per `StackPolicyDef` field, values copied by `EffectCatalogBuilder.Compile` at build time:

`MaxStack, OnReapply, AddStackCount, RefreshDurationOnReapply, ResetPeriodOnReapply, OnExpiration, OnOverflow, OverflowEffectName, ClearStacksOnOverflow, LevelFromStack, ExtendCapMultiplier` — all 11 fields, matching exactly what `GameplayCatalogBinary.WriteEffect`/`ReadEffect` read/write for the stack-policy section (checked the binary serializer field-by-field before writing the constructor — nothing was missed).

`CompiledEffect.Stack` is now typed `CompiledStackPolicy`. `EffectCatalogBuilder.Compile` builds a fresh `CompiledStackPolicy` from `def.Stack`'s values instead of forwarding the reference. `GameplayCatalogBinary.ReadEffect` constructs a `CompiledStackPolicy` via its constructor instead of a `StackPolicyDef` object initializer (argument evaluation order for the sequential `reader.Read...()` calls is guaranteed left-to-right by the C# spec, same read order as before — no behavior change). `WriteEffect` needed no change (still reads the same 11 property names off `effect.Stack`, now off the new type).

> Note on the task's premise: the task said "only five... members are read at runtime (`MaxStack`, `OnExpiration`, `OnOverflow`, `ClearStacksOnOverflow`, `LevelFromStack`)". That undercounts — `EffectPipeline.MergeReapply` also reads `OnReapply`, `AddStackCount`, `RefreshDurationOnReapply`, `ResetPeriodOnReapply`, and `ExtendCapMultiplier`. In practice every field except `OverflowEffectName` (which is resolved separately into `CompiledEffect.OverflowEffectId` at build time) is read somewhere in `EffectPipeline.cs`. This doesn't change the fix — "carry every field" was already the instruction — but the five-member premise doesn't match the actual pipeline code.

### New test proving the leak is closed

`common/tests/Bun3.Gameplay.Tests/EffectCatalogBuilderTests.cs`:

```csharp
[Test]
public void Build_copies_the_authoring_stack_policy_so_later_mutation_does_not_leak_into_the_compiled_catalog()
{
    var kit = EffectTestKit.Create();
    var stackPolicy = new StackPolicyDef { MaxStack = 3, OnReapply = StackReapply.AddStack, LevelFromStack = true };
    var def = EffectTestKit.MinimalDuration("leaky", 10);
    def.Stack = stackPolicy;
    kit.AddEffect(def);

    var compiled = kit.Catalog.GetEffect(kit.EffectId("leaky"));

    stackPolicy.MaxStack = 99;
    stackPolicy.OnReapply = StackReapply.ExtendCapped;
    stackPolicy.LevelFromStack = false;

    Assert.That(compiled.Stack.MaxStack, Is.EqualTo(3));
    Assert.That(compiled.Stack.OnReapply, Is.EqualTo(StackReapply.AddStack));
    Assert.That(compiled.Stack.LevelFromStack, Is.True);
}
```

### Verified the test fails against commit 1's code

Temporarily restored the three commit-1 files (`git checkout HEAD -- EffectCatalog.cs EffectCatalogBuilder.cs GameplayCatalogBinary.cs`, i.e. commit `fdf5ff8`'s version, which still has `CompiledEffect.Stack : StackPolicyDef` with no copy) while keeping the new test, and ran:

```
dotnet test ... --filter "FullyQualifiedName~Build_copies_the_authoring_stack_policy"

실패 Build_copies_the_authoring_stack_policy_so_later_mutation_does_not_leak_into_the_compiled_catalog [321 ms]
오류 메시지:
   Assert.That(compiled.Stack.MaxStack, Is.EqualTo(3))
Expected: 3
But was:  99

실패! - 실패: 1, 통과: 0, 건너뜀: 0, 전체: 1
```

Confirmed the test fails exactly as expected (`compiled.Stack` was literally the same `StackPolicyDef` reference as the authoring `stackPolicy`, so mutating the latter mutated `MaxStack` from 3 to 99 as observed through the "compiled" object). Then restored the three commit-2 fix files and re-verified: `dotnet build` → 0 warnings/0 errors; the same test now passes.

### Mutable-authoring-object leak audit — all `Compiled*` types

Enumerated every `class Compiled*` / `struct Compiled*` in `common/src/com.bun3.gameplay/` and checked every reference-typed member:

| Type | Reference-typed members | Leak? |
|---|---|---|
| `CompiledEffect` | `CompiledStackPolicy Stack` (fixed, was the leak), `CompiledModifier[]`, `CompiledExecution[]`, `GameplayTag[]` ×4, `string[] GrantedAbilityNames`, `CompiledChainEdge[]`, `CompiledMagnitude?` ×2, `BigNum[]?`, `BigNum[]` | No further leak — every array is freshly built (`.ToArray()` / `new T[n]` + copy loop) from the authoring `List<T>`, never the authoring list itself. `GrantedAbilityNames` shares `string` references with the authoring `List<string>`, but `string` is immutable so that's not a leak. |
| `CompiledModifier` | `Operand?` ×2 (struct), `IMagnitudeCalc?` (seam contract, not authoring), `GameplayTag` (struct), `BigNum[]?` (fresh array from `MagnitudeCompiler.Resolve`) | No |
| `CompiledMagnitude` | Same shape as above minus attribute id | No |
| `CompiledExecution` | `IExecutionCalc` (seam contract), `GameplayTag`, `Operand[]` (fresh array, `Operand` is a value struct) | No |
| `CompiledCondition` (struct) | `Operand` ×2 (struct) | No |
| `CompiledRaiseEvent` (struct) | `GameplayTag` (struct) | No |
| `CompiledChainEdge` | `ITargetSelector?` (seam contract), `GameplayTag` ×2, `BigNum[]` (fresh `.ToArray()`), `CompiledCondition[]` (freshly resolved) | No |
| `CompiledStackPolicy` | `string? OverflowEffectName` (immutable `string`) | No (this is the new type) |
| `CompiledAbility` | `GameplayTag[]` ×3, `CompiledCondition[]`, `CompiledCost[]`, `CompiledAbilityEffect[]`, `string Name` | No — same pattern, all freshly built arrays/structs |
| `CompiledAbilityEffect` | `ITargetSelector?` (seam contract), `GameplayTag`, `BigNum[]` (fresh `.ToArray()`) | No |
| `CompiledCost` (struct) | `CompiledMagnitude` | No |
| `CompiledFormula` (Numerics) | `FormulaToken[]` (struct array, parsed from an immutable `string`) | No — not even an authoring-def-shaped type, out of scope |
| `TagCatalogCompiledRedirect` | Tag domain, explicitly out of scope (`TagContainer` family, "do not touch" per task) | Not audited further |

**Conclusion: `CompiledEffect.Stack` was the only leak.** No other `Compiled*` type holds a reference to a mutable authoring object; everything else already copies into fresh arrays/structs or holds resolved seam contracts / immutable strings.

### Commit 2 gate — test counts

```
통과! - 실패: 0, 통과: 582, 건너뜀: 1, 전체: 583, 기간: 13 s - Bun3.Gameplay.Tests.dll (net10.0)
```

582 passed (581 + the 1 new test) / 1 skipped / 583 total. `dotnet build Bun3.sln` → 0 warnings / 0 errors.

Commit: (recorded after this report is written — see final response for the SHA)
