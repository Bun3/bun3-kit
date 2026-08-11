# BigNum and Fixed64 Determinism Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** BigNum의 경계·변환·포맷 계약을 보강하고, 서버와 Unity가 동일한 FixedMathSharp Lean 7.0.0 Q32.32 구현과 골든 벡터를 사용하게 한다.

**Architecture:** BigNum은 `Bun3.Gameplay.Numerics`의 독립 십진 대수로 유지하고 지원 가수를 대칭 범위로 제한한다. 공간 수치는 래퍼 없이 `FixedMathSharp.Fixed64`를 사용하며 `Bun3.Common`이 NuGet/UPM 의존성을 제공한다. Fixed64 적합성 테스트 소스 한 개를 .NET NUnit과 Unity EditMode가 함께 컴파일해 Raw 결과의 교차 런타임 일치를 검증한다.

**Tech Stack:** C# 9, netstandard2.1, NUnit 4(.NET), Unity Test Framework 1.6(EditMode), Unity 6000.3.14f1, FixedMathSharp.Lean 7.0.0, UPM Git dependency

## Global Constraints

- 모든 라이브러리 프로젝트는 `netstandard2.1`, C# 9를 유지한다.
- 모든 새 public 멤버에는 한국어 XML 문서를 작성하고 빌드 경고를 0으로 유지한다.
- BigNum 지원 가수는 `[-long.MaxValue, long.MaxValue]`; `long.MinValue`는 `ArgumentOutOfRangeException`이다.
- BigNum float 변환은 유효 7자리, double 변환은 유효 16자리를 0 방향으로 절사한다.
- 결정론 틱 내부에서 float/double 변환을 사용하지 않고 BigNum 두 필드와 Fixed64 Raw `long`을 정본으로 삼는다.
- FixedMathSharp는 서버와 Unity 모두 Lean `7.0.0`을 정확히 고정하며 업그레이드는 결정론 호환성 변경으로 취급한다.
- `Bun3.FixedFloat` 래퍼를 만들지 않고 `FixedMathSharp.Fixed64`와 패키지 벡터 타입을 직접 사용한다.
- `com.bun3.common`의 Unity 최소 버전은 FixedMathSharp 요구사항과 같은 `2022.3`이다.
- 핫패스 산술·포맷은 힙 할당 0을 유지한다.
- 내용이 바뀌는 패키지는 같은 버전 접미사 재사용 없이 버전을 올린다.
- 커밋은 gitmoji 제목과 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` 트레일러를 사용한다.

---

## File Structure

- `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs`: 대칭 가수 범위, 공개 극값, float/double 경계 변환.
- `common/src/com.bun3.gameplay/Runtime/Numerics/BigNumFormat.cs`: 단위 테이블 검증·복제·읽기 전용 공개 뷰.
- `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.Format.cs`: 포맷 핫패스가 내부 단위 배열을 읽는 호출부.
- `common/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs`: BigNum 경계 및 부동소수 변환 회귀 테스트.
- `common/tests/Bun3.Gameplay.Tests/BigNumFormatTests.cs`: 포맷 불변성과 null 검증 회귀 테스트.
- `common/src/com.bun3.common/Tests/Editor/Fixed64ConformanceTests.cs`: .NET과 Unity가 공유하는 Fixed64 Raw 골든 벡터.
- `common/src/com.bun3.common/Tests/Editor/Bun3.Common.FixedMathSharp.Tests.asmdef`: 공유 테스트의 Unity EditMode 어셈블리 경계.
- `common/tests/Bun3.Common.Tests/Fixed64AllocationTests.cs`: .NET에서 Fixed64 틱 연산 할당 0 검증.
- `common/src/com.bun3.common/Bun3.Common.csproj`: Lean NuGet 전이 의존성, 테스트 소스 제외, 패키지 버전.
- `common/tests/Bun3.Common.Tests/Bun3.Common.Tests.csproj`: 공유 적합성 테스트 소스 링크.
- `common/src/com.bun3.common/package.json`: Lean UPM 의존성과 Unity/패키지 버전.
- `unity/Packages/manifest.json`: v7.0.0 Git URL 해석 경로와 테스트 패키지 활성화.
- `unity/Packages/packages-lock.json`: Unity가 생성한 정확한 커밋 잠금.

---

### Task 1: BigNum 대칭 범위와 공개 극값

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs`

**Interfaces:**
- Consumes: `BigNum.FromParts(long mantissa, int exponent)`, 암시적 `long -> BigNum`, `BigNum.MaxExponent`.
- Produces: `public static readonly BigNum MinValue`, `public static readonly BigNum MaxValue`, 대칭 정규 가수 불변식.

- [ ] **Step 1: long.MinValue 손실과 극값 대칭성을 재현하는 테스트를 작성한다**

`BigNumBasicTests`에 다음 테스트를 추가한다.

```csharp
[Test]
public void Long_min_value_is_rejected_instead_of_truncated()
{
    Assert.That(() => _ = (BigNum)long.MinValue,
        Throws.TypeOf<ArgumentOutOfRangeException>());
    Assert.That(() => _ = BigNum.FromParts(long.MinValue, 17),
        Throws.TypeOf<ArgumentOutOfRangeException>());
}

[Test]
public void Published_extrema_are_exact_and_symmetric()
{
    Assert.That(BigNum.MaxValue.Mantissa, Is.EqualTo(long.MaxValue));
    Assert.That(BigNum.MaxValue.Exponent, Is.EqualTo(BigNum.MaxExponent));
    Assert.That(BigNum.MinValue.Mantissa, Is.EqualTo(-long.MaxValue));
    Assert.That(BigNum.MinValue.Exponent, Is.EqualTo(BigNum.MaxExponent));
    Assert.That(-BigNum.MaxValue, Is.EqualTo(BigNum.MinValue));
    Assert.That(-BigNum.MinValue, Is.EqualTo(BigNum.MaxValue));
}
```

- [ ] **Step 2: 테스트가 기존 손실 동작과 누락된 API 때문에 실패하는지 확인한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumBasicTests"
```

Expected: `MinValue`/`MaxValue`가 아직 없어 테스트 프로젝트가 컴파일 실패한다. 두 필드를 먼저 선언만 하면 `long.MinValue` 예외 기대가 실패해 기존 손실 동작도 확인된다.

- [ ] **Step 3: 대칭 극값과 fail-fast 정규화를 구현한다**

`Zero`, `One` 옆에 다음 public 필드를 추가한다.

```csharp
/// <summary>표현 가능한 최소값. <c>-long.MaxValue × 10^MaxExponent</c>.</summary>
public static readonly BigNum MinValue = new BigNum(-long.MaxValue, MaxExponent);

/// <summary>표현 가능한 최대값. <c>long.MaxValue × 10^MaxExponent</c>.</summary>
public static readonly BigNum MaxValue = new BigNum(long.MaxValue, MaxExponent);
```

`Canonicalize`의 기존 `long.MinValue` 절사 블록을 다음 코드로 교체한다.

```csharp
if (mantissa == long.MinValue)
{
    throw new ArgumentOutOfRangeException(
        nameof(mantissa), mantissa,
        "BigNum 가수는 -long.MaxValue 이상이어야 한다.");
}
```

- [ ] **Step 4: BigNum 전체 테스트로 산술 회귀가 없는지 확인한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo
```

Expected: 모든 BigNum·Tag 테스트 PASS, 경고 0.

- [ ] **Step 5: 경계값 변경을 커밋한다**

```powershell
git add common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs common/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs
git commit -m "🐛 BigNum 가수 범위를 대칭화하고 극값 공개" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: float 7자리와 double 16자리 변환 분리

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs`

**Interfaces:**
- Consumes: Task 1의 대칭 가수 불변식과 `Canonicalize(long, long)`.
- Produces: 명시적 `float -> BigNum` 7자리 절사, 기존 명시적 `double -> BigNum` 16자리 절사.

- [ ] **Step 1: float와 double의 서로 다른 유효 자릿수 계약을 테스트한다**

`BigNumBasicTests`에 다음 테스트를 추가하고 기존 `Float_double_convert_explicitly`는 NaN/무한대 기본 사례 검증으로 유지한다.

```csharp
[Test]
public void Float_conversion_truncates_to_seven_significant_digits()
{
    Assert.That((BigNum)12_345_678f,
        Is.EqualTo(BigNum.FromParts(1_234_567, 1)));
    Assert.That((BigNum)(-12_345_678f),
        Is.EqualTo(BigNum.FromParts(-1_234_567, 1)));
}

[Test]
public void Double_conversion_preserves_sixteen_significant_digits()
{
    const double exactSixteenDigitInteger = 1_234_567_890_123_456d;
    Assert.That((BigNum)exactSixteenDigitInteger,
        Is.EqualTo(BigNum.FromParts(1_234_567_890_123_456L, 0)));

    Assert.That((BigNum)(double)12_345_678f,
        Is.EqualTo((BigNum)12_345_678L));
}

[Test]
public void Float_non_finite_values_are_rejected()
{
    Assert.That(() => _ = (BigNum)float.NaN, Throws.ArgumentException);
    Assert.That(() => _ = (BigNum)float.PositiveInfinity, Throws.ArgumentException);
    Assert.That(() => _ = (BigNum)float.NegativeInfinity, Throws.ArgumentException);
}
```

- [ ] **Step 2: float 테스트가 현재 double 위임의 8자리 보존 때문에 실패하는지 확인한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumBasicTests"
```

Expected: `Float_conversion_truncates_to_seven_significant_digits` FAIL; 현재 결과는 `12_345_678`을 보존한다.

- [ ] **Step 3: float 전용 정규화 경로를 구현한다**

double 상수 옆에 float 상수를 추가한다.

```csharp
private const float FloatNormalizeLow = 1e6f;
private const float FloatNormalizeHigh = 1e7f;
```

현재 float 연산자를 다음 코드로 교체한다.

```csharp
/// <summary>float을 절사 변환한다(유효 7자리) — 명시적이며 결정론 경계에서만 사용한다.
/// NaN과 무한대는 던진다.</summary>
public static explicit operator BigNum(float value)
{
    if (float.IsNaN(value) || float.IsInfinity(value))
    {
        throw new ArgumentException("NaN/무한대는 BigNum으로 변환할 수 없다.", nameof(value));
    }

    if (value == 0f)
    {
        return Zero;
    }

    var negative = value < 0f;
    var abs = negative ? -value : value;
    var exponent = 0L;

    while (abs >= FloatNormalizeHigh)
    {
        abs /= 10f;
        exponent++;
    }

    while (abs < FloatNormalizeLow)
    {
        abs *= 10f;
        exponent--;
    }

    var mantissa = (long)abs;
    return Canonicalize(negative ? -mantissa : mantissa, exponent);
}
```

- [ ] **Step 4: 변환 테스트와 전체 Gameplay 테스트를 실행한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumBasicTests"
dotnet test common/tests/Bun3.Gameplay.Tests --nologo
```

Expected: 두 명령 모두 PASS, 경고 0.

- [ ] **Step 5: 변환 정책을 커밋한다**

```powershell
git add common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs common/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs
git commit -m "🐛 BigNum float 변환을 유효 7자리로 제한" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: BigNumFormat 완전 불변화와 Gameplay 버전 갱신

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Numerics/BigNumFormat.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.Format.cs`
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Modify: `common/src/com.bun3.gameplay/package.json`
- Test: `common/tests/Bun3.Gameplay.Tests/BigNumFormatTests.cs`

**Interfaces:**
- Consumes: 기존 `BigNumFormat` 생성자와 `TryFormat` 호출 계약.
- Produces: `IReadOnlyList<string> Units`, internal `string GetUnit(int index)`, `Bun3.Gameplay` 0.2.0.

- [ ] **Step 1: 단위 뷰 변이와 첫 null 원소를 재현하는 테스트를 작성한다**

`BigNumFormatTests.cs`에 다음 using과 테스트를 추가한다.

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
```

```csharp
[Test]
public void Unit_table_cannot_change_after_construction()
{
    var source = new[] { "", "K" };
    var format = new BigNumFormat(3, source);

    source[1] = "X";
    Assert.That(format.Units[1], Is.EqualTo("K"));
    Assert.That(format.Units, Is.InstanceOf<ReadOnlyCollection<string>>());
    Assert.That(() => ((IList<string>)format.Units)[1] = "X",
        Throws.TypeOf<NotSupportedException>());
    Assert.That(Format((BigNum)1_500, format), Is.EqualTo("1.5K"));
}

[Test]
public void Null_unit_entries_report_argument_errors()
{
    Assert.That(() => _ = new BigNumFormat(3, null!),
        Throws.TypeOf<ArgumentNullException>());
    Assert.That(() => _ = new BigNumFormat(3, new string[] { null!, "K" }),
        Throws.TypeOf<ArgumentException>());
    Assert.That(() => _ = new BigNumFormat(3, new string[] { "", null! }),
        Throws.TypeOf<ArgumentException>());
}
```

- [ ] **Step 2: 기존 공개 배열 변이와 units[0] NRE를 확인한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumFormatTests"
```

Expected: 읽기 전용 컬렉션 타입 단언 FAIL, `units[0] == null` 사례는 예상한 `ArgumentException` 대신 `NullReferenceException`.

- [ ] **Step 3: 복제 배열과 읽기 전용 뷰를 분리한다**

`BigNumFormat.cs`에 컬렉션 using을 추가한다.

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
```

`Units`와 내부 저장소를 다음과 같이 정의한다.

```csharp
private readonly string[] _units;

/// <summary>단위 문자 테이블의 읽기 전용 뷰. [0]은 단위 없음(빈 문자열)이다.</summary>
public IReadOnlyList<string> Units { get; }

internal string GetUnit(int index) => _units[index];
```

생성자 검증과 대입을 다음 순서로 바꾼다.

```csharp
if (units == null)
{
    throw new ArgumentNullException(nameof(units));
}

if (units.Length == 0)
{
    throw new ArgumentException("Units는 한 개 이상의 원소가 필요하다.", nameof(units));
}

for (var i = 0; i < units.Length; i++)
{
    if (units[i] == null)
    {
        throw new ArgumentException("Units에 null 원소가 있다.", nameof(units));
    }
}

if (units[0].Length != 0)
{
    throw new ArgumentException("Units[0]은 빈 문자열이어야 한다.", nameof(units));
}
```

나머지 범위 검증 후 대입은 다음 코드로 고정한다.

```csharp
_units = (string[])units.Clone();
Units = new ReadOnlyCollection<string>(_units);
```

`BigNum.Format.cs`의 포맷 핫패스는 공개 인터페이스 대신 내부 배열 접근자를 사용한다.

```csharp
&& TryAppendString(destination, ref charsWritten, format.GetUnit(index));
```

`Base`와 `Korean` XML 문서의 “상한 초과는 지수 표기”를 실제 기본값인 “상한 초과는 최상위 단위 유지”로 고친다.

- [ ] **Step 4: Gameplay 패키지 버전을 0.2.0으로 올린다**

`Bun3.Gameplay.csproj`과 `package.json`의 버전을 모두 `0.2.0`으로 변경한다.

```xml
<Version>0.2.0</Version>
```

```json
"version": "0.2.0"
```

- [ ] **Step 5: 포맷과 전체 Gameplay 회귀를 검증한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumFormatTests"
dotnet test common/tests/Bun3.Gameplay.Tests --nologo
dotnet build common/src/com.bun3.gameplay/Bun3.Gameplay.csproj --nologo
```

Expected: 모든 명령 PASS, 할당 스모크 포함 경고 0.

- [ ] **Step 6: 포맷 불변성과 Gameplay 버전을 커밋한다**

```powershell
git add common/src/com.bun3.gameplay/Runtime/Numerics/BigNumFormat.cs common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.Format.cs common/src/com.bun3.gameplay/Bun3.Gameplay.csproj common/src/com.bun3.gameplay/package.json common/tests/Bun3.Gameplay.Tests/BigNumFormatTests.cs
git commit -m "🔒 BigNumFormat 단위 테이블을 완전 불변화" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: FixedMathSharp Lean 7.0.0 이중 런타임 통합

**Files:**
- Modify: `common/src/com.bun3.common/Bun3.Common.csproj`
- Modify: `common/src/com.bun3.common/package.json`
- Modify: `common/tests/Bun3.Common.Tests/Bun3.Common.Tests.csproj`
- Create: `common/src/com.bun3.common/Tests/Editor/Fixed64ConformanceTests.cs`
- Create: `common/src/com.bun3.common/Tests/Editor/Bun3.Common.FixedMathSharp.Tests.asmdef`
- Create by Unity import: `common/src/com.bun3.common/Tests.meta`
- Create by Unity import: `common/src/com.bun3.common/Tests/Editor.meta`
- Create by Unity import: `common/src/com.bun3.common/Tests/Editor/Fixed64ConformanceTests.cs.meta`
- Create by Unity import: `common/src/com.bun3.common/Tests/Editor/Bun3.Common.FixedMathSharp.Tests.asmdef.meta`
- Create: `common/tests/Bun3.Common.Tests/Fixed64AllocationTests.cs`
- Modify: `unity/Packages/manifest.json`
- Modify by Unity resolver: `unity/Packages/packages-lock.json`

**Interfaces:**
- Consumes: NuGet `FixedMathSharp.Lean` 7.0.0, UPM assembly `FixedMathSharp.Lean.Runtime`, `Fixed64.FromRaw(long)`, public `Fixed64.m_rawValue`.
- Produces: Bun3.Common 0.3.0의 전이 Fixed64 API, .NET/Unity 공용 `Bun3.Common.Tests.Fixed64ConformanceTests`.

- [ ] **Step 1: .NET과 Unity가 공유할 Raw 골든 벡터 테스트를 작성한다**

`common/src/com.bun3.common/Tests/Editor/Fixed64ConformanceTests.cs`를 다음 내용으로 만든다.

```csharp
using System;
using System.Buffers.Binary;
using FixedMathSharp;
using NUnit.Framework;

namespace Bun3.Common.Tests;

[TestFixture]
public sealed class Fixed64ConformanceTests
{
    private static long Raw(Fixed64 value) => value.m_rawValue;

    [Test]
    public void Representation_constants_match_q32_32()
    {
        Assert.That(Raw(Fixed64.Zero), Is.EqualTo(0L));
        Assert.That(Raw(Fixed64.One), Is.EqualTo(1L << 32));
        Assert.That(Raw(Fixed64.Half), Is.EqualTo(1L << 31));
        Assert.That(Raw(Fixed64.MinIncrement), Is.EqualTo(1L));
        Assert.That(Raw(Fixed64.MinValue), Is.EqualTo(long.MinValue));
        Assert.That(Raw(Fixed64.MaxValue), Is.EqualTo(long.MaxValue));
    }

    [TestCase(0L)]
    [TestCase(1L)]
    [TestCase(-1L)]
    [TestCase(long.MinValue)]
    [TestCase(long.MaxValue)]
    public void FromRaw_preserves_every_input_bit(long raw)
    {
        Assert.That(Raw(Fixed64.FromRaw(raw)), Is.EqualTo(raw));
    }

    [Test]
    public void Multiply_and_divide_midpoints_round_to_even()
    {
        Assert.That(Raw(Fixed64.FromRaw(1) * Fixed64.Half), Is.EqualTo(0L));
        Assert.That(Raw(Fixed64.FromRaw(3) * Fixed64.Half), Is.EqualTo(2L));
        Assert.That(Raw(Fixed64.FromRaw(1) / Fixed64.Two), Is.EqualTo(0L));
        Assert.That(Raw(Fixed64.FromRaw(3) / Fixed64.Two), Is.EqualTo(2L));
    }

    [Test]
    public void Overflow_paths_saturate_deterministically()
    {
        Assert.That(Fixed64.MaxValue * Fixed64.Two, Is.EqualTo(Fixed64.MaxValue));
        Assert.That(Fixed64.MinValue * Fixed64.Two, Is.EqualTo(Fixed64.MinValue));
        Assert.That(Fixed64.MinValue / Fixed64.NegOne, Is.EqualTo(Fixed64.MaxValue));
    }

    [Test]
    public void Floating_boundary_rejects_non_finite_and_out_of_range_values()
    {
        Assert.That(() => Fixed64.FromDouble(double.NaN),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Fixed64.FromDouble(double.PositiveInfinity),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => Fixed64.FromDouble(double.MaxValue),
            Throws.TypeOf<OverflowException>());
    }

    [Test]
    public void Scalar_and_vector_math_have_exact_anchor_results()
    {
        Assert.That(Raw(FixedMath.Sqrt((Fixed64)4)), Is.EqualTo(2L << 32));
        Assert.That(Raw(FixedMath.Sin(Fixed64.Zero)), Is.EqualTo(0L));
        Assert.That(Raw(FixedMath.Cos(Fixed64.Zero)), Is.EqualTo(1L << 32));

        var normalized = new Vector2d(3, 0).Normalized;
        Assert.That(Raw(normalized.X), Is.EqualTo(1L << 32));
        Assert.That(Raw(normalized.Y), Is.EqualTo(0L));
    }

    [Test]
    public void Six_hundred_fixed_ticks_accumulate_the_same_raw_position()
    {
        var delta = Fixed64.FromRaw(71_582_788L); // round(2^32 / 60)
        var step = (Fixed64)6 * delta;
        var position = Fixed64.Zero;

        for (var i = 0; i < 600; i++)
        {
            position += step;
        }

        Assert.That(Raw(step), Is.EqualTo(429_496_728L));
        Assert.That(Raw(position), Is.EqualTo(257_698_036_800L));
    }

    [Test]
    public void Raw_state_hash_bytes_are_little_endian_signed_64_bit()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, Raw(Fixed64.One));

        Assert.That(bytes.ToArray(),
            Is.EqualTo(new byte[] { 0, 0, 0, 0, 1, 0, 0, 0 }));
    }
}
```

`Bun3.Common.FixedMathSharp.Tests.asmdef`는 다음 내용으로 만든다.

```json
{
  "name": "Bun3.Common.FixedMathSharp.Tests",
  "rootNamespace": "Bun3.Common.Tests",
  "references": [
    "FixedMathSharp.Lean.Runtime",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": [
    "nunit.framework.dll"
  ],
  "autoReferenced": false,
  "defineConstraints": [
    "UNITY_INCLUDE_TESTS"
  ],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 2: .NET 테스트 프로젝트가 같은 소스를 컴파일하도록 연결한다**

`Bun3.Common.csproj`에 테스트 소스 제외를 추가한다.

```xml
<ItemGroup>
  <Compile Remove="Tests/**/*.cs" />
</ItemGroup>
```

`Bun3.Common.Tests.csproj`의 기존 ItemGroup 뒤에 공유 소스 링크를 추가한다.

```xml
<ItemGroup>
  <Compile Include="..\..\src\com.bun3.common\Tests\Editor\Fixed64ConformanceTests.cs"
           Link="Fixed64ConformanceTests.cs" />
</ItemGroup>
```

- [ ] **Step 3: Fixed64 틱 연산 무할당 테스트를 추가한다**

`common/tests/Bun3.Common.Tests/Fixed64AllocationTests.cs`를 만든다.

```csharp
using System;
using FixedMathSharp;
using NUnit.Framework;

namespace Bun3.Common.Tests;

[TestFixture]
public sealed class Fixed64AllocationTests
{
    [Test]
    public void Arithmetic_tick_loop_does_not_allocate()
    {
        var step = Fixed64.FromRaw(429_496_728L);
        var position = Fixed64.Zero;

        position += step; // JIT warm-up
        position -= step;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 10_000; i++)
        {
            position += step;
            position -= step;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
        Assert.That(position, Is.EqualTo(Fixed64.Zero));
    }
}
```

- [ ] **Step 4: 의존성 전 테스트가 FixedMathSharp 누락으로 실패하는지 확인한다**

공유 테스트와 csproj 링크만 먼저 적용한 상태에서 실행한다.

```powershell
dotnet test common/tests/Bun3.Common.Tests --nologo --filter "FullyQualifiedName~Fixed64"
```

Expected: `FixedMathSharp` namespace/타입을 찾지 못하는 컴파일 실패.

- [ ] **Step 5: UPM 패키지와 프로젝트 manifest를 7.0.0으로 고정한다**

`Bun3.Common.csproj`에 Lean 의존성을 추가하고 버전을 갱신한다.

```xml
<Version>0.3.0</Version>
```

```xml
<ItemGroup>
  <PackageReference Include="FixedMathSharp.Lean" Version="7.0.0" />
</ItemGroup>
```

`common/src/com.bun3.common/package.json`을 다음 값으로 갱신한다.

```json
"version": "0.3.0",
"unity": "2022.3",
```

author 앞에 의존성을 추가한다.

```json
"dependencies": {
  "com.mrdav30.fixedmathsharp.lean": "7.0.0"
},
```

`unity/Packages/manifest.json`의 dependencies에 정확한 Git URL을 추가한다.

```json
"com.mrdav30.fixedmathsharp.lean": "https://github.com/mrdav30/FixedMathSharp-Unity.git?path=/com.mrdav30.fixedmathsharp.lean#v7.0.0",
```

최상위 dependencies 객체 뒤에 테스트 패키지를 명시한다.

```json
"testables": [
  "com.bun3.common"
]
```

- [ ] **Step 6: .NET 골든 벡터와 패키지 출력을 검증한다**

Run:

```powershell
dotnet restore common/src/com.bun3.common/Bun3.Common.csproj
dotnet test common/tests/Bun3.Common.Tests --nologo --filter "FullyQualifiedName~Fixed64"
dotnet test common/tests/Bun3.Common.Tests --nologo
dotnet pack common/src/com.bun3.common/Bun3.Common.csproj -c Release --nologo
dotnet list common/src/com.bun3.common/Bun3.Common.csproj package
```

Expected: Fixed64 공유/할당 테스트 PASS, Common 전체 PASS, pack 경고 0, 직접 패키지에 `FixedMathSharp.Lean 7.0.0` 표시.

- [ ] **Step 7: Unity가 패키지를 해석하고 같은 EditMode 골든 벡터를 실행하게 한다**

PowerShell에서 Unity를 실행한다.

```powershell
$resultPath = Join-Path $env:TEMP 'bun3-fixed64-editmode.xml'
$logPath = Join-Path $env:TEMP 'bun3-fixed64-editmode.log'
& 'E:\Unitys\6000.3.14f1\Editor\Unity.exe' -batchmode `
    -projectPath (Resolve-Path 'unity') `
    -runTests -testPlatform EditMode `
    -testFilter 'Bun3.Common.Tests.Fixed64ConformanceTests' `
    -testResults $resultPath -logFile $logPath -quit
if ($LASTEXITCODE -ne 0) { Get-Content -Tail 200 $logPath; exit $LASTEXITCODE }
```

Expected: `Fixed64ConformanceTests` 전체 PASS. Unity import가 네 `.meta` 파일과 `packages-lock.json`의 v7.0.0 커밋 잠금을 생성한다. 생성된 meta GUID는 손으로 작성하지 않는다.

- [ ] **Step 8: 잠금 파일과 패키지 버전이 정확한지 검사한다**

Run:

```powershell
rg -n 'fixedmathsharp|7\.0\.0|168b6f4f2a7dcf4164aab93db81754bae737de40' common/src/com.bun3.common unity/Packages/manifest.json unity/Packages/packages-lock.json
dotnet build Bun3.sln --nologo
dotnet test Bun3.sln --nologo
git diff --check
```

Expected: UPM URL은 `v7.0.0`, lock은 태그 커밋 `168b6f4f2a7dcf4164aab93db81754bae737de40`, 솔루션 빌드/테스트 경고 0, whitespace 오류 없음.

- [ ] **Step 9: Fixed64 통합을 커밋한다**

```powershell
git add common/src/com.bun3.common/Bun3.Common.csproj common/src/com.bun3.common/package.json common/src/com.bun3.common/Tests common/tests/Bun3.Common.Tests/Bun3.Common.Tests.csproj common/tests/Bun3.Common.Tests/Fixed64AllocationTests.cs unity/Packages/manifest.json unity/Packages/packages-lock.json
git commit -m "➕ FixedMathSharp Lean 7.0.0 결정론 기반 추가" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: 최종 결정론·패키지 회귀 검증

**Files:**
- Verify only: `common/src/com.bun3.gameplay/**`
- Verify only: `common/src/com.bun3.common/**`
- Verify only: `unity/Packages/**`

**Interfaces:**
- Consumes: Task 1~4의 BigNum 0.2.0과 Bun3.Common/FixedMathSharp 0.3.0/7.0.0.
- Produces: 구현 완료를 뒷받침하는 전체 빌드·테스트·패키지·Unity 증거.

- [ ] **Step 1: 변경 범위와 버전을 검토한다**

```powershell
git status --short
git diff HEAD~4 --stat
rg -n '"version": "0\.2\.0"|<Version>0\.2\.0</Version>' common/src/com.bun3.gameplay
rg -n '"version": "0\.3\.0"|<Version>0\.3\.0</Version>|"unity": "2022\.3"' common/src/com.bun3.common
```

Expected: 계획된 파일만 변경되고 Gameplay 두 버전은 0.2.0, Common 두 버전은 0.3.0, Common Unity 하한은 2022.3.

- [ ] **Step 2: .NET 전체 검증을 깨끗한 빌드로 실행한다**

```powershell
dotnet clean Bun3.sln --nologo
dotnet build Bun3.sln --nologo
dotnet test Bun3.sln --no-build --nologo
```

Expected: 빌드 경고 0/오류 0, Gameplay·Common·Server 전체 테스트 PASS.

- [ ] **Step 3: Unity EditMode 적합성 테스트를 한 번 더 실행한다**

```powershell
$resultPath = Join-Path $env:TEMP 'bun3-fixed64-final-editmode.xml'
$logPath = Join-Path $env:TEMP 'bun3-fixed64-final-editmode.log'
& 'E:\Unitys\6000.3.14f1\Editor\Unity.exe' -batchmode `
    -projectPath (Resolve-Path 'unity') `
    -runTests -testPlatform EditMode `
    -testResults $resultPath -logFile $logPath -quit
if ($LASTEXITCODE -ne 0) { Get-Content -Tail 200 $logPath; exit $LASTEXITCODE }
```

Expected: Fixed64 골든 벡터를 포함한 저장소의 모든 Unity EditMode 테스트 PASS.

- [ ] **Step 4: 저장소가 깨끗하고 커밋 경계가 계획과 일치하는지 확인한다**

```powershell
git status --short
git log -5 --oneline
```

Expected: working tree clean. 최근 커밋에 BigNum 경계, float 변환, 포맷 불변성, Fixed64 통합이 각각 분리되어 있다. 검증이 추적 파일을 바꾸면 완료로 판단하지 말고 변경 원인을 해결한 뒤 Step 2부터 다시 실행한다.
