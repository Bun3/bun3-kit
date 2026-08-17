# 게임플레이 슬라이스 2 — Attribute · Effect 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Attribute 집계와 Effect 수명·스택·체인 기계를 결정론·무할당 규율로 구현한다 — "이속 +20% 8초"와 "빙결 3중첩 → 동결"이 코드 0줄.

**Architecture:** Effect는 순수 데이터(EffectSpec), 로직은 시섬 3종(IMagnitudeCalc/IExecutionCalc/ITargetSelector, GameplayTag 식별)만. 기동 Build 체인(TagCatalog → SeamRegistry → AttributeRegistry → EffectCatalog)이 모든 문자열을 id/직결 참조로 컴파일하고, 런타임 틱은 6페이즈 파이프라인이 canonical 순서로 돈다.

**Tech Stack:** netstandard2.1 + C#9, BigNum(기존), GameplayTag/TagCatalog(기존), NUnit(net10 테스트), Newtonsoft.Json(저작 어셈블리만).

**Spec:** `docs/superpowers/specs/2026-08-17-gameplay-slice2-attributes-effects-design.md`

## Global Constraints

- 대상 프로젝트: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj` (netstandard2.1, C#9 블록 네임스페이스, `#nullable enable`). 로더만 `Catalog/`(저작 어셈블리).
- 모든 public 멤버에 한국어 XML 문서. 빌드 경고 0. 플랜의 코드 스니펫에 문서가 생략된 public 멤버가 있으면 구현 시 반드시 채운다.
- 무할당 규율: 틱 정착 상태에서 힙 할당 0. 클로저·LINQ 금지. 컨텍스트는 `ref struct`.
- 결정론: 모든 순회는 canonical 순서(§ 각 태스크 명시). float/double 금지 — BigNum·정수만.
- 수정자 크기의 Operand는 **적용 시점 평가(스냅샷)로 고정** — 적용 후 원본 속성이 변해도 이미 적용된 수정자 크기는 불변 (스펙 §4, live 갱신·Spec 생성 시점 스냅샷은 후속).
- Operand는 세 형태다 — `Constant` / `Attribute`(대상 속성) / `SourceAttribute`(시전자 속성). 별도 origin 축 없음 — kind가 곧 판별자. `SourceAttribute`는 Modifier 크기·Execution inputs·ApplicationConditions에서만 허용, OngoingConditions·클램프 경계에서는 Build 오류. 소스 미해석 시 `SourceAttribute`는 BigNum.Zero로 평가.
- 테스트: `common/tests/Bun3.Gameplay.Tests/` (net10.0, NUnit). 실행: `dotnet test common/tests/Bun3.Gameplay.Tests -c Release -v:minimal`.
- 커밋: gitmoji + `git commit -m "<제목>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"` 이중 플래그 (here-string 금지).
- 신규 `.cs`의 Unity `.meta`는 이번 플랜에서 만들지 않는다 — 에디터를 여는 후속 커밋에서 일괄 (기존 관례).
- 기존 테스트(현재 306개)는 전 태스크에서 계속 통과해야 한다.

---

### Task 1: Operand와 기반 enum

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Attributes/Operand.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Attributes/AttributeEnums.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/OperandTests.cs`

**Interfaces:**
- Produces: `Operand`(readonly struct — `Kind: OperandKind`, `Value: BigNum`, `AttributeId: ushort`; 팩토리 `Operand.Constant(BigNum)`, `Operand.Attribute(ushort)`, `Operand.Attribute(ushort, BigNum coefficient)`, `Operand.SourceAttribute(ushort)`, `Operand.SourceAttribute(ushort, BigNum coefficient)`; `IEquatable<Operand>`), `OperandKind { Constant=0, Attribute=1, SourceAttribute=2 }`, `AttributeModifierOp { Add=0, Multiply=1, Override=2 }`, `MaxIncreasePolicy { Stay=0, Follow=1 }`, `MaxDecreasePolicy { Follow=0, Stay=1 }`, `ComparisonOp { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }`. 테스트에 `SourceAttribute` kind·`Attribute`와의 비동등 케이스 추가.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class OperandTests
{
    [Test]
    public void Constant_and_attribute_factories_populate_kind_and_fields()
    {
        var constant = Operand.Constant(50);
        Assert.That(constant.Kind, Is.EqualTo(OperandKind.Constant));
        Assert.That(constant.Value, Is.EqualTo((BigNum)50));

        var plain = Operand.Attribute(3);
        Assert.That(plain.Kind, Is.EqualTo(OperandKind.Attribute));
        Assert.That(plain.AttributeId, Is.EqualTo((ushort)3));
        Assert.That(plain.Value, Is.EqualTo(BigNum.One));   // 계수 기본 1

        var scaled = Operand.Attribute(3, BigNum.FromParts(3, -1));   // ×0.3
        Assert.That(scaled.Value, Is.EqualTo(BigNum.FromParts(3, -1)));
    }

    [Test]
    public void Operands_compare_by_value()
    {
        Assert.That(Operand.Constant(50), Is.EqualTo(Operand.Constant(50)));
        Assert.That(Operand.Constant(50), Is.Not.EqualTo(Operand.Attribute(3)));
        Assert.That(Operand.Attribute(3, 2), Is.Not.EqualTo(Operand.Attribute(3, 5)));
    }

    [Test]
    public void Default_policies_match_the_spec()
    {
        Assert.That(default(MaxIncreasePolicy), Is.EqualTo(MaxIncreasePolicy.Stay));
        Assert.That(default(MaxDecreasePolicy), Is.EqualTo(MaxDecreasePolicy.Follow));
    }
}
```

- [ ] **Step 2: 실패 확인** — `dotnet test common/tests/Bun3.Gameplay.Tests -c Release --filter OperandTests -v:minimal` → 컴파일 오류(타입 없음) 확인.

- [ ] **Step 3: 구현**

`AttributeEnums.cs`:

```csharp
#nullable enable
namespace Bun3.Gameplay.Attributes
{
    /// <summary>피연산자 형태 판별자입니다. 미래의 Expression 노드가 추가될 수 있습니다.</summary>
    public enum OperandKind : byte
    {
        /// <summary>상수 BigNum입니다.</summary>
        Constant = 0,
        /// <summary>대상(target) 속성 Current × 상수 계수입니다.</summary>
        Attribute = 1,
        /// <summary>시전자(source) 속성 Current × 상수 계수입니다. 소스 미해석 시 0으로 평가됩니다.</summary>
        SourceAttribute = 2,
    }

    /// <summary>수정자 연산 종류입니다.</summary>
    public enum AttributeModifierOp : byte
    {
        /// <summary>가산 — ΣAdd에 합산됩니다.</summary>
        Add = 0,
        /// <summary>합산식 곱 — 퍼센트가 ΣMulPct에 합산된 뒤 한 번 곱해집니다.</summary>
        Multiply = 1,
        /// <summary>최우선 덮어쓰기 — 복수면 가장 나중 인스턴스가 이깁니다.</summary>
        Override = 2,
    }

    /// <summary>max 경계가 상승할 때 Base의 동반 여부입니다.</summary>
    public enum MaxIncreasePolicy : byte
    {
        /// <summary>Base 유지 — 빈 여유만 커집니다(기본).</summary>
        Stay = 0,
        /// <summary>Base += Δ — 잃은 량이 보존됩니다.</summary>
        Follow = 1,
    }

    /// <summary>max 경계가 하락할 때 Base의 처리입니다.</summary>
    public enum MaxDecreasePolicy : byte
    {
        /// <summary>Base를 경계로 잘라 기록 — 초과분 영구 소실(기본).</summary>
        Follow = 0,
        /// <summary>Base 보존 — Current만 안전망에 눌리고 경계 복귀 시 복원됩니다.</summary>
        Stay = 1,
    }

    /// <summary>조건 비교 연산자입니다.</summary>
    public enum ComparisonOp : byte
    {
        /// <summary>같음.</summary>
        Equal = 0,
        /// <summary>다름.</summary>
        NotEqual = 1,
        /// <summary>미만.</summary>
        Less = 2,
        /// <summary>이하.</summary>
        LessOrEqual = 3,
        /// <summary>초과.</summary>
        Greater = 4,
        /// <summary>이상.</summary>
        GreaterOrEqual = 5,
    }
}
```

`Operand.cs`:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>
    /// 수정자 크기·조건 양변·클램프 경계가 공유하는 피연산자 — 상수 또는 속성 Current × 계수입니다.
    /// </summary>
    public readonly struct Operand : IEquatable<Operand>
    {
        /// <summary>형태 판별자입니다.</summary>
        public OperandKind Kind { get; }

        /// <summary>상수 값 또는 속성 참조의 계수입니다.</summary>
        public BigNum Value { get; }

        /// <summary>참조하는 속성 id이며 상수면 0입니다.</summary>
        public ushort AttributeId { get; }

        private Operand(OperandKind kind, BigNum value, ushort attributeId)
        {
            Kind = kind;
            Value = value;
            AttributeId = attributeId;
        }

        /// <summary>상수 피연산자를 만듭니다.</summary>
        public static Operand Constant(BigNum value) => new Operand(OperandKind.Constant, value, 0);

        /// <summary>계수 1의 대상 속성 참조 피연산자를 만듭니다.</summary>
        public static Operand Attribute(ushort attributeId) => Attribute(attributeId, BigNum.One);

        /// <summary>대상 속성 Current × 계수 피연산자를 만듭니다.</summary>
        public static Operand Attribute(ushort attributeId, BigNum coefficient) =>
            new Operand(OperandKind.Attribute, coefficient, attributeId);

        /// <summary>계수 1의 시전자 속성 참조 피연산자를 만듭니다.</summary>
        public static Operand SourceAttribute(ushort attributeId) => SourceAttribute(attributeId, BigNum.One);

        /// <summary>시전자 속성 Current × 계수 피연산자를 만듭니다.</summary>
        public static Operand SourceAttribute(ushort attributeId, BigNum coefficient) =>
            new Operand(OperandKind.SourceAttribute, coefficient, attributeId);

        /// <summary>값 동등 비교입니다.</summary>
        public bool Equals(Operand other) =>
            Kind == other.Kind && Value.Equals(other.Value) && AttributeId == other.AttributeId;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Operand other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                hash = (hash * 397) ^ Value.GetHashCode();
                hash = (hash * 397) ^ AttributeId;
                return hash;
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — 같은 필터로 실행, PASS.
- [ ] **Step 5: 커밋** — `git add` 두 소스+테스트 → `✨ Operand·기반 enum 추가`.

---

### Task 2: AttributeRegistry — 수집·Build 검증·위상 순서

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Attributes/AttributeRegistryBuilder.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Attributes/AttributeRegistry.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/AttributeRegistryTests.cs`

**Interfaces:**
- Consumes: Task 1 전부.
- Produces: `AttributeRegistryBuilder` — `void Register(ushort attributeId, Operand? min = null, Operand? max = null, MaxIncreasePolicy onMaxIncrease = MaxIncreasePolicy.Stay, MaxDecreasePolicy onMaxDecrease = MaxDecreasePolicy.Follow)`, `AttributeRegistry Build()`. `AttributeRegistry` — `int Count`, `bool Contains(ushort)`, internal `AttributeDefinition GetDefinition(ushort)`(struct: Min/Max/두 정책), internal `ReadOnlySpan<ushort> EvaluationOrder`(위상, 동순위 id 오름차순), internal `ReadOnlySpan<ushort> GetClampDependents(ushort)`(이 속성을 클램프로 참조하는 후손, id 순).

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AttributeRegistryTests
{
    private const ushort Hp = 1;
    private const ushort MaxHp = 2;
    private const ushort Mp = 3;
    private const ushort MaxMp = 4;

    [Test]
    public void Registration_order_does_not_matter_and_forward_references_are_allowed()
    {
        var forward = new AttributeRegistryBuilder();
        forward.Register(Hp, min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        forward.Register(MaxHp, min: Operand.Constant(1));

        var backward = new AttributeRegistryBuilder();
        backward.Register(MaxHp, min: Operand.Constant(1));
        backward.Register(Hp, min: Operand.Constant(0), max: Operand.Attribute(MaxHp));

        var a = forward.Build();
        var b = backward.Build();
        Assert.That(a.EvaluationOrder.ToArray(), Is.EqualTo(b.EvaluationOrder.ToArray()));
        Assert.That(a.GetClampDependents(MaxHp).ToArray(), Is.EqualTo(new[] { Hp }));
    }

    [Test]
    public void Evaluation_order_puts_referenced_attributes_first_with_id_tiebreak()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(Hp, max: Operand.Attribute(MaxHp));
        builder.Register(Mp, max: Operand.Attribute(MaxMp));
        builder.Register(MaxMp);
        builder.Register(MaxHp);
        var registry = builder.Build();

        var order = registry.EvaluationOrder.ToArray();
        Assert.That(Array.IndexOf(order, MaxHp), Is.LessThan(Array.IndexOf(order, Hp)));
        Assert.That(Array.IndexOf(order, MaxMp), Is.LessThan(Array.IndexOf(order, Mp)));
        // 독립 원소끼리는 id 오름차순
        Assert.That(order, Is.EqualTo(new ushort[] { MaxHp, MaxMp, Hp, Mp }).Or.EqualTo(new ushort[] { MaxMp, MaxHp, Hp, Mp }));
        Assert.That(order[0], Is.EqualTo(MaxHp));   // 동순위 타이브레이크 = id 오름차순이므로 2 < 4
    }

    [Test]
    public void Build_rejects_missing_reference_cycle_and_meaningless_policy()
    {
        var missing = new AttributeRegistryBuilder();
        missing.Register(Hp, max: Operand.Attribute(MaxHp));   // MaxHp 미등록
        Assert.Throws<InvalidOperationException>(() => missing.Build());

        var cyclic = new AttributeRegistryBuilder();
        cyclic.Register(Hp, max: Operand.Attribute(MaxHp));
        cyclic.Register(MaxHp, max: Operand.Attribute(Hp));
        Assert.Throws<InvalidOperationException>(() => cyclic.Build());

        var meaningless = new AttributeRegistryBuilder();
        meaningless.Register(Hp, max: Operand.Constant(100), onMaxIncrease: MaxIncreasePolicy.Follow);
        Assert.Throws<InvalidOperationException>(() => meaningless.Build());   // max가 속성 참조 아님

        var duplicated = new AttributeRegistryBuilder();
        duplicated.Register(Hp);
        Assert.Throws<InvalidOperationException>(() => duplicated.Register(Hp));

        var frozen = new AttributeRegistryBuilder();
        frozen.Register(Hp);
        frozen.Build();
        Assert.Throws<InvalidOperationException>(() => frozen.Register(Mp));
    }
}
```

- [ ] **Step 2: 실패 확인** — 필터 `AttributeRegistryTests`, 컴파일 오류 확인.

- [ ] **Step 3: 구현**

`AttributeRegistry.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>속성 하나의 클램프·정책 정의입니다.</summary>
    public readonly struct AttributeDefinition
    {
        internal AttributeDefinition(
            Operand? min, Operand? max,
            MaxIncreasePolicy onMaxIncrease, MaxDecreasePolicy onMaxDecrease)
        {
            Min = min;
            Max = max;
            OnMaxIncrease = onMaxIncrease;
            OnMaxDecrease = onMaxDecrease;
        }

        /// <summary>하한 경계이며 없으면 null입니다.</summary>
        public Operand? Min { get; }

        /// <summary>상한 경계이며 없으면 null입니다.</summary>
        public Operand? Max { get; }

        /// <summary>max 경계 상승 시 Base 동반 정책입니다.</summary>
        public MaxIncreasePolicy OnMaxIncrease { get; }

        /// <summary>max 경계 하락 시 Base 처리 정책입니다.</summary>
        public MaxDecreasePolicy OnMaxDecrease { get; }
    }

    /// <summary>기동 시 한 번 만들어져 변하지 않는 속성 정의 레지스트리입니다.</summary>
    public sealed class AttributeRegistry
    {
        private readonly Dictionary<ushort, AttributeDefinition> _definitions;
        private readonly ushort[] _evaluationOrder;
        private readonly Dictionary<ushort, ushort[]> _clampDependents;
        private static readonly ushort[] Empty = Array.Empty<ushort>();

        internal AttributeRegistry(
            Dictionary<ushort, AttributeDefinition> definitions,
            ushort[] evaluationOrder,
            Dictionary<ushort, ushort[]> clampDependents)
        {
            _definitions = definitions;
            _evaluationOrder = evaluationOrder;
            _clampDependents = clampDependents;
        }

        /// <summary>등록된 속성 수입니다.</summary>
        public int Count => _definitions.Count;

        /// <summary>속성 id가 등록되어 있는지 확인합니다.</summary>
        public bool Contains(ushort attributeId) => _definitions.ContainsKey(attributeId);

        internal AttributeDefinition GetDefinition(ushort attributeId) => _definitions[attributeId];

        /// <summary>클램프 의존 위상 순서(동순위 id 오름차순)입니다.</summary>
        public ReadOnlySpan<ushort> EvaluationOrder => _evaluationOrder;

        /// <summary>이 속성을 클램프 경계로 참조하는 속성들(id 오름차순)입니다.</summary>
        public ReadOnlySpan<ushort> GetClampDependents(ushort attributeId) =>
            _clampDependents.TryGetValue(attributeId, out var dependents) ? dependents : Empty;
    }
}
```

`AttributeRegistryBuilder.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>속성 정의를 수집한 뒤 Build에서 일괄 검증·확정하는 빌더입니다. 등록 순서는 결과에 영향을 주지 않습니다.</summary>
    public sealed class AttributeRegistryBuilder
    {
        private readonly Dictionary<ushort, AttributeDefinition> _definitions = new Dictionary<ushort, AttributeDefinition>();
        private bool _built;

        /// <summary>속성 정의를 등록합니다. 전방 참조를 허용하며 검증은 Build에서 일괄 수행합니다.</summary>
        public void Register(
            ushort attributeId,
            Operand? min = null,
            Operand? max = null,
            MaxIncreasePolicy onMaxIncrease = MaxIncreasePolicy.Stay,
            MaxDecreasePolicy onMaxDecrease = MaxDecreasePolicy.Follow)
        {
            if (_built) throw new InvalidOperationException("Build 후에는 등록할 수 없습니다.");
            if (_definitions.ContainsKey(attributeId))
                throw new InvalidOperationException($"속성 {attributeId}이(가) 중복 등록되었습니다.");

            _definitions.Add(attributeId, new AttributeDefinition(min, max, onMaxIncrease, onMaxDecrease));
        }

        /// <summary>참조·순환·정책 정합성을 검증하고 위상 순서·후손 목록을 계산해 불변 레지스트리를 만듭니다.</summary>
        public AttributeRegistry Build()
        {
            _built = true;
            var dependencyLists = new Dictionary<ushort, List<ushort>>();
            foreach (var pair in _definitions)
            {
                ValidateBound(pair.Key, pair.Value.Min, dependencyLists);
                ValidateBound(pair.Key, pair.Value.Max, dependencyLists);
                if (pair.Value.OnMaxIncrease != MaxIncreasePolicy.Stay && !IsAttributeBound(pair.Value.Max))
                    throw new InvalidOperationException($"속성 {pair.Key}: MaxIncreasePolicy가 기본값이 아니면 max가 속성 참조여야 합니다.");
                if (pair.Value.OnMaxDecrease != MaxDecreasePolicy.Follow && !IsAttributeBound(pair.Value.Max))
                    throw new InvalidOperationException($"속성 {pair.Key}: MaxDecreasePolicy가 기본값이 아니면 max가 속성 참조여야 합니다.");
            }

            var order = TopologicalOrder(dependencyLists);
            var dependents = new Dictionary<ushort, ushort[]>();
            foreach (var pair in dependencyLists)
            {
                pair.Value.Sort();
                dependents.Add(pair.Key, pair.Value.ToArray());
            }

            return new AttributeRegistry(
                new Dictionary<ushort, AttributeDefinition>(_definitions), order, dependents);
        }

        private static bool IsAttributeBound(Operand? bound) =>
            bound.HasValue && bound.Value.Kind == OperandKind.Attribute;

        private void ValidateBound(ushort owner, Operand? bound, Dictionary<ushort, List<ushort>> dependencyLists)
        {
            if (!IsAttributeBound(bound)) return;
            var referenced = bound!.Value.AttributeId;
            if (!_definitions.ContainsKey(referenced))
                throw new InvalidOperationException($"속성 {owner}의 클램프가 미등록 속성 {referenced}을(를) 참조합니다.");

            if (!dependencyLists.TryGetValue(referenced, out var list))
            {
                list = new List<ushort>();
                dependencyLists.Add(referenced, list);
            }

            if (!list.Contains(owner)) list.Add(owner);
        }

        // Kahn — 준비 큐를 id 오름차순으로 뽑아 동순위 타이브레이크를 canonical로 만든다.
        private ushort[] TopologicalOrder(Dictionary<ushort, List<ushort>> dependents)
        {
            var inDegree = new Dictionary<ushort, int>();
            foreach (var id in _definitions.Keys) inDegree[id] = 0;
            foreach (var pair in dependents)
                foreach (var dependent in pair.Value) inDegree[dependent]++;

            var ready = new List<ushort>();
            foreach (var pair in inDegree)
                if (pair.Value == 0) ready.Add(pair.Key);

            var order = new ushort[_definitions.Count];
            var written = 0;
            while (ready.Count > 0)
            {
                ready.Sort();
                var current = ready[0];
                ready.RemoveAt(0);
                order[written++] = current;
                if (!dependents.TryGetValue(current, out var children)) continue;
                foreach (var child in children)
                {
                    if (--inDegree[child] == 0) ready.Add(child);
                }
            }

            if (written != order.Length)
                throw new InvalidOperationException("클램프 참조에 순환이 있습니다.");
            return order;
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — 필터 `AttributeRegistryTests`, PASS.
- [ ] **Step 5: 커밋** — `✨ AttributeRegistry Build 체인 추가`.

---

### Task 3: AttributeSet 기초 — 밀집 슬롯 · Base 항상 규칙 · 변경 이벤트

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Attributes/AttributeSet.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Attributes/AttributeChange.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/AttributeSetBasicTests.cs`

**Interfaces:**
- Consumes: Task 1–2.
- Produces: `AttributeChange`(readonly struct — `AttributeId: ushort`, `OldCurrent`, `NewCurrent`: BigNum). `AttributeSet` — `AttributeSet(AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds)`(아키타입 선언, 내부 슬롯은 id 오름차순 canonical), `bool Has(ushort)`, `BigNum GetBase(ushort)`, `BigNum GetCurrent(ushort)`, `void SetBase(ushort, BigNum)`, `void AddBase(ushort, BigNum)`, `ReadOnlySpan<AttributeChange> PendingChanges`, `void ClearChanges()`. Base 쓰기는 항상 클램프 통과(항상 규칙 2), Current 변경 시 이벤트 적재(old==new 미방출). 이 태스크에선 수정자·전파 없이 Base≈Current.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AttributeSetBasicTests
{
    private const ushort Hp = 1;
    private const ushort MaxHp = 2;
    private const ushort Attack = 5;

    private static AttributeRegistry BuildRegistry()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(MaxHp, min: Operand.Constant(1));
        builder.Register(Hp, min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        builder.Register(Attack);
        return builder.Build();
    }

    private static AttributeSet CreateSet()
    {
        var registry = BuildRegistry();
        Span<ushort> ids = stackalloc ushort[] { Attack, Hp, MaxHp };   // 순서 무관 — 내부 canonical
        return new AttributeSet(registry, ids);
    }

    [Test]
    public void Base_writes_always_pass_through_clamp()
    {
        var set = CreateSet();
        set.SetBase(MaxHp, 1000);
        set.SetBase(Hp, 800);

        set.AddBase(Hp, 500);                       // 과다 힐
        Assert.That(set.GetBase(Hp), Is.EqualTo((BigNum)1000));

        set.AddBase(Hp, -1300);                     // 치명 데미지
        Assert.That(set.GetBase(Hp), Is.EqualTo(BigNum.Zero));

        set.SetBase(Attack, -50);                   // 클램프 없는 속성은 자유
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)(-50)));
    }

    [Test]
    public void Change_events_carry_old_and_new_and_skip_no_ops()
    {
        var set = CreateSet();
        set.SetBase(MaxHp, 1000);
        set.ClearChanges();

        set.SetBase(Hp, 800);
        set.SetBase(Hp, 800);                       // 동일 값 — 미방출
        Assert.That(set.PendingChanges.Length, Is.EqualTo(1));
        Assert.That(set.PendingChanges[0].AttributeId, Is.EqualTo(Hp));
        Assert.That(set.PendingChanges[0].OldCurrent, Is.EqualTo(BigNum.Zero));
        Assert.That(set.PendingChanges[0].NewCurrent, Is.EqualTo((BigNum)800));

        set.ClearChanges();
        Assert.That(set.PendingChanges.Length, Is.Zero);
    }

    [Test]
    public void Unknown_attribute_access_throws_and_has_reports_membership()
    {
        var set = CreateSet();
        Assert.That(set.Has(Hp), Is.True);
        Assert.That(set.Has(999), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => set.GetCurrent(999));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.SetBase(999, 1));
    }
}
```

- [ ] **Step 2: 실패 확인** — 필터 `AttributeSetBasicTests`.

- [ ] **Step 3: 구현**

`AttributeChange.cs`:

```csharp
#nullable enable
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>속성 Current 변경 이벤트입니다. 복제 큐와 게임 구독이 소비합니다.</summary>
    public readonly struct AttributeChange
    {
        internal AttributeChange(ushort attributeId, BigNum oldCurrent, BigNum newCurrent)
        {
            AttributeId = attributeId;
            OldCurrent = oldCurrent;
            NewCurrent = newCurrent;
        }

        /// <summary>변경된 속성 id입니다.</summary>
        public ushort AttributeId { get; }

        /// <summary>변경 전 Current입니다.</summary>
        public BigNum OldCurrent { get; }

        /// <summary>변경 후 Current입니다.</summary>
        public BigNum NewCurrent { get; }
    }
}
```

`AttributeSet.cs` (이 태스크 범위 — 수정자 집계는 Task 4에서 확장):

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Attributes
{
    /// <summary>
    /// 아키타입이 선언한 속성들의 밀집 슬롯 집합입니다. Base 쓰기는 항상 클램프를 통과하고
    /// Current 변경은 이벤트 버퍼에 적재됩니다.
    /// </summary>
    public sealed class AttributeSet
    {
        private struct Slot
        {
            public ushort AttributeId;
            public BigNum Base;
            public BigNum SumAdd;
            public BigNum SumMulPct;
            public bool HasOverride;
            public BigNum OverrideValue;
            public BigNum Current;
            public bool Dirty;
        }

        private readonly AttributeRegistry _registry;
        private readonly Slot[] _slots;                    // AttributeId 오름차순 canonical
        private readonly int[] _slotByAttributeId;         // 희소 → 밀집 (등록 최대 id + 1 크기, -1 = 없음)
        private AttributeChange[] _changes = new AttributeChange[8];
        private int _changeCount;

        /// <summary>아키타입이 선언한 속성 id들로 밀집 슬롯을 만듭니다. 선언 순서는 무관합니다.</summary>
        public AttributeSet(AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            var ids = attributeIds.ToArray();
            Array.Sort(ids);
            var maxId = 0;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!registry.Contains(ids[i]))
                    throw new ArgumentException($"미등록 속성 {ids[i]}입니다.", nameof(attributeIds));
                if (i > 0 && ids[i] == ids[i - 1])
                    throw new ArgumentException($"속성 {ids[i]}이(가) 중복 선언되었습니다.", nameof(attributeIds));
                if (ids[i] > maxId) maxId = ids[i];
            }

            _slots = new Slot[ids.Length];
            _slotByAttributeId = new int[maxId + 1];
            Array.Fill(_slotByAttributeId, -1);
            for (var i = 0; i < ids.Length; i++)
            {
                _slots[i].AttributeId = ids[i];
                _slotByAttributeId[ids[i]] = i;
            }
        }

        /// <summary>이 집합이 해당 속성을 선언했는지 확인합니다.</summary>
        public bool Has(ushort attributeId) =>
            attributeId < _slotByAttributeId.Length && _slotByAttributeId[attributeId] >= 0;

        private int SlotIndex(ushort attributeId)
        {
            if (!Has(attributeId))
                throw new ArgumentOutOfRangeException(nameof(attributeId), attributeId, "선언되지 않은 속성입니다.");
            return _slotByAttributeId[attributeId];
        }

        /// <summary>영구값 Base를 가져옵니다.</summary>
        public BigNum GetBase(ushort attributeId) => _slots[SlotIndex(attributeId)].Base;

        /// <summary>집계·클램프가 반영된 Current를 가져옵니다.</summary>
        public BigNum GetCurrent(ushort attributeId) => _slots[SlotIndex(attributeId)].Current;

        /// <summary>Base를 설정합니다. 항상 클램프를 통과하며 Current가 즉시 갱신됩니다.</summary>
        public void SetBase(ushort attributeId, BigNum value)
        {
            var index = SlotIndex(attributeId);
            _slots[index].Base = ClampToBounds(index, value);
            ReapplyFormula(index);
        }

        /// <summary>Base에 델타를 더합니다. 항상 클램프를 통과합니다.</summary>
        public void AddBase(ushort attributeId, BigNum delta)
        {
            var index = SlotIndex(attributeId);
            SetBase(attributeId, _slots[index].Base + delta);
        }

        private BigNum ResolveBound(Operand bound)
        {
            if (bound.Kind == OperandKind.Constant) return bound.Value;
            // 클램프 경계의 속성 참조는 등록 시 검증됨 — 미선언이면 경계 없음으로 취급
            return Has(bound.AttributeId)
                ? _slots[_slotByAttributeId[bound.AttributeId]].Current * bound.Value
                : bound.Value * 0;
        }

        private BigNum ClampToBounds(int slotIndex, BigNum value)
        {
            var definition = _registry.GetDefinition(_slots[slotIndex].AttributeId);
            if (definition.Min.HasValue)
            {
                var min = ResolveBound(definition.Min.Value);
                if (value < min) value = min;
            }

            if (definition.Max.HasValue)
            {
                var max = ResolveBound(definition.Max.Value);
                if (value > max) value = max;
            }

            return value;
        }

        // 공식 재적용 — Σ 캐시 불변 경로 (O(1)). Task 4에서 집계, Task 5에서 전파가 이어진다.
        internal void ReapplyFormula(int slotIndex)
        {
            ref var slot = ref _slots[slotIndex];
            var value = slot.HasOverride
                ? slot.OverrideValue
                : (slot.Base + slot.SumAdd) * (BigNum.One + slot.SumMulPct);
            var clamped = ClampToBounds(slotIndex, value);
            var old = slot.Current;
            if (clamped.Equals(old)) return;
            slot.Current = clamped;
            EmitChange(slot.AttributeId, old, clamped);
        }

        private void EmitChange(ushort attributeId, BigNum oldCurrent, BigNum newCurrent)
        {
            if (_changeCount == _changes.Length)
                Array.Resize(ref _changes, _changes.Length * 2);
            _changes[_changeCount++] = new AttributeChange(attributeId, oldCurrent, newCurrent);
        }

        /// <summary>아직 소비되지 않은 변경 이벤트입니다.</summary>
        public ReadOnlySpan<AttributeChange> PendingChanges => _changes.AsSpan(0, _changeCount);

        /// <summary>변경 이벤트 버퍼를 비웁니다.</summary>
        public void ClearChanges() => _changeCount = 0;
    }
}
```

- [ ] **Step 4: 통과 확인** — 필터 `AttributeSetBasicTests`, PASS. 전체 실행으로 기존 테스트 무영향 확인.
- [ ] **Step 5: 커밋** — `✨ AttributeSet 밀집 슬롯과 Base 항상 규칙`.

---

### Task 4: 수정자 집계 — canonical 순서 · Override 규칙 · 셔플 불변 오라클

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Attributes/AttributeSet.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Attributes/IAttributeModifierSource.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/AttributeAggregationTests.cs`

**Interfaces:**
- Consumes: Task 3.
- Produces: `IAttributeModifierSource { ulong Id { get; } int Stack { get; } bool Enabled { get; } }`. `AttributeSet`에 추가 — internal `void AttachModifier(IAttributeModifierSource source, int rowIndex, ushort attributeId, AttributeModifierOp op, BigNum magnitude, bool scaleWithStack)`, internal `void DetachModifiers(IAttributeModifierSource source)`, internal `void MarkDirty(ushort attributeId)`, internal `void RebuildDirty()`. 재집계는 (source.Id, rowIndex) 오름차순 canonical, `Enabled == false` 항목 건너뜀, `scaleWithStack`이면 크기 × `Stack`. Override 복수 시 Id 최대 승리.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AttributeAggregationTests
{
    private const ushort Attack = 5;

    private sealed class FakeSource : IAttributeModifierSource
    {
        public ulong Id { get; set; }
        public int Stack { get; set; } = 1;
        public bool Enabled { get; set; } = true;
    }

    private static AttributeSet CreateSet()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(Attack, min: Operand.Constant(0));
        Span<ushort> ids = stackalloc ushort[] { Attack };
        return new AttributeSet(builder.Build(), ids);
    }

    [Test]
    public void Formula_applies_add_then_summed_multiply()
    {
        var set = CreateSet();
        set.SetBase(Attack, 100);
        var buff = new FakeSource { Id = 1 };
        set.AttachModifier(buff, 0, Attack, AttributeModifierOp.Add, 20, scaleWithStack: false);
        set.AttachModifier(buff, 1, Attack, AttributeModifierOp.Multiply, BigNum.FromParts(3, -1), scaleWithStack: false); // +30%
        var other = new FakeSource { Id = 2 };
        set.AttachModifier(other, 0, Attack, AttributeModifierOp.Multiply, BigNum.FromParts(2, -1), scaleWithStack: false); // +20%
        set.RebuildDirty();

        // (100 + 20) × (1 + 0.3 + 0.2) = 180 — 합산식
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)180));
    }

    [Test]
    public void Detach_restores_the_exact_previous_current()
    {
        var set = CreateSet();
        set.SetBase(Attack, 100);
        var before = set.GetCurrent(Attack);
        var buff = new FakeSource { Id = 7 };
        set.AttachModifier(buff, 0, Attack, AttributeModifierOp.Multiply, BigNum.FromParts(37, -2), scaleWithStack: false);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.Not.EqualTo(before));

        set.DetachModifiers(buff);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.EqualTo(before));   // 무흔적
    }

    [Test]
    public void Latest_override_wins_and_disabled_or_stacked_entries_behave()
    {
        var set = CreateSet();
        set.SetBase(Attack, 100);
        var early = new FakeSource { Id = 1 };
        var late = new FakeSource { Id = 9 };
        set.AttachModifier(late, 0, Attack, AttributeModifierOp.Override, 55, scaleWithStack: false);
        set.AttachModifier(early, 0, Attack, AttributeModifierOp.Override, 77, scaleWithStack: false);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)55));   // Id 최대 승리

        var stacked = new FakeSource { Id = 3, Stack = 4 };
        set.DetachModifiers(late);
        set.DetachModifiers(early);
        set.AttachModifier(stacked, 0, Attack, AttributeModifierOp.Add, 10, scaleWithStack: true);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)140)); // 100 + 10×4

        stacked.Enabled = false;
        set.MarkDirty(Attack);
        set.RebuildDirty();
        Assert.That(set.GetCurrent(Attack), Is.EqualTo((BigNum)100)); // 비활성 = 건너뜀
    }

    [Test]
    public void Aggregation_is_bit_identical_regardless_of_attach_order()
    {
        // canonical 순서 오라클 — BigNum 절사 비결합성 때문에 자명하지 않다.
        var random = new Random(20260817);
        for (var round = 0; round < 200; round++)
        {
            var entries = new List<(ulong Id, AttributeModifierOp Op, BigNum Magnitude)>();
            var count = random.Next(2, 12);
            for (var i = 0; i < count; i++)
            {
                var op = (AttributeModifierOp)random.Next(0, 2);   // Add | Multiply
                var mantissa = (long)random.Next(1, 1_000_000_000) * (random.Next(2) == 0 ? 1 : -1);
                entries.Add(((ulong)(i + 1), op, BigNum.FromParts(mantissa, random.Next(-6, 7))));
            }

            BigNum Aggregate(IEnumerable<int> order)
            {
                var set = CreateSet();
                set.SetBase(Attack, BigNum.FromParts(987_654_321_987_654_321, -3));
                foreach (var index in order)
                {
                    var entry = entries[index];
                    set.AttachModifier(new FakeSource { Id = entry.Id }, 0, Attack, entry.Op, entry.Magnitude, false);
                }
                set.RebuildDirty();
                return set.GetCurrent(Attack);
            }

            var forward = new List<int>();
            for (var i = 0; i < count; i++) forward.Add(i);
            var shuffled = new List<int>(forward);
            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            Assert.That(Aggregate(shuffled), Is.EqualTo(Aggregate(forward)),
                $"round {round}: 적용 순서가 결과를 바꿨습니다.");
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — 필터 `AttributeAggregationTests`, 컴파일 오류.

- [ ] **Step 3: 구현** — `IAttributeModifierSource.cs`:

```csharp
#nullable enable
namespace Bun3.Gameplay.Attributes
{
    /// <summary>수정자를 공급하는 소스(EffectInstance 등)가 집계에 노출하는 최소 상태입니다.</summary>
    public interface IAttributeModifierSource
    {
        /// <summary>World가 발급한 단조 증가 id — canonical 집계 순서의 근거입니다.</summary>
        ulong Id { get; }

        /// <summary>현재 스택 수입니다.</summary>
        int Stack { get; }

        /// <summary>Ongoing 조건 토글 상태입니다. false면 집계에서 건너뜁니다.</summary>
        bool Enabled { get; }
    }
}
```

`AttributeSet.cs`에 추가 (Slot에 `List<ModifierEntry>? Modifiers` 필드, 항목 struct, attach/detach/rebuild):

```csharp
        private struct ModifierEntry
        {
            public IAttributeModifierSource Source;
            public int RowIndex;
            public AttributeModifierOp Op;
            public BigNum Magnitude;
            public bool ScaleWithStack;
        }
        // Slot에 추가: public System.Collections.Generic.List<ModifierEntry>? Modifiers;

        internal void AttachModifier(
            IAttributeModifierSource source, int rowIndex, ushort attributeId,
            AttributeModifierOp op, BigNum magnitude, bool scaleWithStack)
        {
            var index = SlotIndex(attributeId);
            ref var slot = ref _slots[index];
            slot.Modifiers ??= new System.Collections.Generic.List<ModifierEntry>(4);
            var entry = new ModifierEntry
            {
                Source = source, RowIndex = rowIndex, Op = op,
                Magnitude = magnitude, ScaleWithStack = scaleWithStack,
            };
            // (Id, RowIndex) 오름차순 삽입 정렬 — 목록이 항상 canonical
            var position = slot.Modifiers.Count;
            while (position > 0)
            {
                var previous = slot.Modifiers[position - 1];
                if (previous.Source.Id < source.Id
                    || (previous.Source.Id == source.Id && previous.RowIndex <= rowIndex)) break;
                position--;
            }
            slot.Modifiers.Insert(position, entry);
            slot.Dirty = true;
        }

        internal void DetachModifiers(IAttributeModifierSource source)
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                var modifiers = _slots[i].Modifiers;
                if (modifiers is null) continue;
                for (var j = modifiers.Count - 1; j >= 0; j--)
                {
                    if (ReferenceEquals(modifiers[j].Source, source))
                    {
                        modifiers.RemoveAt(j);
                        _slots[i].Dirty = true;
                    }
                }
            }
        }

        internal void MarkDirty(ushort attributeId) => _slots[SlotIndex(attributeId)].Dirty = true;

        /// <summary>dirty 슬롯을 canonical 순서로 전체 재집계합니다. 호출 순서는 클램프 위상(레지스트리 EvaluationOrder)을 따릅니다.</summary>
        internal void RebuildDirty()
        {
            var order = _registry.EvaluationOrder;
            for (var i = 0; i < order.Length; i++)
            {
                if (!Has(order[i])) continue;
                var index = _slotByAttributeId[order[i]];
                if (!_slots[index].Dirty) continue;
                RebuildSlot(index);
            }
        }

        private void RebuildSlot(int index)
        {
            ref var slot = ref _slots[index];
            slot.Dirty = false;
            slot.SumAdd = BigNum.Zero;
            slot.SumMulPct = BigNum.Zero;
            slot.HasOverride = false;
            slot.OverrideValue = BigNum.Zero;
            var modifiers = slot.Modifiers;
            if (modifiers is not null)
            {
                for (var i = 0; i < modifiers.Count; i++)   // 목록이 canonical 정렬 유지
                {
                    var entry = modifiers[i];
                    if (!entry.Source.Enabled) continue;
                    var magnitude = entry.ScaleWithStack ? entry.Magnitude * entry.Source.Stack : entry.Magnitude;
                    switch (entry.Op)
                    {
                        case AttributeModifierOp.Add:
                            slot.SumAdd += magnitude;
                            break;
                        case AttributeModifierOp.Multiply:
                            slot.SumMulPct += magnitude;
                            break;
                        default:   // Override — 목록이 Id 순이라 마지막 활성 항목이 최신
                            slot.HasOverride = true;
                            slot.OverrideValue = magnitude;
                            break;
                    }
                }
            }

            ReapplyFormula(index);
        }
```

- [ ] **Step 4: 통과 확인** — 필터 `AttributeAggregationTests` PASS + 전체 무영향.
- [ ] **Step 5: 커밋** — `✨ 수정자 canonical 집계와 셔플 불변 오라클`.

---

### Task 5: 경계 이동 정책과 즉시 전파

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Attributes/AttributeSet.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/AttributeClampPolicyTests.cs`

**Interfaces:**
- Consumes: Task 4.
- Produces: `ReapplyFormula`가 Current 변경 시 `AttributeRegistry.GetClampDependents`를 위상 순으로 즉시 전파. 전파 시 후손 정의의 `OnMaxIncrease == Follow`면 Base += Δ(자기 클램프 통과), `OnMaxDecrease`에 따라 Truncate(Base 기록)/Stay(Current만). 불변식: 어떤 관찰 시점에도 클램프 성립.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AttributeClampPolicyTests
{
    private const ushort Hp = 1;
    private const ushort MaxHp = 2;

    private static AttributeSet CreateSet(MaxIncreasePolicy increase, MaxDecreasePolicy decrease)
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(MaxHp, min: Operand.Constant(1));
        builder.Register(Hp,
            min: Operand.Constant(0),
            max: Operand.Attribute(MaxHp),
            onMaxIncrease: increase,
            onMaxDecrease: decrease);
        Span<ushort> ids = stackalloc ushort[] { Hp, MaxHp };
        var set = new AttributeSet(builder.Build(), ids);
        set.SetBase(MaxHp, 1000);
        return set;
    }

    [Test]
    public void Decrease_follow_truncates_base_permanently()
    {
        var set = CreateSet(MaxIncreasePolicy.Stay, MaxDecreasePolicy.Follow);
        set.SetBase(Hp, 800);
        set.SetBase(MaxHp, 500);                       // 저주
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)500));   // 즉시 전파 — 관찰 창 없음
        Assert.That(set.GetBase(Hp), Is.EqualTo((BigNum)500));      // Base 기록

        set.SetBase(MaxHp, 1000);                      // 저주 해제
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)500));   // 소실 영구
    }

    [Test]
    public void Decrease_stay_preserves_base_and_restores_on_bound_return()
    {
        var set = CreateSet(MaxIncreasePolicy.Stay, MaxDecreasePolicy.Stay);
        set.SetBase(Hp, 800);
        set.SetBase(MaxHp, 500);
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)500));   // 안전망
        Assert.That(set.GetBase(Hp), Is.EqualTo((BigNum)800));      // 보존

        set.SetBase(MaxHp, 1000);
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)800));   // 복원
    }

    [Test]
    public void Increase_follow_carries_delta_and_buff_cycling_heals()
    {
        var set = CreateSet(MaxIncreasePolicy.Follow, MaxDecreasePolicy.Follow);
        set.SetBase(Hp, 600);

        set.SetBase(MaxHp, 1500);                      // +500 버프
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)1100)); // Δ 동반

        set.SetBase(MaxHp, 1000);                      // 버프 만료
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)1000)); // 잘림 — 순 +400 (알려진 성질)
    }

    [Test]
    public void Increase_stay_leaves_base_untouched()
    {
        var set = CreateSet(MaxIncreasePolicy.Stay, MaxDecreasePolicy.Follow);
        set.SetBase(Hp, 600);
        set.SetBase(MaxHp, 2000);
        Assert.That(set.GetCurrent(Hp), Is.EqualTo((BigNum)600));
    }
}
```

- [ ] **Step 2: 실패 확인** — 필터 `AttributeClampPolicyTests` (전파 미구현으로 500/1100 대신 이전 값 관찰).

- [ ] **Step 3: 구현** — `ReapplyFormula` 끝에 전파 추가:

```csharp
        internal void ReapplyFormula(int slotIndex)
        {
            // ... (기존 본문: clamped 계산, 변경 없으면 return, Current 갱신·이벤트) ...
            var oldCurrent = old;               // 기존 지역변수
            var newCurrent = clamped;
            PropagateToDependents(slot.AttributeId, oldCurrent, newCurrent);
        }

        private void PropagateToDependents(ushort changedAttributeId, BigNum oldValue, BigNum newValue)
        {
            var dependents = _registry.GetClampDependents(changedAttributeId);
            for (var i = 0; i < dependents.Length; i++)
            {
                if (!Has(dependents[i])) continue;
                var index = _slotByAttributeId[dependents[i]];
                var definition = _registry.GetDefinition(dependents[i]);
                var referencesAsMax = definition.Max.HasValue
                    && definition.Max.Value.Kind == OperandKind.Attribute
                    && definition.Max.Value.AttributeId == changedAttributeId;

                if (referencesAsMax && newValue > oldValue
                    && definition.OnMaxIncrease == MaxIncreasePolicy.Follow)
                {
                    var delta = (newValue - oldValue) * definition.Max.Value.Value;   // 계수 반영
                    _slots[index].Base = ClampToBounds(index, _slots[index].Base + delta);
                }

                if (referencesAsMax && newValue < oldValue
                    && definition.OnMaxDecrease == MaxDecreasePolicy.Follow)
                {
                    _slots[index].Base = ClampToBounds(index, _slots[index].Base);    // 경계로 잘라 기록
                }

                ReapplyFormula(index);   // Stay는 안전망만 — 재적용이 처리 (전파는 위상 DAG라 재귀 안전)
            }
        }
```

- [ ] **Step 4: 통과 확인** — 필터 + 전체.
- [ ] **Step 5: 커밋** — `✨ 클램프 즉시 전파와 경계 이동 정책`.

---

### Task 6: 시섬 계약 — 인터페이스 · IRng · SeamRegistry

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/IRng.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Seams/Seams.cs` (인터페이스 3종 — 컨텍스트는 Task 8~10에서 파이프라인과 함께)
- Create: `common/src/com.bun3.gameplay/Runtime/Seams/SeamRegistryBuilder.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Seams/SeamRegistry.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/TargetId.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/SeamRegistryTests.cs`

**Interfaces:**
- Consumes: 기존 `GameplayTag`/`TagCatalog`/`TagCatalogJson`(테스트 카탈로그 생성), Task 1.
- Produces:
  - `TargetId`(readonly struct, `ulong Value`, `IEquatable`, `CompareTo`).
  - `IRng { uint NextUInt32(); }`, `XorShiftRng(ulong seed)` — xorshift64\*, seed 0 금지(예외).
  - `IMagnitudeCalc { BigNum Calculate(in MagnitudeContext ctx); }`, `IExecutionCalc { void Execute(ref ExecutionContext ctx); }`, `ITargetSelector { int Select(in SelectorContext ctx, Span<TargetId> results); }` — 컨텍스트 3종은 이 태스크에서 **빈 껍데기 ref struct로 선언**(필드는 후속 태스크가 채움, 시그니처 고정 목적).
  - `SeamRegistryBuilder` — `RegisterMagnitudeCalc(GameplayTag, IMagnitudeCalc)`, `RegisterExecutionCalc(GameplayTag, IExecutionCalc)`, `RegisterTargetSelector(GameplayTag, ITargetSelector)`, `SeamRegistry Build(TagCatalog catalog)`. Build 검증: 태그가 각각 `calc.magnitude` / `calc.execution` / `selector` 서브트리 소속(`TagCatalog.IsAncestorOrSelf`), 중복 등록 금지, 예약 루트 태그 자체 등록 금지.
  - `SeamRegistry` — internal `IMagnitudeCalc GetMagnitudeCalc(GameplayTag)`, `IExecutionCalc GetExecutionCalc(GameplayTag)`, `ITargetSelector GetTargetSelector(GameplayTag)`, `bool TryGet...` 3종.

- [ ] **Step 1: 실패하는 테스트 작성** — 테스트 카탈로그는 기존 `TagCatalogJson.Load` 픽스처 패턴으로 `calc.magnitude.x`, `calc.execution.dmg`, `selector.team`, `state.dead` 태그를 만든다.

```csharp
#nullable enable
using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class SeamRegistryTests
{
    private static TagCatalog LoadCatalog()
    {
        const string json = "{\"schemaVersion\":1,\"tags\":[" +
            "{\"name\":\"calc.magnitude.x\"},{\"name\":\"calc.execution.dmg\"}," +
            "{\"name\":\"selector.team\"},{\"name\":\"state.dead\"}]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return TagCatalogJson.Load(stream);
    }

    private sealed class FixedMagnitude : IMagnitudeCalc
    {
        public BigNum Calculate(in MagnitudeContext ctx) => 7;
    }

    [Test]
    public void Registered_seam_resolves_by_tag()
    {
        var catalog = LoadCatalog();
        var builder = new SeamRegistryBuilder();
        var calc = new FixedMagnitude();
        builder.RegisterMagnitudeCalc(catalog.GetRequired("calc.magnitude.x"), calc);
        var registry = builder.Build(catalog);
        Assert.That(registry.GetMagnitudeCalc(catalog.GetRequired("calc.magnitude.x")), Is.SameAs(calc));
    }

    [Test]
    public void Build_rejects_wrong_root_duplicate_and_root_itself()
    {
        var catalog = LoadCatalog();

        var wrongRoot = new SeamRegistryBuilder();
        wrongRoot.RegisterMagnitudeCalc(catalog.GetRequired("state.dead"), new FixedMagnitude());
        Assert.Throws<InvalidOperationException>(() => wrongRoot.Build(catalog));

        var duplicated = new SeamRegistryBuilder();
        duplicated.RegisterMagnitudeCalc(catalog.GetRequired("calc.magnitude.x"), new FixedMagnitude());
        Assert.Throws<InvalidOperationException>(
            () => duplicated.RegisterMagnitudeCalc(catalog.GetRequired("calc.magnitude.x"), new FixedMagnitude()));

        var rootItself = new SeamRegistryBuilder();
        rootItself.RegisterMagnitudeCalc(catalog.GetRequired("calc.magnitude"), new FixedMagnitude());
        Assert.Throws<InvalidOperationException>(() => rootItself.Build(catalog));
    }

    [Test]
    public void XorShift_is_deterministic_and_rejects_zero_seed()
    {
        var a = new XorShiftRng(42);
        var b = new XorShiftRng(42);
        for (var i = 0; i < 100; i++)
            Assert.That(a.NextUInt32(), Is.EqualTo(b.NextUInt32()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new XorShiftRng(0));
    }
}
```

- [ ] **Step 2: 실패 확인.**
- [ ] **Step 3: 구현.** `IRng.cs`(xorshift64\*: `x ^= x >> 12; x ^= x << 25; x ^= x >> 27; return (uint)((x * 0x2545F4914F6CDD1D) >> 32)`), `TargetId.cs`, `Seams.cs`(인터페이스 3종 + 빈 `MagnitudeContext`/`ExecutionContext`/`SelectorContext` ref struct 선언), `SeamRegistryBuilder`/`SeamRegistry`(딕셔너리 3개, Build에서 카탈로그로 루트 태그 해석 후 `IsAncestorOrSelf(root, tag) && tag != root` 검증 — 루트 미존재 시 "예약 루트 태그가 카탈로그에 없습니다" 예외).
- [ ] **Step 4: 통과 확인.**
- [ ] **Step 5: 커밋** — `✨ 시섬 인터페이스·IRng·SeamRegistry`.

---

### Task 7: EffectSpec 데이터 모델과 EffectCatalogBuilder 검증

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/EffectSpec.cs` (spec + ModifierDef/MagnitudeDef/ExecutionDef/ConditionDef/ChainEdgeDef/StackPolicy + enum들)
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/EffectCatalogBuilder.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/EffectCatalog.cs` (+ internal `CompiledEffectSpec`)
- Test: `common/tests/Bun3.Gameplay.Tests/EffectCatalogBuilderTests.cs`

**Interfaces:**
- Consumes: Task 1·2·6, 기존 태그 타입.
- Produces (저작 모델 — 전부 public, 로더·게임이 채움):
  - `EffectDurationType { Instant, Duration, Infinite }`, `StackReapply { Refresh, AddStack }`, `StackExpiration { ClearAll, RemoveOneAndRefresh }`, `StackOverflow { Deny, ApplyEffect }`, `ChainTrigger { OnApplication, OnCompleteNormal, OnCompletePrematurely, OnStackOverflow }`, `ChainLevelRule { Inherit, Fixed }`.
  - `MagnitudeDef { Operand? Base; Operand? PerLevel; string? CalcTag; }` — CalcTag 있으면 Base/PerLevel 없어야 함(검증).
  - `ModifierDef { ushort AttributeId; AttributeModifierOp Op; MagnitudeDef Magnitude; bool ScaleWithStack = true; }`
  - `ExecutionDef { string CalcTag; List<Operand> Inputs; }`
  - `ConditionDef { Operand Left; ComparisonOp Op; Operand Right; }`
  - `ChainEdgeDef { ChainTrigger Trigger; string EffectName; string? SelectorTag; List<BigNum> SelectorParams; List<ConditionDef> Conditions; ChainLevelRule LevelRule; int FixedLevel; }`
  - `StackPolicy { int MaxStack(0=스택없음); StackReapply OnReapply; int AddStackCount=1; bool RefreshDurationOnReapply=true; bool ResetPeriodOnReapply=false; StackExpiration OnExpiration; StackOverflow OnOverflow; string? OverflowEffectName; bool ClearStacksOnOverflow; }`
  - `EffectSpec { string Name; EffectDurationType DurationType; int DurationTicks; int PeriodTicks; StackPolicy Stack; List<ModifierDef> Modifiers; List<ExecutionDef> Executions; List<ConditionDef> ApplicationConditions; List<ConditionDef> OngoingConditions; List<string> GrantedTags; List<string> AssetTags; List<string> ImmunityTags; List<ChainEdgeDef> Chains; }`
  - `EffectCatalogBuilder — void Add(EffectSpec)`, `EffectCatalog Build(TagCatalog, SeamRegistry, AttributeRegistry)`.
  - `EffectCatalog — int Count`, `int GetRequiredId(string name)`, `bool TryGetId(string name, out int id)`, internal `CompiledEffectSpec GetSpec(int id)`, `IReadOnlyList<string> BuildWarnings`(체인 순환 경고 문자열).
  - internal `CompiledEffectSpec` — 이름·타입·틱·스택 + 배열들: `CompiledModifier { ushort AttributeId; AttributeModifierOp Op; Operand? Base; Operand? PerLevel; IMagnitudeCalc? Calc; bool ScaleWithStack; }`, `CompiledExecution { IExecutionCalc Calc; Operand[] Inputs; }`, `CompiledCondition { Operand Left; ComparisonOp Op; Operand Right; }`(적용/지속 각각), `GameplayTag[] GrantedTags/AssetTags/ImmunityTags`, `CompiledChain { ChainTrigger Trigger; int EffectId; ITargetSelector? Selector; BigNum[] SelectorParams; CompiledCondition[] Conditions; ChainLevelRule LevelRule; int FixedLevel; }`, `int OverflowEffectId(-1=없음)`.

**Build 검증 규칙(스펙 §10 — 규칙 하나당 예외 메시지에 스펙 이름 포함):**
1. 이름 중복 금지, 이름은 비어 있을 수 없음.
2. Instant: `DurationTicks/PeriodTicks == 0`, OngoingConditions·GrantedTags·Stack.MaxStack 전부 비어야 함 — 위반 시 예외.
3. Duration: `DurationTicks > 0`. Infinite: `DurationTicks == 0`.
4. Executions는 Instant 또는 `PeriodTicks > 0`에서만.
5. `Stack.MaxStack == 0`인데 Overflow 정책이 ApplyEffect·`OnReapply == AddStack` → 예외.
6. 모든 태그 문자열·CalcTag·SelectorTag는 카탈로그·SeamRegistry에서 해석(미해석 = 예외). 시섬 태그 루트는 SeamRegistry가 이미 검증.
7. 모든 Operand의 속성 참조는 AttributeRegistry에 존재해야 함. `Kind == SourceAttribute`인
   Operand가 OngoingConditions에 있으면 예외 (클램프 경계의 금지는 Task 2의
   AttributeRegistryBuilder.Build가 담당 — min/max Operand의 Kind가 SourceAttribute면 예외).
8. 체인·Overflow의 EffectName 해석(미해석 = 예외).
9. MagnitudeDef: CalcTag XOR (Base 존재). PerLevel은 Base 있을 때만.
10. 체인 그래프 순환: OnApplication 엣지만 따라 닫히는 순환 → `BuildWarnings`에 "high" 경고, Duration/Period 보유 스펙 경유 순환 → "low" 경고. 예외 아님.

- [ ] **Step 1: 실패하는 테스트 작성** — 규칙별 케이스. 대표(전체는 규칙 1~10 각 1케이스 이상, 동일 패턴):

```csharp
[Test]
public void Build_rejects_instant_with_duration_only_fields()
{
    var spec = MinimalInstant("bad");
    spec.GrantedTags.Add("state.dead");
    var builder = new EffectCatalogBuilder();
    builder.Add(spec);
    var ex = Assert.Throws<InvalidOperationException>(() => BuildCatalog(builder));
    Assert.That(ex!.Message, Does.Contain("bad"));
}

[Test]
public void Application_only_cycle_is_a_high_warning_not_an_error()
{
    var a = MinimalInstant("a"); a.Chains.Add(Edge(ChainTrigger.OnApplication, "b"));
    var b = MinimalInstant("b"); b.Chains.Add(Edge(ChainTrigger.OnApplication, "a"));
    var builder = new EffectCatalogBuilder();
    builder.Add(a); builder.Add(b);
    var catalog = BuildCatalog(builder);
    Assert.That(catalog.BuildWarnings, Has.Some.Contains("high"));
}
```

(테스트 파일 상단에 `MinimalInstant(name)`/`MinimalDuration(name, ticks)`/`Edge(trigger, target)` 헬퍼와 Task 6의 카탈로그 JSON에 `effect.burn` 등 분류 태그를 추가한 `LoadCatalog`, 빈 `SeamRegistry`, `Hp/MaxHp` AttributeRegistry를 만드는 `BuildCatalog(builder)` 헬퍼를 정의한다 — 이후 태스크들이 같은 헬퍼를 재사용하므로 `EffectTestKit.cs`로 분리 생성: `common/tests/Bun3.Gameplay.Tests/EffectTestKit.cs`.)

- [ ] **Step 2: 실패 확인.**
- [ ] **Step 3: 구현** — 데이터 클래스는 프로퍼티 가방(로더 친화적으로 `List<>` 초기화 포함). Builder는 2패스: ① 이름→id 사전 구축 ② 스펙별 해석·검증·Compiled 생성. 순환 검출은 OnApplication 엣지 인접 리스트로 DFS(회색/검정), "경유 스펙에 Duration/Period 있음" 플래그로 심각도 분류.
- [ ] **Step 4: 통과 확인.**
- [ ] **Step 5: 커밋** — `✨ EffectSpec 모델과 EffectCatalog Build 검증`.

---

### Task 8: EffectTarget · 적용 큐 · Instant 경로 (면역·조건·Execution)

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/EffectTarget.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/EffectInstance.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/EffectLifecycleEvent.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/IEffectTargetResolver.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Seams/Seams.cs` (컨텍스트 필드 채움)
- Test: `common/tests/Bun3.Gameplay.Tests/EffectInstantTests.cs`

**Interfaces:**
- Consumes: Task 3–7.
- Produces:
  - `EffectInstance`(sealed class, `IAttributeModifierSource`) — `ulong Id`, `int SpecId`, `int RemainingTicks`, `int PeriodCountdown`, `int Stack`, `int Level`, `TargetId Source`, `bool Enabled`. internal 생성·풀.
  - `EffectLifecycleEvent { EffectLifecycleKind Kind; ulong InstanceId; int SpecId; int Stack; }`, `EffectLifecycleKind { Applied, Expired, RemovedPrematurely, StackChanged }`.
  - `EffectTarget(TargetId id, AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds, TagCatalog tagCatalog)` — `TargetId Id`, `AttributeSet Attributes`, `TagCountContainer Tags`, `int ActiveEffectCount`, `ReadOnlySpan<EffectLifecycleEvent> PendingEffectEvents`, `void ClearEffectEvents()`, internal 활성 목록(List, Id 순).
  - `IEffectTargetResolver { bool TryResolve(TargetId id, out EffectTarget target); IReadOnlyList<TargetId> TargetIds { get; } }` — TargetIds는 오름차순 유지 계약(문서).
  - `EffectPipeline(EffectCatalog catalog, IEffectTargetResolver resolver, IRng rng, int applyBudgetPerTick = 64)` — `long CurrentTick`, `void EnqueueApply(int specId, TargetId source, TargetId target, int level = 1)`, `void Tick()`. 이 태스크 범위: 페이즈 ①만(드레인·면역·적용조건·Instant의 Modifiers Base 가감·Executions 호출) + ③(RebuildDirty). 나머지 페이즈는 Task 9–10.
  - 컨텍스트 실구현: `MagnitudeContext`(readonly ref struct — `BigNum SourceAttr(ushort)`, `BigNum TargetAttr(ushort)`, `bool HasSource`, `int Level`, `int Stack`, `long WorldTick`), `ExecutionContext`(ref struct — Magnitude의 읽기 + `BigNum Input(int)`, `void WriteTarget(ushort, BigNum)`(=`AttributeSet.SetBase` 경유 — 항상 규칙·전파·이벤트 통과), `void ApplyToTarget(int specId)`(파이프라인 큐 적재, source=현재 source), `IRng Rng`), `SelectorContext`(readonly ref struct — `TargetId Source`, `BigNum Param(int)`, `int ParamCount`, `IRng Rng`).
  - 크기 평가(내부 정적): `Operand` 평가 = Constant→값, Attribute→대상 Current × 계수, SourceAttribute→소스 EffectTarget의 Current × 계수. **소스 미해석(TryResolve 실패)이면 SourceAttribute는 BigNum.Zero.** `MagnitudeDef` 평가 = Calc 있으면 Calc.Calculate, 아니면 `Base + PerLevel×(level-1)`. Task 8 테스트에 "시전자 공격력 × 1.2 데미지"(SourceAttribute) 케이스 추가: Attacker에 Attack 100 설정 → `Operand.SourceAttribute(Attack, 1.2)` 크기의 Instant로 Defender Hp가 120 감소.
  - 면역: 대상 활성 인스턴스의 스펙 ImmunityTags × 신규 스펙 AssetTags를 `TagCatalog.IsAncestorOrSelf(immunity, asset)`로 검사 — 하나라도 참이면 적용 무산(이벤트 없음, 통계 카운터).

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectInstantTests
{
    [Test]
    public void Instant_modifier_permanently_changes_base()
    {
        var kit = EffectTestKit.Create();                       // 카탈로그·레지스트리·타깃 2개(공/수) 조립
        var damage = EffectTestKit.MinimalInstant("hit");
        damage.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-30) },
        });
        kit.AddSpec(damage);
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);
        pipeline.EnqueueApply(kit.SpecId("hit"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)70));
        Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);   // Instant는 인스턴스 없음
    }

    [Test]
    public void Execution_calc_reads_inputs_and_writes_through_clamp()
    {
        var kit = EffectTestKit.Create();
        kit.RegisterExecutionCalc("calc.execution.dmg", new EffectTestKit.SubtractHpCalc()); // Input(0)만큼 Hp 감소
        var spell = EffectTestKit.MinimalInstant("spell");
        spell.Executions.Add(new ExecutionDef
        {
            CalcTag = "calc.execution.dmg",
            Inputs = { Operand.Attribute(EffectTestKit.MaxHp, BigNum.FromParts(5, -1)) }, // 최대체력의 50%
        });
        kit.AddSpec(spell);
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 200);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 150);
        pipeline.EnqueueApply(kit.SpecId("spell"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Hp), Is.EqualTo((BigNum)50));
    }

    [Test]
    public void Application_condition_and_immunity_block_application()
    {
        var kit = EffectTestKit.Create();
        var gated = EffectTestKit.MinimalInstant("gated");
        gated.ApplicationConditions.Add(new ConditionDef
        {
            Left = Operand.Attribute(EffectTestKit.Hp),
            Op = ComparisonOp.Less,
            Right = Operand.Attribute(EffectTestKit.MaxHp, BigNum.FromParts(3, -1)),
        });
        gated.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-10) },
        });
        kit.AddSpec(gated);

        var ward = EffectTestKit.MinimalInfinite("ward");
        ward.ImmunityTags.Add("effect.fire");
        kit.AddSpec(ward);
        var fireball = EffectTestKit.MinimalInstant("fireball");
        fireball.AssetTags.Add("effect.fire.bolt");
        fireball.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-25) },
        });
        kit.AddSpec(fireball);
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 90);

        pipeline.EnqueueApply(kit.SpecId("gated"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)90)); // Hp<30% 아님

        pipeline.EnqueueApply(kit.SpecId("ward"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("fireball"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)90)); // 면역 차단
    }
}
```

- [ ] **Step 2: 실패 확인.**
- [ ] **Step 3: 구현** — `EffectTestKit`에 조립 헬퍼(카탈로그 JSON에 `effect.fire.bolt` 포함, 딕셔너리 리졸버 스텁, `SubtractHpCalc`), 파이프라인 ①: `Queue<PendingApply>`(struct: SpecId/Source/Target/Level), 드레인 예산, 대상 해석 실패 드랍, 면역→적용조건→Instant 처리(Modifiers는 `MagnitudeDef` 평가 후 `AddBase`, Executions는 `ExecutionContext` 구성 후 호출), 각 드레인 항목 후 `RebuildDirty()`는 불필요(Instant는 Base 경로 즉시) — Duration 수정자 부착이 생기는 Task 9부터 ③이 유의미.
- [ ] **Step 4: 통과 확인.**
- [ ] **Step 5: 커밋** — `✨ EffectPipeline Instant 경로와 시섬 컨텍스트`.

---

### Task 9: Duration/Infinite — 인스턴스 · 스택 · GrantedTags · 주기

**Files:**
- Modify: `EffectPipeline.cs`, `EffectTarget.cs`, `EffectInstance.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/EffectDurationStackTests.cs`

**Interfaces:**
- Consumes: Task 8.
- Produces: 페이즈 ② 구현 — `Tick()`이 ① 후 대상들을 TargetId 순으로 순회: 인스턴스 Id 순으로 ttl 감소·주기 발화(주기 도래 시 Instant 실행과 동일 경로 — Modifiers Base 가감 + Executions), 만료 수집·제거(Id 순), 만료 시 스택 정책 `RemoveOneAndRefresh`면 스택 1 감소+지속 리셋. 적용 병합: 동일 SpecId 활성 인스턴스 존재 시 스택 정책 적용(Refresh/AddStack·지속 갱신·주기 리셋·MaxStack 클램프), `StackChanged` 이벤트. 신규 인스턴스: Duration 수정자 `AttachModifier`(크기는 적용 시점 평가 스냅샷), GrantedTags를 `Tags.Add`, `Applied` 이벤트. 만료: `DetachModifiers`+`Tags.Remove`+`Expired` 이벤트. 페이즈 ③: 전 대상 `RebuildDirty()`.

- [ ] **Step 1: 실패하는 테스트 작성** — 핵심 케이스:

```csharp
[Test]
public void Duration_buff_expires_without_trace()
{
    var kit = EffectTestKit.Create();
    var haste = EffectTestKit.MinimalDuration("haste", ticks: 3);
    haste.Modifiers.Add(new ModifierDef
    {
        AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Multiply,
        Magnitude = new MagnitudeDef { Base = Operand.Constant(BigNum.FromParts(2, -1)) },  // +20%
    });
    haste.GrantedTags.Add("state.hasted");
    kit.AddSpec(haste);
    var pipeline = kit.BuildPipeline();
    kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 100);
    var before = kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack);

    pipeline.EnqueueApply(kit.SpecId("haste"), kit.Attacker.Id, kit.Defender.Id);
    pipeline.Tick();
    Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)120));
    Assert.That(kit.Defender.Tags.Has(kit.Tag("state.hasted")), Is.True);

    pipeline.Tick(); pipeline.Tick(); pipeline.Tick();     // 3틱 경과 — 만료
    Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo(before));
    Assert.That(kit.Defender.Tags.Has(kit.Tag("state.hasted")), Is.False);
    Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);
}

[Test]
public void Stacking_reapply_and_max_clamp()
{
    var kit = EffectTestKit.Create();
    var chill = EffectTestKit.MinimalDuration("chill", ticks: 10);
    chill.Stack = new StackPolicy { MaxStack = 3, OnReapply = StackReapply.AddStack };
    chill.Modifiers.Add(new ModifierDef
    {
        AttributeId = EffectTestKit.Attack, Op = AttributeModifierOp.Add,
        Magnitude = new MagnitudeDef { Base = Operand.Constant(-5) },   // 중첩당 -5 (×stack 기본)
    });
    kit.AddSpec(chill);
    var pipeline = kit.BuildPipeline();
    kit.Defender.Attributes.SetBase(EffectTestKit.Attack, 100);

    for (var i = 0; i < 5; i++)
    {
        pipeline.EnqueueApply(kit.SpecId("chill"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
    }

    Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));         // 대상 기준 병합
    Assert.That(kit.Defender.Attributes.GetCurrent(EffectTestKit.Attack), Is.EqualTo((BigNum)85)); // 3중첩 클램프
}

[Test]
public void Periodic_ticks_execute_after_each_period_and_survive_dispel_permanently()
{
    var kit = EffectTestKit.Create();
    var poison = EffectTestKit.MinimalDuration("poison", ticks: 6);
    poison.PeriodTicks = 2;
    poison.Modifiers.Add(new ModifierDef
    {
        AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
        Magnitude = new MagnitudeDef { Base = Operand.Constant(-10) },
    });
    kit.AddSpec(poison);
    var pipeline = kit.BuildPipeline();
    kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
    kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);

    pipeline.EnqueueApply(kit.SpecId("poison"), kit.Attacker.Id, kit.Defender.Id);
    pipeline.Tick();                                        // 적용 틱 — 발화 없음(첫 주기 경과 전)
    Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));

    for (var i = 0; i < 6; i++) pipeline.Tick();            // 6틱 = 3회 발화 후 만료
    Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)70));
    Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);   // 깎인 Hp는 복원되지 않음
}
```

- [ ] **Step 2: 실패 확인.** — Duration 스펙에 필요한 태그(`state.hasted`)를 EffectTestKit 카탈로그 JSON에 추가.
- [ ] **Step 3: 구현** — 인스턴스 풀(간단 스택 풀, 재사용 시 필드 리셋), 병합·주기·만료 로직. 주기 발화는 `PeriodCountdown` 감소 후 0 도달 시 실행+리셋.
- [ ] **Step 4: 통과 확인.**
- [ ] **Step 5: 커밋** — `✨ Duration 수명·스택 기계·주기 실행`.

---

### Task 10: 페이즈 파이프라인 완성 — Ongoing 토글 · 체인 4종 · 만감 · 예산

**Files:**
- Modify: `EffectPipeline.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/EffectChainConditionTests.cs`

**Interfaces:**
- Consumes: Task 9.
- Produces: `Tick()` = ①드레인(예산, OnApplication 체인은 같은 큐 — 예산 내 같은 틱) → ②시간(만료 체인은 큐 적재 — 다음 틱) → ③재계산 1차 → ④Ongoing 일괄 평가(enabled 토글, GrantedTags on/off — `Tags.Add/Remove`) → ⑤재계산 2차(토글분) → ⑥이벤트 확정(별도 동작 없음 — 버퍼는 게임이 드레인). 체인 발화: 엣지 조건 평가 → 대상 결정(SelectorTag 없으면 원 대상, 있으면 Selector 호출 — `Span<TargetId>` 스택 버퍼 최대 32) → `EnqueueApply`(source 승계, 레벨 규칙). OnStackOverflow: 병합 시 스택이 MaxStack 초과분 발생하면 발화(+ClearStacksOnOverflow). 예산 초과분 이월 + `PendingApplyCount` 노출.

- [ ] **Step 1: 실패하는 테스트 작성** — 핵심 케이스:

```csharp
[Test]
public void Stack_overflow_chain_freezes_and_resets()   // "빙결 3중첩 → 동결" — 스펙의 합격선
{
    var kit = EffectTestKit.Create();
    var frozen = EffectTestKit.MinimalDuration("frozen", ticks: 2);
    frozen.GrantedTags.Add("state.frozen");
    kit.AddSpec(frozen);
    var chill = EffectTestKit.MinimalDuration("chill", ticks: 10);
    chill.Stack = new StackPolicy
    {
        MaxStack = 3, OnReapply = StackReapply.AddStack,
        OnOverflow = StackOverflow.ApplyEffect, OverflowEffectName = "frozen", ClearStacksOnOverflow = true,
    };
    kit.AddSpec(chill);
    var pipeline = kit.BuildPipeline();

    for (var i = 0; i < 4; i++)   // 4번째에서 만감 초과
    {
        pipeline.EnqueueApply(kit.SpecId("chill"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
    }
    pipeline.Tick();              // 체인 이월분 처리
    Assert.That(kit.Defender.Tags.Has(kit.Tag("state.frozen")), Is.True);
}

[Test]
public void Ongoing_condition_toggles_once_per_tick_without_removal()
{
    var kit = EffectTestKit.Create();
    var lowHp = EffectTestKit.MinimalInfinite("lowhp");
    lowHp.OngoingConditions.Add(new ConditionDef
    {
        Left = Operand.Attribute(EffectTestKit.Hp),
        Op = ComparisonOp.Less,
        Right = Operand.Attribute(EffectTestKit.MaxHp, BigNum.FromParts(3, -1)),
    });
    lowHp.GrantedTags.Add("state.lowhealth");
    kit.AddSpec(lowHp);
    var pipeline = kit.BuildPipeline();
    kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
    kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);

    pipeline.EnqueueApply(kit.SpecId("lowhp"), kit.Defender.Id, kit.Defender.Id);
    pipeline.Tick();
    Assert.That(kit.Defender.Tags.Has(kit.Tag("state.lowhealth")), Is.False);

    kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 20);
    pipeline.Tick();
    Assert.That(kit.Defender.Tags.Has(kit.Tag("state.lowhealth")), Is.True);
    Assert.That(kit.Defender.ActiveEffectCount, Is.EqualTo(1));   // 제거 아님 — 토글

    kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 80);
    pipeline.Tick();
    Assert.That(kit.Defender.Tags.Has(kit.Tag("state.lowhealth")), Is.False);
}

[Test]
public void Expiry_chain_fires_next_tick_and_selector_routes_targets()
{
    var kit = EffectTestKit.Create();
    kit.RegisterSelector("selector.everyone", new EffectTestKit.AllTargetsSelector());
    var blast = EffectTestKit.MinimalInstant("blast");
    blast.Modifiers.Add(new ModifierDef
    {
        AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
        Magnitude = new MagnitudeDef { Base = Operand.Constant(-15) },
    });
    kit.AddSpec(blast);
    var bomb = EffectTestKit.MinimalDuration("bomb", ticks: 2);
    bomb.Chains.Add(new ChainEdgeDef
    {
        Trigger = ChainTrigger.OnCompleteNormal, EffectName = "blast", SelectorTag = "selector.everyone",
    });
    kit.AddSpec(bomb);
    var pipeline = kit.BuildPipeline();
    foreach (var target in kit.AllTargets) { target.Attributes.SetBase(EffectTestKit.MaxHp, 100); target.Attributes.SetBase(EffectTestKit.Hp, 100); }

    pipeline.EnqueueApply(kit.SpecId("bomb"), kit.Attacker.Id, kit.Defender.Id);
    pipeline.Tick(); pipeline.Tick(); pipeline.Tick();     // 만료 (2틱) 후
    Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100)); // 아직 — 1틱 지연
    pipeline.Tick();                                        // 다음 틱 ①에서 폭발
    foreach (var target in kit.AllTargets)
        Assert.That(target.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)85));
}

[Test]
public void Apply_budget_carries_over_and_survives_a_cyclic_chain()
{
    var kit = EffectTestKit.Create();
    var a = EffectTestKit.MinimalInstant("a"); a.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "b"));
    var b = EffectTestKit.MinimalInstant("b"); b.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "a"));
    kit.AddSpec(a); kit.AddSpec(b);
    var pipeline = kit.BuildPipeline(applyBudgetPerTick: 8);

    pipeline.EnqueueApply(kit.SpecId("a"), kit.Attacker.Id, kit.Defender.Id);
    for (var i = 0; i < 10; i++) pipeline.Tick();          // 라이브락 없이 진행
    Assert.That(pipeline.PendingApplyCount, Is.LessThanOrEqualTo(1));   // 틱당 예산으로 유계
}
```

- [ ] **Step 2: 실패 확인.**
- [ ] **Step 3: 구현** — 페이즈 순서를 `Tick()`에 명시적 메서드 6개로 분리(`DrainApplications/AdvanceTime/RebuildAll/EvaluateOngoing/RebuildToggled/`⑥은 주석). Ongoing 평가는 인스턴스 Id 순, 조건은 대상 속성 Current로 `CompiledCondition` 비교(BigNum CompareTo).
- [ ] **Step 4: 통과 확인.**
- [ ] **Step 5: 커밋** — `✨ 틱 6페이즈·체인 4종·만감·예산`.

---

### Task 11: 제거 API — RemoveByTags/RemoveById · Prematurely 체인

**Files:**
- Modify: `EffectPipeline.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/EffectRemovalTests.cs`

**Interfaces:**
- Consumes: Task 10.
- Produces: `int RemoveByTags(TargetId target, TagContainer query)` — 활성 인스턴스 중 스펙 AssetTags가 query와 계층 매칭(`query.Has(assetTag)`)되는 것 전부 즉시 제거(Id 순), `RemovedPrematurely` 이벤트 + `OnCompletePrematurely` 체인 큐 적재, 제거 수 반환. `bool RemoveById(TargetId, ulong instanceId)` 동일 경로. `OnCompleteNormal`은 발화하지 않음을 검증.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
[Test]
public void Dispel_fires_premature_chain_but_not_normal()
{
    var kit = EffectTestKit.Create();
    var backlash = EffectTestKit.MinimalInstant("backlash");
    backlash.Modifiers.Add(new ModifierDef
    {
        AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
        Magnitude = new MagnitudeDef { Base = Operand.Constant(-40) },
    });
    kit.AddSpec(backlash);
    var reward = EffectTestKit.MinimalInstant("reward");
    reward.Modifiers.Add(new ModifierDef
    {
        AttributeId = EffectTestKit.Hp, Op = AttributeModifierOp.Add,
        Magnitude = new MagnitudeDef { Base = Operand.Constant(+40) },
    });
    kit.AddSpec(reward);

    var curse = EffectTestKit.MinimalDuration("curse", ticks: 100);
    curse.AssetTags.Add("effect.magic.curse");
    curse.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnCompletePrematurely, "backlash"));
    curse.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnCompleteNormal, "reward"));
    kit.AddSpec(curse);
    var pipeline = kit.BuildPipeline();
    kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
    kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 100);

    pipeline.EnqueueApply(kit.SpecId("curse"), kit.Attacker.Id, kit.Defender.Id);
    pipeline.Tick();

    var dispel = kit.TagCatalog.CreateContainer();
    dispel.Add(kit.Tag("effect.magic"));                    // 계층 매칭 — curse 포함
    Assert.That(pipeline.RemoveByTags(kit.Defender.Id, dispel), Is.EqualTo(1));
    Assert.That(kit.Defender.ActiveEffectCount, Is.Zero);

    pipeline.Tick();                                        // 체인 처리
    Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)60)); // backlash만
}
```

- [ ] **Step 2: 실패 확인.** (`effect.magic.curse` 태그를 카탈로그 JSON에 추가.)
- [ ] **Step 3: 구현.**
- [ ] **Step 4: 통과 확인.**
- [ ] **Step 5: 커밋** — `✨ 디스펠 API와 Prematurely 체인 구분`.

---

### Task 12: BigNum.TryParse + JSON 로더 · 두 경로 동등성

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.Format.cs` (TryParse 추가)
- Create: `common/src/com.bun3.gameplay/Catalog/Source/EffectSpecJson.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/BigNumParseTests.cs`, `common/tests/Bun3.Gameplay.Tests/EffectSpecJsonTests.cs`

**Interfaces:**
- Consumes: Task 7 모델, 기존 `TagCatalogJson.StrictJsonSyntax`(internal — 같은 어셈블리).
- Produces:
  - `public static bool BigNum.TryParse(ReadOnlySpan<char> text, out BigNum value)` — invariant, `-?\d+(\.\d+)?([eE][+-]?\d+)?`. 가수 유효 19자리 초과는 절사(0 방향), 실패 시 false.
  - `public static class EffectSpecJson { public static List<EffectSpec> Load(Stream utf8Json, IReadOnlyDictionary<string, ushort> attributeNames); }` — strict 검증(기존 `StrictJsonSyntax` 재사용, 중복 키 거부, 미지 필드 거부, 줄·열 포함 `TagCatalogException`). 스키마:

```json
{ "schemaVersion": 1,
  "specs": [ {
    "name": "chill",
    "duration": { "type": "Duration", "ticks": 10, "periodTicks": 0 },
    "stack": { "maxStack": 3, "onReapply": "AddStack",
               "onOverflow": "ApplyEffect", "overflowEffect": "frozen", "clearStacksOnOverflow": true },
    "modifiers": [ { "attribute": "Attack", "op": "Add",
                     "magnitude": { "constant": "-5" }, "scaleWithStack": true } ],
    "executions": [ { "calc": "calc.execution.dmg",
                      "inputs": [ { "attribute": "MaxHp", "coefficient": "0.5" } ] } ],
    "applicationConditions": [ { "left": { "attribute": "Hp" }, "op": "Less",
                                 "right": { "attribute": "MaxHp", "coefficient": "0.3" } } ],
    "ongoingConditions": [],
    "grantedTags": [ "state.chilled" ],
    "assetTags": [ "effect.frost" ],
    "immunityTags": [],
    "chains": [ { "trigger": "OnStackOverflow", "effect": "frozen" } ] } ] }
```

  피연산자 JSON — 프로퍼티 이름이 곧 판별자: `{"constant":"50"}` | `{"attribute":"Hp","coefficient":"0.3"}` | `{"sourceAttribute":"AttackPower","coefficient":"1.2"}`. magnitude에 추가로 `{"calc":"calc.magnitude.x"}` | `{"base":{...},"perLevel":{...}}`. BigNum 리터럴은 항상 문자열(JSON number 금지 — double 경유 차단).

- [ ] **Step 1: TryParse 실패 테스트** — `"50"`, `"-1.5"`, `"0.3"`, `"1.23e45"`, `"9999999999999999999999"`(절사) 성공 / `""`, `"abc"`, `"1.2.3"`, `"1e"` 실패. 성공값은 `FromParts` 기대치와 동등 비교.
- [ ] **Step 2: 실패 확인 → TryParse 구현 → 통과.** 파싱: 부호 → 정수부 자릿수 수집(long, 19자리 초과분은 지수로) → 소수부(지수 감산) → e지수 가산 → `Canonicalize` 경유(`FromParts`).
- [ ] **Step 3: 로더 실패 테스트** — 위 스키마 JSON을 로드해 `EffectSpec` 필드 일치 검증 + 오류 케이스(미지 필드, BigNum이 number로 옴, 미지 enum 이름, attributeNames에 없는 속성 이름 — 전부 줄·열 포함 예외).
- [ ] **Step 4: 실패 확인 → 로더 구현 → 통과.** 구현은 `TagSourceJson.cs`의 JObject 순회·`RequireAllowedProperties` 패턴 복제.
- [ ] **Step 5: 동등성 테스트** — 같은 스펙 3종(chill/frozen/독)을 ① 코드 구축 ② JSON 로드 두 경로로 `EffectCatalogBuilder`에 넣어 Build → 동일 시나리오(적용·틱 20회) 후 대상 Hp/Attack/활성 수가 동일함을 단언.
- [ ] **Step 6: 통과 확인 → 커밋** — `✨ BigNum.TryParse와 EffectSpec JSON 로더`.

---

### Task 13: 스냅샷/복원 · 무흔적 왕복 오라클 · 시나리오 해시 · 무할당 스모크

**Files:**
- Modify: `EffectTarget.cs`(+`EffectTargetSnapshot`), `EffectInstance.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Runtime/EffectScenario.cs` (Unity Player 공유 — asmdef `Bun3.Gameplay.Runtime.Tests`에 포함됨)
- Create: `common/tests/Bun3.Gameplay.Tests/EffectDeterminismTests.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj` (EffectScenario.cs 링크 — TagPerformanceFixture 패턴)

**Interfaces:**
- Consumes: Task 1–12.
- Produces:
  - `EffectTargetSnapshot`(sealed class) — Base/Current 등 슬롯 값 배열 + 인스턴스 상태 배열(Id/SpecId/RemainingTicks/PeriodCountdown/Stack/Level/Source/Enabled) + 태그 카운트. `EffectTarget.CreateSnapshot()`, `RestoreSnapshot(EffectTargetSnapshot)`(수정자 재부착 포함 — 복원 후 RebuildDirty로 Current 재구성).
  - `EffectScenario.Run(EffectPipeline, EffectTestKitLike…) → ulong StateHash` — 고정 시드로 적용·틱·디스펠을 섞어 200틱 실행 후 전 대상의 (속성 Base/Current 비트, 인스턴스 필드)를 FNV-1a로 접는 순수 함수. **파일에 골든 해시 상수 없음** — .NET 테스트가 두 번 실행 동일성+스냅샷 복원 동일성을 검증하고, Unity Player 테스트(후속 커밋에서 러너 연결)는 같은 파일로 같은 해시를 재계산해 .NET 골든과 비교한다(골든은 테스트 어셈블리 상수로 1회 고정).
- 테스트:
  1. **무흔적 왕복**: 시드 랜덤 Duration 스펙 30종 생성 → 무작위 apply/제거/틱 500회 후 모든 인스턴스 제거 → 각 속성 Current가 "수정자 없이 같은 Base 이력을 재생한 참조 세트"와 비트 동일.
  2. **스냅샷 복원**: 시나리오 중간(틱 100)에 스냅샷 → 계속 진행해 해시 A → 복원 후 재진행해 해시 B → A == B.
  3. **시나리오 해시 안정**: 같은 시드 2회 실행 해시 동일 + 골든 상수와 일치(최초 실행값을 상수로 고정).
  4. **무할당 스모크**: 정착 상태(스펙 적용 완료, 큐 빈 상태) `pipeline.Tick()` 1000회 전후 `GC.GetAllocatedBytesForCurrentThread` 델타 0 — 기존 `AllocationSmokeTests` 워밍업 패턴 재사용. 주의: `EmitChange`의 배열 성장은 워밍업에서 선성장, 이벤트 버퍼는 매 틱 Clear.
- [ ] **Step 1~5: 각 테스트 작성→실패→구현(스냅샷·해시)→통과→커밋** — `✅ 결정론 오라클·스냅샷·무할당 스모크`.

---

### Task 14: 마무리 — 버전·XML 문서·전체 검증

**Files:**
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`(0.13.0), `common/src/com.bun3.gameplay/package.json`(0.13.0)

- [ ] **Step 1:** 버전 0.13.0으로 범프 (같은 버전 재퍼블리시 금지 규약).
- [ ] **Step 2:** `dotnet build Bun3.sln -c Release` — 경고 0 확인(신규 public XML 문서 누락은 여기서 드러남 — 채운다).
- [ ] **Step 3:** `dotnet test Bun3.sln -c Release -v:minimal` — 전체 통과(기존 306 + 신규).
- [ ] **Step 4:** `& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode -AllEditMode` — Unity EditMode 통과(신규 Runtime 코드 컴파일 확인. `.meta` 생성분은 이 커밋에 포함).
- [ ] **Step 5:** 커밋 — `🔖 Bun3.Gameplay 0.13.0 — Attribute·Effect 심 코어`.

---

## Self-Review 결과 (플랜 확정 전 수행)

- 스펙 §4(Operand)→T1, §5(집계·정책)→T2–T5, §6(스펙·스택)→T7·T9, §7(조건·체인)→T10, §8(시섬)→T6·T8, §9(EffectTarget·페이즈)→T8–T11, §10(카탈로그·데이터)→T7·T12, §11(불변식)→T4(무흔적)·T13(스냅샷·이벤트 id는 `EffectLifecycleEvent.InstanceId`), §13(테스트)→T4·T5·T13. 커버리지 갭 없음.
- 타입 일관성: `Operand`/`MagnitudeDef`/`CompiledEffectSpec`/컨텍스트 시그니처를 Interfaces 블록에 고정 — 태스크 간 참조는 해당 블록 기준.
- 미결 주의점(구현자가 알아야 할 것): ① Operand는 세 kind(Constant/Attribute/SourceAttribute) — 자리별 허용은 Global Constraints 참조, 소스 미해석 = Zero. ② `EffectTestKit`은 T7에서 만들고 T8~13이 확장한다 — 시그니처 변경 시 이전 태스크 테스트도 같이 컴파일되므로 주의.
