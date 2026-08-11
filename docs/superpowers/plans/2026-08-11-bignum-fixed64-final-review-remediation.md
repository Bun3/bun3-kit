# BigNum·Fixed64 Final Review Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 최종 whole-branch 리뷰에서 확인된 BigNum 산술·비교·해시·표시, Fixed64 pin·golden, Gameplay Unity 패키징, TagSet overflow 결함을 하나의 검증 가능한 fix wave로 해결한다.

**Architecture:** BigNum 덧셈은 기존 무할당 unsigned 128-bit 기반을 확장해 부호-크기 exact intermediate를 한 번만 축약하고, 비교와 해시는 산술 연산에서 분리한다. 출력 편의 API는 기존 시그니처를 유지하면서 256자 기본 예산과 명시적 예산 overload를 제공한다. FixedMathSharp는 NuGet/UPM 모두 7.0.0으로 고정하고, 동일한 비자명 literal golden을 .NET과 Unity가 실행하며 Gameplay 패키지도 Unity 샘플에 실제 import한다.

**Tech Stack:** C# 9, netstandard2.1, NUnit 4, Unity Test Framework 1.6, Unity 6000.3.14f1, FixedMathSharp.Lean 7.0.0, UPM Git dependency

## Global Constraints

- 모든 런타임 코드는 netstandard2.1과 C# 9에서 컴파일되어야 한다.
- 모든 새 public API와 예외 계약에는 정확한 한국어 XML 문서가 있어야 한다.
- 빌드 결과는 경고 0, 오류 0이어야 한다.
- BigNum 가수 범위는 `[-long.MaxValue, long.MaxValue]`이며 `long.MinValue`는 public 입력에서 `ArgumentOutOfRangeException`이다.
- BigNum 산술·비교·해시는 프로세스, 문화권, .NET/Unity 런타임에 관계없이 결정론적이어야 한다.
- BigNum 산술·formatting hot path에는 새 heap allocation을 추가하지 않는다. 문자열을 반환하는 편의 API만 명시적으로 할당한다.
- 기존 `ToDisplayString(BigNumFormat? format = null)` 바이너리 시그니처를 유지하고 기본 출력 예산은 정확히 256자다.
- FixedMathSharp NuGet dependency는 exact `[7.0.0]`; UPM은 v7.0.0/revision `168b6f4f2a7dcf4164aab93db81754bae737de40`이다.
- 결정론 상태 저장·전송은 BigNum 정규 가수/지수 또는 Fixed64 signed Raw `long` little-endian만 사용한다.
- `Bun3.FixedFloat` wrapper를 추가하지 않는다.
- Gameplay 버전은 .NET/UPM 모두 `0.3.0`, Common 버전은 .NET/UPM 모두 `0.4.0`, 두 Unity 최소 버전은 `2022.3`이다.
- 커밋은 gitmoji 형식과 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` trailer를 사용한다.

## File Structure

- Modify `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs`: exact 128-bit 덧셈, 직접 비교, 고정 FNV-1a hash, invariant debug 문자열, public 예외 XML.
- Modify `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.Format.cs`: 256자 기본 예산과 명시적 `maxLength` overload.
- Modify `common/src/com.bun3.gameplay/Runtime/Tags/TagSet.cs`: checked count arithmetic.
- Modify `common/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs`: 산술·비교·hash 회귀.
- Modify `common/tests/Bun3.Gameplay.Tests/BigNumFormatTests.cs`: 출력 예산 회귀.
- Modify `common/tests/Bun3.Gameplay.Tests/TagSetTests.cs`: count overflow 회귀.
- Modify `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`: 0.3.0 및 Unity test source 제외.
- Modify `common/src/com.bun3.gameplay/package.json`: 0.3.0, Unity 2022.3.
- Create `common/src/com.bun3.gameplay/Tests/Editor/GameplayUnitySmokeTests.cs`: Unity에서 BigNum 핵심 계약 smoke.
- Create `common/src/com.bun3.gameplay/Tests/Editor/Bun3.Gameplay.Tests.asmdef`: Gameplay Unity EditMode test assembly.
- Create Unity-generated `.meta` files for every existing/new gameplay package folder and asset that lacks one.
- Modify `common/src/com.bun3.common/Bun3.Common.csproj`: 0.4.0 및 exact FixedMathSharp `[7.0.0]`.
- Modify `common/src/com.bun3.common/package.json`: 0.4.0 유지 dependency 7.0.0.
- Modify `common/src/com.bun3.common/Tests/Editor/Fixed64ConformanceTests.cs`: 비자명 signed arithmetic/math/vector/tick/serialization literal goldens.
- Modify `unity/Packages/manifest.json`: local Gameplay dependency와 testable 추가.
- Modify `unity/Packages/packages-lock.json`: Unity resolver가 생성한 Gameplay/Common/FixedMathSharp 정합 lock.

---

### Task 1: 최종 리뷰 단일 Fix Wave

**Files:** 위 File Structure의 모든 파일. 이 계획은 final-review 규칙상 하나의 구현·검증·리뷰 단위이며 중간 커밋을 만들지 않는다.

**Interfaces:**

- Consumes: 현재 `BigNum`, `Int128Math`, `BigNumFormat`, `TagSet`, `FixedMathSharp.Fixed64` public 계약.
- Produces: `BigNum.ToDisplayString(BigNumFormat? format, int maxLength)` overload.
- Preserves: `BigNum.ToDisplayString(BigNumFormat? format = null)`, 모든 기존 BigNum/TagSet public signature, Fixed64 직접 사용.

- [ ] **Step 1: 현재 결함을 focused RED 테스트로 재현한다**

`BigNumBasicTests.cs`에 각각 독립된 테스트를 추가한다. 기대값은 구현 helper로 계산하지 말고 literal/BigInteger oracle을 사용한다.

```csharp
[Test]
public void Addition_reduces_valid_negative_long_min_intermediate()
{
    var actual = (BigNum)(-long.MaxValue) + (BigNum)(-1);
    Assert.That(actual, Is.EqualTo(BigNum.FromParts(-92_233_720_368_547_758L, 2)));
}

[Test]
public void Addition_reduces_exact_sum_once_after_carry()
{
    var actual = (BigNum)long.MaxValue + (BigNum)long.MaxValue;
    Assert.That(actual, Is.EqualTo(BigNum.FromParts(1_844_674_407_370_955_161L, 1)));
}

[Test]
public void Addition_preserves_exact_near_cancellation()
{
    var actual = BigNum.FromParts(-922_337_203_685_477_581L, -2)
                 + BigNum.FromParts(3, -3);
    Assert.That(actual, Is.EqualTo(BigNum.FromParts(-long.MaxValue, -3)));
}

[Test]
public void Comparison_is_total_at_extrema_and_opposite_signs()
{
    Assert.That(BigNum.MaxValue.CompareTo(BigNum.MinValue), Is.GreaterThan(0));
    Assert.That(BigNum.MinValue.CompareTo(BigNum.MaxValue), Is.LessThan(0));
    Assert.That(BigNum.MaxValue > BigNum.MinValue, Is.True);
    Assert.That(((BigNum)(-1)).CompareTo((BigNum)long.MaxValue), Is.LessThan(0));
}

[Test]
public void Hash_code_uses_fixed_fnv1a_golden()
{
    Assert.That(BigNum.FromParts(12_345, 6).GetHashCode(), Is.EqualTo(930_490_798));
    Assert.That(BigNum.MinValue.GetHashCode(), Is.EqualTo(1_520_456_044));
}
```

기존 `long.MaxValue + long.MaxValue`의 잘못된 기대값 `184467440737095516e2`는 새 exact-once 기대값으로 교체한다. 추가로 table-driven BigInteger oracle을 사용해 같은 부호 carry, 반대 부호 상쇄, exponent-gap 18/19 경계, 양·음 대칭을 검증한다. oracle은 `(mantissa * 10^exponent)`을 공통 지수로 맞춘 뒤 결과를 19자리/`long.MaxValue`까지 0 방향으로 한 번 축약해야 하며 production helper를 호출하지 않는다.

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumBasicTests"
```

Expected RED: 위 addition/CompareTo/hash 테스트가 현재 잘못된 값, `ArgumentOutOfRangeException`, `BigNumOverflowException`, 비고정 hash 중 하나로 실패한다.

- [ ] **Step 2: exact signed-magnitude 128-bit 덧셈을 구현한다**

`BigNum.cs`에 private helper를 추가한다. 이름과 역할은 다음을 고정한다.

```csharp
private static void Add128(
    ulong leftHi, ulong leftLo, ulong rightHi, ulong rightLo,
    out ulong resultHi, out ulong resultLo);

private static int Compare128(
    ulong leftHi, ulong leftLo, ulong rightHi, ulong rightLo);

private static void Subtract128(
    ulong largerHi, ulong largerLo, ulong smallerHi, ulong smallerLo,
    out ulong resultHi, out ulong resultLo);

private static void ScaleMantissa128(
    ulong mantissa, int decimalShift, out ulong hi, out ulong lo);
```

구현 규칙:

1. 두 값의 `CountDigits64(absMantissa) + Exponent - 1` magnitude를 계산한다.
2. magnitude 차이가 `ScaleDigits`보다 크면 작은 값은 결과 19자리에 영향을 줄 수 없으므로 magnitude가 큰 operand를 반환한다.
3. 그 외에는 더 작은 exponent를 공통 exponent로 선택하고 `ScaleMantissa128`로 양쪽 절댓값을 정확히 정렬한다. 이 경로의 magnitude 차이는 최대 18이므로 128-bit 범위 안이다.
4. 같은 부호는 `Add128`; 다른 부호는 `Compare128` 후 `Subtract128`를 호출한다.
5. 결과가 0이면 `Zero`; 아니면 `ReduceToLong(resultHi, resultLo, ref exponent)` 후 부호를 붙이고 `Canonicalize`한다.
6. 어떤 경로에서도 signed `long.MinValue`를 중간 가수로 만들지 않는다.

`operator -`는 기존 `a + (-b)`를 유지할 수 있다. 단항 부호 반전은 public 범위가 대칭이므로 안전하다.

- [ ] **Step 3: 직접 비교와 결정론 hash를 구현한다**

`CompareTo`는 다음 순서로 구현한다.

```csharp
public int CompareTo(BigNum other)
{
    var signComparison = Sign.CompareTo(other.Sign);
    if (signComparison != 0 || IsZero)
    {
        return signComparison;
    }

    // 같은 부호: decimal magnitude 비교 후, 같으면 128-bit 정렬 가수 비교.
    // 음수는 magnitude comparison 결과를 반전한다.
}
```

같은 magnitude에서 exponent 차이는 mantissa 자릿수 차이 범위이므로 128-bit 정렬 비교를 사용한다. subtraction이나 BigNum 산술 operator를 호출하지 않는다.

`GetHashCode`는 정확히 다음 FNV-1a 순서를 사용한다.

```csharp
public override int GetHashCode()
{
    unchecked
    {
        var hash = (int)2_166_136_261u;
        hash = (hash ^ (int)Mantissa) * 16_777_619;
        hash = (hash ^ (int)(Mantissa >> 32)) * 16_777_619;
        hash = (hash ^ Exponent) * 16_777_619;
        return hash;
    }
}
```

`ToString()`의 Mantissa/Exponent 변환은 `CultureInfo.InvariantCulture`를 사용한다. `using System.Globalization;`을 추가한다. `long` 명시적 변환과 `FromParts` XML에 `long.MinValue`의 `ArgumentOutOfRangeException`을 한국어 `<exception>`으로 문서화한다.

- [ ] **Step 4: BigNum RED를 GREEN으로 만든다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumBasicTests"
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumMulDivTests"
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~AllocationSmokeTests"
```

Expected: 모두 PASS, allocation 결과 0 유지.

- [ ] **Step 5: 256자 기본 출력 예산 RED 테스트를 작성한다**

`BigNumFormatTests.cs`에 추가한다.

```csharp
[Test]
public void ToDisplayString_default_budget_rejects_oversized_top_unit_output()
{
    Assert.That(
        () => BigNum.MaxValue.ToDisplayString(),
        Throws.TypeOf<InvalidOperationException>());
}

[Test]
public void ToDisplayString_explicit_budget_allows_larger_output()
{
    var value = BigNum.FromParts(1, 300);
    var text = value.ToDisplayString(BigNumFormat.Base, 512);
    Assert.That(text.Length, Is.GreaterThan(256));
}

[TestCase(0)]
[TestCase(-1)]
public void ToDisplayString_rejects_nonpositive_budget(int maxLength)
{
    Assert.That(
        () => BigNum.One.ToDisplayString(BigNumFormat.Base, maxLength),
        Throws.TypeOf<ArgumentOutOfRangeException>());
}

[Test]
public void ToDisplayString_scientific_formats_extrema_within_default_budget()
{
    var scientific = new BigNumFormat(
        new[] { "", "K", "M", "B", "T" },
        overflowStyle: BigNumOverflowStyle.Scientific);
    Assert.That(BigNum.MaxValue.ToDisplayString(scientific).Length, Is.LessThanOrEqualTo(256));
}
```

Run focused test and confirm the first call would otherwise grow without bound, the overload does not exist, and nonpositive validation is absent.

- [ ] **Step 6: 출력 예산 overload를 구현한다**

`BigNum.Format.cs`의 기존 시그니처를 그대로 둔다.

```csharp
public string ToDisplayString(BigNumFormat? format = null) =>
    ToDisplayString(format, 256);

public string ToDisplayString(BigNumFormat? format, int maxLength)
{
    if (maxLength <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(maxLength));
    }

    Span<char> initial = stackalloc char[128];
    var first = maxLength < initial.Length ? initial.Slice(0, maxLength) : initial;
    if (TryFormat(first, out var written, format))
    {
        return new string(first.Slice(0, written));
    }

    if (maxLength <= initial.Length)
    {
        throw CreateDisplayBudgetException(maxLength);
    }

    var buffer = new char[maxLength];
    if (TryFormat(buffer, out written, format))
    {
        return new string(buffer, 0, written);
    }

    throw CreateDisplayBudgetException(maxLength);
}
```

`CreateDisplayBudgetException`은 private helper이며 메시지에 `Scientific`, 더 큰 `maxLength`, `TryFormat`을 명시한다. 두 public overload의 한국어 XML에 allocation, 기본 256자, parameter, return, `ArgumentOutOfRangeException`, `InvalidOperationException`을 문서화한다.

Run BigNumFormat focused tests; Expected GREEN.

- [ ] **Step 7: TagSet checked overflow를 TDD로 구현한다**

`TagSetTests.cs`에 추가한다.

```csharp
[Test]
public void Add_fails_fast_before_exact_count_wraps_negative()
{
    _set.Add(_hasted, int.MaxValue);
    Assert.That(() => _set.Add(_hasted), Throws.TypeOf<OverflowException>());
    Assert.That(_set.ExactCount(_hasted), Is.EqualTo(int.MaxValue));
}

[Test]
public void Hierarchical_count_fails_fast_before_sum_wraps_negative()
{
    _set.Add(_dead, int.MaxValue);
    _set.Add(_ghost);
    Assert.That(() => _set.Count(_state), Throws.TypeOf<OverflowException>());
}
```

RED를 확인한 뒤 `current + count`와 hierarchical `total + pair.Value`만 `checked(...)`로 감싼다. 실패한 `Add`가 dictionary 값을 바꾸지 않는 순서를 유지한다. public `Add`와 `Count` XML에 `OverflowException`을 문서화한다. focused TagSet tests를 GREEN으로 만든다.

- [ ] **Step 8: Fixed64 공유 golden을 먼저 확장해 현재 누락을 확인한다**

`Fixed64ConformanceTests.cs`의 namespace는 C# 9 block 형태를 유지한다. 아래 literal은 테스트 대상 내부 helper로 계산하지 않는다. 주석에 “pinned upstream 7.0.0 reference harness + Q32.32 hand check” provenance를 남긴다.

필수 raw literal:

```text
Sin(1)                    3_614_090_365
Cos(1)                    2_320_580_735
Sqrt(2)                   6_074_001_000
Normalized(3,4).X         2_576_980_378
Normalized(3,4).Y         3_435_973_837
-1.5 + 2.25               3_221_225_472
-1.5 - 2.25             -16_106_127_360
-1.5 * 2.25             -14_495_514_624
-1.5 / 2.25              -2_863_311_531
Raw(-1) * Half                         0
Raw(-3) * Half                        -2
Raw(-1) / Two                          0
Raw(-3) / Two                         -2
MaxValue + One       9_223_372_036_854_775_807
MinValue - One      -9_223_372_036_854_775_808
MaxValue * Half      4_611_686_018_427_387_904
MinValue * Half     -4_611_686_018_427_387_904
MaxValue / Two       4_611_686_018_427_387_904
MinValue / Two      -4_611_686_018_427_387_904
FromDouble(1/60)                71_582_788
FromDouble(6.25)            26_843_545_600
step                         447_392_425
position after 600 ticks    268_435_455_000
```

signed serialization input `-4_294_967_297L`의 little-endian literal은 다음과 같다.

```csharp
new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF }
```

기존 trivial scalar/vector test와 positive-only endian test를 위 비자명 사례로 교체하거나 확장한다. add/sub saturation, negative midpoint, intermediate multiply/divide edge를 독립 test method로 분리한다.

Run:

```powershell
dotnet test common/tests/Bun3.Common.Tests --nologo --filter "FullyQualifiedName~Fixed64"
```

Expected: 새 tests가 pinned 7.0.0에서 PASS해야 한다. 이 단계는 library 구현 변경이 아니라 기존 검증 공백을 닫는 단계이므로, mutation check로 각 literal의 한 비트를 바꾸면 해당 test가 실패함을 확인하고 원복한다.

- [ ] **Step 9: package exact pin과 새 버전을 적용한다**

정확히 다음 메타데이터를 적용한다.

```xml
<!-- common/src/com.bun3.common/Bun3.Common.csproj -->
<Version>0.4.0</Version>
<PackageReference Include="FixedMathSharp.Lean" Version="[7.0.0]" />
```

```json
// common/src/com.bun3.common/package.json
"version": "0.4.0",
"unity": "2022.3",
"com.mrdav30.fixedmathsharp.lean": "7.0.0"
```

```xml
<!-- common/src/com.bun3.gameplay/Bun3.Gameplay.csproj -->
<Version>0.3.0</Version>
<Compile Remove="Tests/**/*.cs" />
```

```json
// common/src/com.bun3.gameplay/package.json
"version": "0.3.0",
"unity": "2022.3"
```

Run `dotnet restore`, `dotnet pack` 두 패키지. 생성된 Common nupkg를 임시 디렉터리에 unzip하여 `.nuspec` dependency version이 `[7.0.0]`인지 확인한다. generated artifacts는 커밋하지 않는다.

- [ ] **Step 10: Gameplay UPM import와 Unity smoke를 추가한다**

`unity/Packages/manifest.json` dependencies에 다음을 추가한다.

```json
"com.bun3.gameplay": "file:../../common/src/com.bun3.gameplay"
```

`testables`는 다음 두 항목을 가진다.

```json
"testables": [
  "com.bun3.common",
  "com.bun3.gameplay"
]
```

Gameplay package에 `Tests/Editor/Bun3.Gameplay.Tests.asmdef`를 추가한다.

```json
{
  "name": "Bun3.Gameplay.Unity.Tests",
  "rootNamespace": "Bun3.Gameplay.Unity.Tests",
  "references": [
    "Bun3.Gameplay",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`GameplayUnitySmokeTests.cs`는 block namespace로 작성하고 다음 literal 계약을 실행한다.

```csharp
Assert.That((BigNum)long.MaxValue + (BigNum)long.MaxValue,
    Is.EqualTo(BigNum.FromParts(1_844_674_407_370_955_161L, 1)));
Assert.That(BigNum.MaxValue > BigNum.MinValue, Is.True);
Assert.That(BigNum.FromParts(12_345, 6).GetHashCode(), Is.EqualTo(930_490_798));
Assert.That(
    BigNum.MaxValue.ToDisplayString(scientific).Length,
    Is.LessThanOrEqualTo(256));
```

Unity Editor 6000.3.14f1을 열어 resolver/import를 수행하고 Unity가 gameplay package의 모든 누락 `.meta` 및 `packages-lock.json`을 생성/갱신하게 한다. `.meta` GUID를 손으로 만들지 않는다. `ProjectSettings.asset` 등 계획 밖 serialization 변경은 diff를 확인하여 기존 값과 동일하게 복구한다.

- [ ] **Step 11: 전체 검증을 fresh 상태에서 수행한다**

Run:

```powershell
dotnet clean Bun3.sln --nologo
dotnet build Bun3.sln --nologo
dotnet test Bun3.sln --no-build --nologo
dotnet pack common/src/com.bun3.gameplay/Bun3.Gameplay.csproj -c Release --nologo
dotnet pack common/src/com.bun3.common/Bun3.Common.csproj -c Release --nologo
git diff --check
```

Expected: build 경고/오류 0, Gameplay/Common/Server 전체 PASS, package versions 0.3.0/0.4.0, Common nuspec exact `[7.0.0]`.

Unity EditMode는 이 환경에서 `-quit`가 test runner보다 먼저 종료되므로 `-quit` 없이 실행한다. 결과 XML과 log의 `Run completed`를 모두 확인하고 Editor process가 정상 종료했는지 확인한다.

```powershell
$resultPath = Join-Path $env:TEMP 'bun3-final-remediation-editmode.xml'
$logPath = Join-Path $env:TEMP 'bun3-final-remediation-editmode.log'
& 'E:\Unitys\6000.3.14f1\Editor\Unity.exe' -batchmode `
    -projectPath (Resolve-Path 'unity') `
    -runTests -testPlatform EditMode `
    -testResults $resultPath -logFile $logPath
```

Expected: Fixed64 expanded conformance, Gameplay smoke, 기존 settings tests를 포함한 모든 EditMode test PASS. 마지막에 `git status --short`로 계획된 파일 외 변경이 없음을 확인한다. Unity가 tracked project setting을 바꿨다면 원인을 확인하고 복구한 뒤 .NET Step 11 검증을 다시 실행한다.

- [ ] **Step 12: self-review하고 단일 fix commit을 만든다**

Self-review checklist:

- 각 Important 7건과 Minor 2건에 대응하는 test/코드/메타데이터가 있는가.
- BigNum addition/CompareTo/HashCode/TryFormat hot path에 allocation이 추가되지 않았는가.
- 기대값이 production helper를 복제하지 않고 literal/BigInteger oracle인가.
- public XML이 한국어이며 실제 예외와 일치하는가.
- Gameplay package의 모든 asset/folder meta가 Unity-generated이며 smoke가 실제 assembly를 참조하는가.
- Common exact NuGet pin과 UPM tag/revision이 양쪽에서 7.0.0인가.
- `git diff --check`와 working tree 범위가 깨끗한가.

Commit:

```powershell
git add -- common/src/com.bun3.gameplay common/tests/Bun3.Gameplay.Tests `
    common/src/com.bun3.common common/tests/Bun3.Common.Tests `
    unity/Packages/manifest.json unity/Packages/packages-lock.json
git commit -m "🐛 BigNum·Fixed64 결정론 경계 보완" `
    -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

최종 보고서에는 각 finding별 root cause, RED/GREEN 명령·핵심 출력, 변경 파일, .NET/Unity/package 검증, 남은 관찰사항을 기록한다.
