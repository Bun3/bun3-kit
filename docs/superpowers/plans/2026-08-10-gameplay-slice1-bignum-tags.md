# Bun3.Gameplay 슬라이스 1: BigNum + GameplayTag 구현 플랜

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bun3.Gameplay 패키지의 기반 계층 — 결정론적 대수(BigNum, 가수+지수)와 언리얼식 계층 태그(TagRegistry/GameplayTag/TagSet), 무할당 표시 포맷(TryFormat) — 를 완전한 테스트와 함께 구축한다.

**Architecture:** 스펙 `docs/superpowers/specs/2026-08-10-gameplay-framework-design.md` §6·§7. 순수 netstandard2.1 라이브러리(의존성 0 — Google.Protobuf/Bun3.Common은 후속 슬라이스에서 필요 시 추가). BigNum은 정수 연산만 사용(64×64→128 곱, 128÷64 이진 나눗셈)해 플랫폼 무관 결정론. 태그는 등록 시 1회 인터닝된 핸들(int)로 심 핫패스 무할당.

**Tech Stack:** C# 9 / netstandard2.1(패키지), net10.0 + NUnit 4(테스트, 오라클로 System.Numerics.BigInteger 사용)

## Global Constraints

- 패키지 코드: netstandard2.1 + `<LangVersion>9.0</LangVersion>` + 블록 네임스페이스(파일 스코프 금지)
- 모든 public 멤버에 **한국어 XML 문서** — 작성 시점에 함께
- 빌드 경고 0
- 런타임 문자열 할당 금지(핫패스): 포맷은 `TryFormat(Span<char>)`, 태그 이름은 등록 시 인터닝. LINQ 금지
- 커밋: gitmoji + `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` 트레일러. 서브에이전트는 `git commit -m "<제목>" -m "<트레일러>"` 이중 플래그 사용(here-string 금지)
- 작업 브랜치: `Bun3/gameplay-base` (이미 생성됨)
- 예외 정책(스펙 §6): BigNum 지수 오버플로·0 나눗셈은 throw, 지수 언더플로(극소값)는 Zero, 절사는 0 방향

---

### Task 1: 프로젝트 스캐폴드 (gameplay/ 영역 + 솔루션 등록)

**Files:**
- Create: `gameplay/Directory.Build.props`
- Create: `gameplay/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Create: `gameplay/src/com.bun3.gameplay/package.json`
- Create: `gameplay/src/com.bun3.gameplay/Bun3.Gameplay.asmdef`
- Create: `gameplay/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
- Create: `gameplay/tests/Bun3.Gameplay.Tests/SmokeTests.cs`
- Modify: `Bun3.sln` (dotnet sln 명령 사용 — 직접 편집 금지)

**Interfaces:**
- Consumes: 없음
- Produces: 이후 모든 태스크가 사용하는 프로젝트 골격. 네임스페이스 루트 `Bun3.Gameplay`

- [ ] **Step 1: 디렉터리와 프로젝트 파일 생성**

`gameplay/Directory.Build.props` (common/Directory.Build.props와 동일):

```xml
<Project>
  <PropertyGroup>
    <UseArtifactsOutput>true</UseArtifactsOutput>
  </PropertyGroup>
</Project>
```

`gameplay/src/com.bun3.gameplay/Bun3.Gameplay.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Gameplay</RootNamespace>
    <AssemblyName>Bun3.Gameplay</AssemblyName>
    <PackageId>Bun3.Gameplay</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
  </PropertyGroup>

</Project>
```

`gameplay/src/com.bun3.gameplay/package.json`:

```json
{
  "name": "com.bun3.gameplay",
  "displayName": "Bun3 Gameplay",
  "version": "0.1.0",
  "unity": "2020.1",
  "description": "Generic gameplay framework (BigNum, tags, attributes, effects, abilities). netstandard2.1; no UnityEngine dependency.",
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  }
}
```

`gameplay/src/com.bun3.gameplay/Bun3.Gameplay.asmdef`:

```json
{
    "name": "Bun3.Gameplay",
    "rootNamespace": "Bun3.Gameplay",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

주의: Unity `.meta` 파일은 이 플랜에서 만들지 않는다 — Unity 프로젝트에서 패키지를 처음 열 때 생성해 커밋(후속 작업, [[pooled-collections-followups]] 메모와 동일 트랙). 소스 코드는 `Runtime/` 하위에 둔다(공용 패턴).

`gameplay/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <RootNamespace>Bun3.Gameplay.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="NUnit" Version="4.1.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\com.bun3.gameplay\Bun3.Gameplay.csproj" />
  </ItemGroup>

</Project>
```

`gameplay/tests/Bun3.Gameplay.Tests/SmokeTests.cs`:

```csharp
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class SmokeTests
{
    [Test]
    public void Test_project_builds_and_runs()
    {
        Assert.Pass();
    }
}
```

- [ ] **Step 2: 솔루션 등록**

Run (레포 루트에서):
```bash
dotnet sln Bun3.sln add --solution-folder com.bun3.gameplay gameplay/src/com.bun3.gameplay/Bun3.Gameplay.csproj
dotnet sln Bun3.sln add --solution-folder com.bun3.gameplay gameplay/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj
```

- [ ] **Step 3: 빌드·스모크 테스트 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo`
Expected: 통과 1건, 경고 0

- [ ] **Step 4: Commit**

```bash
git add gameplay Bun3.sln
git commit -m "🎉 Bun3.Gameplay 패키지 스캐폴드 (netstandard2.1 + 테스트 프로젝트)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Int128Math — 64×64→128 곱, 128÷64 나눗셈

**Files:**
- Create: `gameplay/src/com.bun3.gameplay/Runtime/Numerics/Int128Math.cs`
- Test: `gameplay/tests/Bun3.Gameplay.Tests/Int128MathTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces (Task 3·4가 사용):
  - `internal static void Int128Math.Mul64(ulong a, ulong b, out ulong hi, out ulong lo)`
  - `internal static void Int128Math.DivRem(ulong uHi, ulong uLo, ulong divisor, out ulong qHi, out ulong qLo, out ulong remainder)` — divisor 0이면 `DivideByZeroException`
- 테스트 프로젝트에서 internal 접근을 위해 csproj에 InternalsVisibleTo 추가(아래 Step 1)

- [ ] **Step 1: InternalsVisibleTo 추가**

`Bun3.Gameplay.csproj`의 `</PropertyGroup>` 아래에 추가:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Bun3.Gameplay.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: 실패하는 테스트 작성**

`gameplay/tests/Bun3.Gameplay.Tests/Int128MathTests.cs`:

```csharp
using System;
using System.Numerics;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class Int128MathTests
{
    // BigInteger를 오라클로 사용 — 프레임워크 결과와 무한 정밀 계산을 비교한다.
    private static readonly ulong[] Samples =
    {
        0UL, 1UL, 9UL, 10UL, 999_999_999UL, 1_000_000_000UL,
        uint.MaxValue, (ulong)uint.MaxValue + 1,
        1_000_000_000_000_000_000UL,           // 10^18
        9_223_372_036_854_775_807UL,           // long.MaxValue
        ulong.MaxValue,
    };

    [Test]
    public void Mul64_matches_BigInteger_oracle()
    {
        foreach (var a in Samples)
        foreach (var b in Samples)
        {
            Int128Math.Mul64(a, b, out var hi, out var lo);
            var actual = ((BigInteger)hi << 64) | lo;
            Assert.That(actual, Is.EqualTo((BigInteger)a * b), $"{a} * {b}");
        }
    }

    [Test]
    public void DivRem_matches_BigInteger_oracle()
    {
        foreach (var a in Samples)
        foreach (var b in Samples)
        foreach (var d in Samples)
        {
            if (d == 0)
            {
                continue;
            }

            var dividend = ((BigInteger)a << 64) | b;
            Int128Math.DivRem(a, b, d, out var qHi, out var qLo, out var rem);
            var quotient = ((BigInteger)qHi << 64) | qLo;
            Assert.That(quotient, Is.EqualTo(dividend / d), $"({a}:{b}) / {d} 몫");
            Assert.That((BigInteger)rem, Is.EqualTo(dividend % d), $"({a}:{b}) / {d} 나머지");
        }
    }

    [Test]
    public void DivRem_by_zero_throws()
    {
        Assert.Throws<DivideByZeroException>(() =>
            Int128Math.DivRem(1, 0, 0, out _, out _, out _));
    }
}
```

- [ ] **Step 3: 테스트가 실패하는지 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter Int128MathTests`
Expected: 컴파일 실패("Int128Math를 찾을 수 없음") — 실패 확인이면 충분

- [ ] **Step 4: 구현**

`gameplay/src/com.bun3.gameplay/Runtime/Numerics/Int128Math.cs`:

```csharp
using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// BigNum이 쓰는 최소한의 128비트 정수 연산. netstandard2.1에는 UInt128/Math.BigMul이
    /// 없으므로 직접 구현한다 — 전부 정수 연산이라 플랫폼 무관 결정론.
    /// </summary>
    internal static class Int128Math
    {
        /// <summary>부호 없는 64×64 → 128비트 곱. 32비트 반분할 스쿨북.</summary>
        internal static void Mul64(ulong a, ulong b, out ulong hi, out ulong lo)
        {
            ulong aLo = (uint)a;
            ulong aHi = a >> 32;
            ulong bLo = (uint)b;
            ulong bHi = b >> 32;

            ulong ll = aLo * bLo;
            ulong lh = aLo * bHi;
            ulong hl = aHi * bLo;
            ulong hh = aHi * bHi;

            ulong mid = (ll >> 32) + (uint)lh + (uint)hl;
            lo = (mid << 32) | (uint)ll;
            hi = hh + (lh >> 32) + (hl >> 32) + (mid >> 32);
        }

        /// <summary>
        /// 부호 없는 128비트 ÷ 64비트 → 몫 128비트 + 나머지. 이진 롱 디비전(128회 루프) —
        /// 단순하고 자명하게 정확하다. BigNum 연산 빈도(수정자 재계산 수준)에는 충분히 빠르며,
        /// 병목으로 측정되면 Knuth D로 교체한다.
        /// </summary>
        internal static void DivRem(
            ulong uHi, ulong uLo, ulong divisor, out ulong qHi, out ulong qLo, out ulong remainder)
        {
            if (divisor == 0)
            {
                throw new DivideByZeroException();
            }

            if (uHi == 0)
            {
                qHi = 0;
                qLo = uLo / divisor;
                remainder = uLo % divisor;
                return;
            }

            qHi = 0;
            qLo = 0;
            ulong rem = 0;
            for (var i = 127; i >= 0; i--)
            {
                var carry = rem >> 63;
                rem = (rem << 1) | ((i >= 64 ? uHi >> (i - 64) : uLo >> i) & 1);
                if (carry != 0 || rem >= divisor)
                {
                    rem -= divisor;   // carry 시 2^64 초과분이 언더플로 래핑으로 정확히 상쇄된다
                    if (i >= 64)
                    {
                        qHi |= 1UL << (i - 64);
                    }
                    else
                    {
                        qLo |= 1UL << i;
                    }
                }
            }

            remainder = rem;
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter Int128MathTests`
Expected: 3건 통과

- [ ] **Step 6: Commit**

```bash
git add gameplay
git commit -m "✨ Int128Math: 결정론적 128비트 곱/나눗셈 (BigNum 기반)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: BigNum 표현·정규화·덧셈·비교

**Files:**
- Create: `gameplay/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs`
- Create: `gameplay/src/com.bun3.gameplay/Runtime/Numerics/BigNumOverflowException.cs`
- Test: `gameplay/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs`

**Interfaces:**
- Consumes: `Int128Math.Mul64/DivRem` (Task 2)
- Produces (Task 4·5와 이후 슬라이스가 사용):
  - `readonly struct BigNum : IEquatable<BigNum>, IComparable<BigNum>`
  - `long BigNum.Mantissa { get; }` / `int BigNum.Exponent { get; }` — 값 = Mantissa × 10^Exponent
  - `static BigNum BigNum.FromParts(long mantissa, int exponent)` / `implicit operator BigNum(long)`
  - `static readonly BigNum BigNum.Zero / One`
  - `bool IsZero { get; }` / `int Sign { get; }`
  - `operator + - ==` `!=` `< <= > >=`, 단항 `-`
  - `const int BigNum.MaxExponent = 100_000_000` (초과 시 `BigNumOverflowException`, 미만 언더플로는 Zero)
  - 정규 형식 불변식: `Mantissa == 0 ⇒ Exponent == 0`, 그 외 `Mantissa % 10 != 0` — 같은 값은 항상 같은 비트

- [ ] **Step 1: 실패하는 테스트 작성**

`gameplay/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs`:

```csharp
using System;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class BigNumBasicTests
{
    [Test]
    public void Canonical_form_is_unique()
    {
        // 같은 값의 서로 다른 표현이 같은 비트로 정규화된다
        var a = BigNum.FromParts(5, 0);
        var b = BigNum.FromParts(500, -2);
        var c = BigNum.FromParts(5_000_000, -6);
        Assert.That(a, Is.EqualTo(b));
        Assert.That(b, Is.EqualTo(c));
        Assert.That(a.Mantissa, Is.EqualTo(5));
        Assert.That(a.Exponent, Is.EqualTo(0));
        Assert.That(b.Mantissa, Is.EqualTo(5));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Zero_is_canonical()
    {
        Assert.That(BigNum.FromParts(0, 999).IsZero, Is.True);
        Assert.That(BigNum.FromParts(0, 999), Is.EqualTo(BigNum.Zero));
        Assert.That(BigNum.Zero.Exponent, Is.EqualTo(0));
        Assert.That(BigNum.Zero.Sign, Is.EqualTo(0));
    }

    [Test]
    public void Long_conversion_is_exact_to_long_max()
    {
        // 922경(long.MaxValue)까지 정수 정확 — 스펙 §6
        var max = (BigNum)long.MaxValue;
        Assert.That(max.Sign, Is.EqualTo(1));
        Assert.That(max, Is.EqualTo(BigNum.FromParts(long.MaxValue, 0)));

        var min = (BigNum)(long.MinValue + 1);
        Assert.That(min.Sign, Is.EqualTo(-1));
    }

    [Test]
    public void Addition_small_integers_exact()
    {
        Assert.That((BigNum)3 + 5, Is.EqualTo((BigNum)8));
        Assert.That((BigNum)1_000_000_000_000L + 1, Is.EqualTo((BigNum)1_000_000_000_001L));
        Assert.That((BigNum)7 + (-7), Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void Addition_at_long_scale_stays_exact()
    {
        // 9e18 + 2e17 = 9.2e18 — 유효 자릿수 안에서 정확
        var a = (BigNum)9_000_000_000_000_000_000L;
        var b = (BigNum)200_000_000_000_000_000L;
        Assert.That(a + b, Is.EqualTo((BigNum)9_200_000_000_000_000_000L));
    }

    [Test]
    public void Addition_beyond_significant_digits_drops_small_term()
    {
        // 1e30 + 1 = 1e30 — 방치형 표준 타협 (스펙 §6)
        var big = BigNum.FromParts(1, 30);
        Assert.That(big + 1, Is.EqualTo(big));
    }

    [Test]
    public void Subtraction_cancellation_is_exact_within_window()
    {
        var a = BigNum.FromParts(1, 18);                 // 1e18
        var b = (BigNum)999_999_999_999_999_999L;        // 1e18 - 1
        Assert.That(a - b, Is.EqualTo((BigNum)1));
    }

    [Test]
    public void Comparison_orders_by_value()
    {
        Assert.That((BigNum)5 < 6, Is.True);
        Assert.That(BigNum.FromParts(1, 30) > BigNum.FromParts(999, 27), Is.True);   // 1e30 > 9.99e29
        Assert.That((BigNum)(-5) < 3, Is.True);
        Assert.That(BigNum.FromParts(-1, 30) < BigNum.FromParts(-999, 27), Is.True); // -1e30 < -9.99e29
        Assert.That((BigNum)7 <= 7 && (BigNum)7 >= 7, Is.True);
    }

    [Test]
    public void Exponent_overflow_throws_underflow_clamps_to_zero()
    {
        Assert.Throws<BigNumOverflowException>(() => BigNum.FromParts(1, BigNum.MaxExponent + 1));
        Assert.That(BigNum.FromParts(1, -BigNum.MaxExponent - 1), Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void Determinism_same_ops_same_bits()
    {
        // 연산 결과의 비트 동일성 — 결정론 계약의 최소 검증
        var x1 = BigNum.FromParts(123_456_789, 5) + BigNum.FromParts(-987, 10) - 42;
        var x2 = BigNum.FromParts(123_456_789, 5) + BigNum.FromParts(-987, 10) - 42;
        Assert.That(x1.Mantissa, Is.EqualTo(x2.Mantissa));
        Assert.That(x1.Exponent, Is.EqualTo(x2.Exponent));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter BigNumBasicTests`
Expected: 컴파일 실패("BigNum을 찾을 수 없음")

- [ ] **Step 3: 구현**

`gameplay/src/com.bun3.gameplay/Runtime/Numerics/BigNumOverflowException.cs`:

```csharp
using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>BigNum 지수가 표현 한계를 넘었을 때. 정당한 게임플레이로는 도달 불가능한
    /// 규모이므로, 밸런스 공식 폭주 버그를 숨기지 않기 위해 클램프 대신 던진다(스펙 §6).</summary>
    public sealed class BigNumOverflowException : OverflowException
    {
        /// <summary>지수 값과 함께 예외를 생성한다.</summary>
        public BigNumOverflowException(long exponent)
            : base($"BigNum 지수 {exponent}가 한계(±{BigNum.MaxExponent})를 넘었다 — 공식 폭주를 의심할 것.")
        {
        }
    }
}
```

`gameplay/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs` — Task 3 범위(곱·나눗셈은 Task 4에서 같은 파일에 추가):

```csharp
using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// 결정론적 십진 대수: 값 = Mantissa × 10^Exponent. 정수 연산만 사용하므로 플랫폼
    /// 무관하게 비트 동일 결과를 낸다. 유효 18~19자리 — long 범위(±9.2×10^18)까지 정수
    /// 정확, 그 너머는 근사(하위 자릿수 절사, 0 방향).
    /// 정규 형식: Mantissa==0이면 Exponent==0, 그 외 Mantissa는 10의 배수가 아니다 —
    /// 같은 값은 항상 같은 비트라 동등성·해시가 필드 비교로 끝난다.
    /// </summary>
    public readonly struct BigNum : IEquatable<BigNum>, IComparable<BigNum>
    {
        /// <summary>지수 한계. 초과는 <see cref="BigNumOverflowException"/>, 미만(언더플로)은 0으로 수렴.</summary>
        public const int MaxExponent = 100_000_000;

        private const long LongMaxDiv10 = long.MaxValue / 10;          //  922337203685477580
        private const long HalfLongMax = long.MaxValue / 2;

        /// <summary>가수. 정규 형식에서 10의 배수가 아니다(0 제외).</summary>
        public readonly long Mantissa;

        /// <summary>십진 지수.</summary>
        public readonly int Exponent;

        /// <summary>0.</summary>
        public static readonly BigNum Zero = default;

        /// <summary>1.</summary>
        public static readonly BigNum One = new BigNum(1, 0);

        private BigNum(long mantissa, int exponent)
        {
            Mantissa = mantissa;
            Exponent = exponent;
        }

        /// <summary>가수×10^지수로 값을 만든다. 정규화하며, 지수 한계 초과 시 던진다.</summary>
        public static BigNum FromParts(long mantissa, int exponent) =>
            Canonicalize(mantissa, exponent);

        /// <summary>long 정수는 정확하게 변환된다.</summary>
        public static implicit operator BigNum(long value) => Canonicalize(value, 0);

        /// <summary>int 정수는 정확하게 변환된다.</summary>
        public static implicit operator BigNum(int value) => Canonicalize(value, 0);

        /// <summary>값이 0인지 여부.</summary>
        public bool IsZero => Mantissa == 0;

        /// <summary>부호: -1, 0, +1.</summary>
        public int Sign => Math.Sign(Mantissa);

        private static BigNum Canonicalize(long mantissa, long exponent)
        {
            if (mantissa == 0)
            {
                return default;
            }

            if (mantissa == long.MinValue)
            {
                // 절댓값 부정이 불가능한 유일한 값 — 한 자리 내려 정규화 경로에 합류
                mantissa /= 10;
                exponent++;
            }

            while (mantissa % 10 == 0)
            {
                mantissa /= 10;
                exponent++;
            }

            if (exponent > MaxExponent)
            {
                throw new BigNumOverflowException(exponent);
            }

            if (exponent < -MaxExponent)
            {
                return default;   // 언더플로 — 극소값은 0으로 수렴(정보 손실이 자연스러운 방향)
            }

            return new BigNum(mantissa, (int)exponent);
        }

        /// <summary>덧셈. 유효 자릿수 밖의 항은 절사된다(0 방향).</summary>
        public static BigNum operator +(BigNum a, BigNum b)
        {
            if (a.IsZero)
            {
                return b;
            }

            if (b.IsZero)
            {
                return a;
            }

            if (a.Exponent < b.Exponent)
            {
                (a, b) = (b, a);   // a가 큰 지수
            }

            long am = a.Mantissa;
            long ae = a.Exponent;
            long bm = b.Mantissa;
            long be = b.Exponent;

            // a 가수를 키워 지수를 b에 근접 — 정밀도 보존
            while (ae > be && am > -LongMaxDiv10 && am < LongMaxDiv10)
            {
                am *= 10;
                ae--;
            }

            var gap = ae - be;
            if (gap > 18)
            {
                return a;   // b는 유효 자릿수 창 밖
            }

            // 남은 갭은 b를 절사해 올린다
            for (var i = 0L; i < gap; i++)
            {
                bm /= 10;
            }

            // 합이 long을 넘지 않도록 한 자리 양보 (같은 지수 정렬 유지)
            if (am > HalfLongMax || am < -HalfLongMax || bm > HalfLongMax || bm < -HalfLongMax)
            {
                am /= 10;
                bm /= 10;
                ae++;
            }

            return Canonicalize(am + bm, ae);
        }

        /// <summary>뺄셈.</summary>
        public static BigNum operator -(BigNum a, BigNum b) => a + (-b);

        /// <summary>부호 반전.</summary>
        public static BigNum operator -(BigNum value) =>
            value.IsZero ? value : new BigNum(-value.Mantissa, value.Exponent);

        /// <summary>값 비교. 유효 자릿수 밖 차이는 같음으로 본다(정밀도 계약과 일관).</summary>
        public int CompareTo(BigNum other) => (this - other).Sign;

        /// <summary>정규 형식 필드 비교 — 같은 값은 항상 같은 비트다.</summary>
        public bool Equals(BigNum other) => Mantissa == other.Mantissa && Exponent == other.Exponent;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is BigNum other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Mantissa, Exponent);

        /// <summary>동등 비교.</summary>
        public static bool operator ==(BigNum a, BigNum b) => a.Equals(b);

        /// <summary>비동등 비교.</summary>
        public static bool operator !=(BigNum a, BigNum b) => !a.Equals(b);

        /// <summary>미만.</summary>
        public static bool operator <(BigNum a, BigNum b) => a.CompareTo(b) < 0;

        /// <summary>이하.</summary>
        public static bool operator <=(BigNum a, BigNum b) => a.CompareTo(b) <= 0;

        /// <summary>초과.</summary>
        public static bool operator >(BigNum a, BigNum b) => a.CompareTo(b) > 0;

        /// <summary>이상.</summary>
        public static bool operator >=(BigNum a, BigNum b) => a.CompareTo(b) >= 0;

        /// <summary>디버그 표기. 핫패스 사용 금지 — 표시용은 TryFormat(Task 5).</summary>
        public override string ToString() =>
            Exponent == 0 ? Mantissa.ToString() : $"{Mantissa}e{Exponent}";
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter BigNumBasicTests`
Expected: 10건 통과 (Int128MathTests 포함 전체도 통과)

- [ ] **Step 5: Commit**

```bash
git add gameplay
git commit -m "✨ BigNum: 정규형·덧셈·비교 (결정론적 가수+지수 대수)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: BigNum 곱셈·나눗셈·예외 경로

**Files:**
- Modify: `gameplay/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs` (연산자 추가)
- Test: `gameplay/tests/Bun3.Gameplay.Tests/BigNumMulDivTests.cs`

**Interfaces:**
- Consumes: Task 2·3의 전부
- Produces:
  - `operator *` / `operator /` — 유효 18~19자리 유지, 절사(0 방향)
  - `/`의 0 나눗셈은 `DivideByZeroException`, 지수 초과는 `BigNumOverflowException`

- [ ] **Step 1: 실패하는 테스트 작성**

`gameplay/tests/Bun3.Gameplay.Tests/BigNumMulDivTests.cs`:

```csharp
using System;
using System.Numerics;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class BigNumMulDivTests
{
    [Test]
    public void Multiply_small_integers_exact()
    {
        Assert.That((BigNum)6 * 7, Is.EqualTo((BigNum)42));
        Assert.That((BigNum)(-6) * 7, Is.EqualTo((BigNum)(-42)));
        Assert.That((BigNum)123_456_789L * 1_000_000_000L,
            Is.EqualTo((BigNum)123_456_789_000_000_000L));
        Assert.That(BigNum.Zero * 12345, Is.EqualTo(BigNum.Zero));
    }

    [Test]
    public void Multiply_huge_matches_BigInteger_leading_digits()
    {
        // 1.4e14 × 1.4e14 — FixedFloat이 못 하던 곱 (스펙 §6 근거)
        var a = (BigNum)140_000_000_000_000L;
        var product = a * a;
        Assert.That(product, Is.EqualTo(BigNum.FromParts(196, 26)));   // 1.96e28
    }

    [Test]
    public void Multiply_retains_18_significant_digits()
    {
        // 두 18자리 수의 곱 — 선두 18~19자리가 BigInteger 오라클과 일치해야 한다
        long m1 = 123_456_789_012_345_678L;
        long m2 = 987_654_321_098_765_432L;
        var product = (BigNum)m1 * m2;

        var oracle = (BigInteger)m1 * m2;                 // 121932631137021795... (36자리)
        var oracleStr = oracle.ToString();
        // 정규화가 트레일링 0을 지수로 옮길 수 있으므로 가수 전체가 오라클의 접두인지 본다
        var resultDigits = Math.Abs(product.Mantissa).ToString();
        Assert.That(oracleStr.StartsWith(resultDigits), Is.True,
            $"oracle={oracleStr} result={product.Mantissa}e{product.Exponent}");
        Assert.That(resultDigits.Length, Is.GreaterThanOrEqualTo(17), "유효 자릿수 유지 확인");
    }

    [Test]
    public void Percent_scaling_pattern_works_at_idle_scale()
    {
        // 방치형 핵심 패턴: 초대형 데미지 × 퍼센트 배율
        var damage = BigNum.FromParts(37, 28);            // 3.7e29
        var multiplied = damage * BigNum.FromParts(15, -1);   // ×1.5
        Assert.That(multiplied, Is.EqualTo(BigNum.FromParts(555, 27)));   // 5.55e29
    }

    [Test]
    public void Divide_exact_and_truncating()
    {
        Assert.That((BigNum)84 / 2, Is.EqualTo((BigNum)42));
        Assert.That((BigNum)1 / 4, Is.EqualTo(BigNum.FromParts(25, -2)));    // 0.25
        Assert.That((BigNum)(-84) / 2, Is.EqualTo((BigNum)(-42)));

        // 1/3 = 0.333... — 18~19자리 절사
        var third = (BigNum)1 / 3;
        Assert.That(third.Sign, Is.EqualTo(1));
        Assert.That(third < BigNum.FromParts(334, -3) && third > BigNum.FromParts(333, -3),
            Is.True, $"1/3 = {third.Mantissa}e{third.Exponent}");
    }

    [Test]
    public void Divide_huge_by_tiny_and_vice_versa()
    {
        var huge = BigNum.FromParts(5, 40);
        Assert.That(huge / BigNum.FromParts(2, -3), Is.EqualTo(BigNum.FromParts(25, 42)));
        Assert.That(BigNum.FromParts(2, -3) / huge, Is.EqualTo(BigNum.FromParts(4, -44)));
    }

    [Test]
    public void Divide_by_zero_throws()
    {
        Assert.Throws<DivideByZeroException>(() => _ = (BigNum)1 / BigNum.Zero);
    }

    [Test]
    public void Exponent_overflow_on_multiply_throws()
    {
        var big = BigNum.FromParts(1, BigNum.MaxExponent - 1);
        Assert.Throws<BigNumOverflowException>(() => _ = big * big);
    }

    [Test]
    public void Exponent_underflow_on_divide_returns_zero()
    {
        var tiny = BigNum.FromParts(1, -BigNum.MaxExponent + 1);
        var huge = BigNum.FromParts(1, BigNum.MaxExponent - 1);
        Assert.That(tiny / huge, Is.EqualTo(BigNum.Zero));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter BigNumMulDivTests`
Expected: 컴파일 실패("연산자 *를 적용할 수 없음")

- [ ] **Step 3: 구현 — BigNum.cs에 추가**

`BigNum` 구조체 안(비교 연산자들 앞)에 추가:

```csharp
        private const ulong TenPow18 = 1_000_000_000_000_000_000UL;

        /// <summary>곱셈. 결과는 유효 18~19자리로 절사(0 방향)된다.</summary>
        public static BigNum operator *(BigNum a, BigNum b)
        {
            if (a.IsZero || b.IsZero)
            {
                return Zero;
            }

            var negative = (a.Mantissa < 0) != (b.Mantissa < 0);
            var ua = (ulong)Math.Abs(a.Mantissa);
            var ub = (ulong)Math.Abs(b.Mantissa);

            Int128Math.Mul64(ua, ub, out var hi, out var lo);
            var exponent = (long)a.Exponent + b.Exponent;
            var mantissa = ReduceToLong(hi, lo, ref exponent);
            return Canonicalize(negative ? -mantissa : mantissa, exponent);
        }

        /// <summary>나눗셈. 결과는 유효 18~19자리로 절사(0 방향)된다. 0으로 나누면 던진다.</summary>
        public static BigNum operator /(BigNum a, BigNum b)
        {
            if (b.IsZero)
            {
                throw new DivideByZeroException();
            }

            if (a.IsZero)
            {
                return Zero;
            }

            var negative = (a.Mantissa < 0) != (b.Mantissa < 0);
            var ua = (ulong)Math.Abs(a.Mantissa);
            var ub = (ulong)Math.Abs(b.Mantissa);

            // (가수a × 10^18) ÷ 가수b — 몫이 18~19자리 정밀도를 갖도록 분자를 키운다
            Int128Math.Mul64(ua, TenPow18, out var hi, out var lo);
            Int128Math.DivRem(hi, lo, ub, out var qHi, out var qLo, out _);
            var exponent = (long)a.Exponent - b.Exponent - 18;
            var mantissa = ReduceToLong(qHi, qLo, ref exponent);
            return Canonicalize(negative ? -mantissa : mantissa, exponent);
        }

        // 128비트 값을 long 범위(≤ long.MaxValue)까지 10^k 절사로 줄인다. exponent에 k를 더한다.
        private static long ReduceToLong(ulong hi, ulong lo, ref long exponent)
        {
            if (hi != 0)
            {
                Int128Math.DivRem(hi, lo, TenPow18, out hi, out lo, out _);
                exponent += 18;
            }

            // 위 나눗셈 후에도 최대 ~8.5×10^19 — 한두 자리 더 내린다
            while (hi != 0 || lo > long.MaxValue)
            {
                Int128Math.DivRem(hi, lo, 10, out hi, out lo, out _);
                exponent++;
            }

            return (long)lo;
        }
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter BigNumMulDivTests`
Expected: 9건 통과 (전체 스위트도 통과)

- [ ] **Step 5: Commit**

```bash
git add gameplay
git commit -m "✨ BigNum 곱셈·나눗셈 + 오버플로/0나눗셈 예외 정책" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: BigNum 무할당 표시 포맷 (TryFormat + BigNumFormat)

**Files:**
- Create: `gameplay/src/com.bun3.gameplay/Runtime/Numerics/BigNumFormat.cs`
- Modify: `gameplay/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs` (TryFormat 추가)
- Test: `gameplay/tests/Bun3.Gameplay.Tests/BigNumFormatTests.cs`

**Interfaces:**
- Consumes: Task 3·4의 BigNum
- Produces:
  - `sealed class BigNumFormat { int GroupDigits; string[] Units; }` + `static BigNumFormat.Korean / Alpha`
  - `bool BigNum.TryFormat(Span<char> destination, out int charsWritten, BigNumFormat? format = null)`
    — 무할당. 단위 테이블 초과 시 지수 표기(`1.23e45`) 폴백. format null = `BigNumFormat.Alpha`
  - 소수부는 최대 2자리, 트레일링 0 제거. 목적지가 부족하면 false

- [ ] **Step 1: 실패하는 테스트 작성**

`gameplay/tests/Bun3.Gameplay.Tests/BigNumFormatTests.cs`:

```csharp
using System;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class BigNumFormatTests
{
    private static string Format(BigNum value, BigNumFormat? format = null)
    {
        Span<char> buffer = stackalloc char[64];
        Assert.That(value.TryFormat(buffer, out var written, format), Is.True);
        return new string(buffer[..written]);
    }

    [Test]
    public void Small_integers_render_plain()
    {
        Assert.That(Format(0), Is.EqualTo("0"));
        Assert.That(Format(42), Is.EqualTo("42"));
        Assert.That(Format(-42), Is.EqualTo("-42"));
        Assert.That(Format(999), Is.EqualTo("999"));
    }

    [Test]
    public void Small_fractions_render_with_decimal_point()
    {
        Assert.That(Format(BigNum.FromParts(125, -2)), Is.EqualTo("1.25"));
        Assert.That(Format(BigNum.FromParts(-5, -1)), Is.EqualTo("-0.5"));
    }

    [Test]
    public void Alpha_units_group_by_thousands()
    {
        Assert.That(Format((BigNum)1_500), Is.EqualTo("1.5K"));
        Assert.That(Format((BigNum)2_000_000), Is.EqualTo("2M"));
        Assert.That(Format((BigNum)3_450_000_000L), Is.EqualTo("3.45B"));
        Assert.That(Format((BigNum)7_000_000_000_000L), Is.EqualTo("7T"));
        Assert.That(Format((BigNum)(-1_500)), Is.EqualTo("-1.5K"));
    }

    [Test]
    public void Korean_units_group_by_ten_thousands()
    {
        var fmt = BigNumFormat.Korean;
        Assert.That(Format((BigNum)15_000, fmt), Is.EqualTo("1.5만"));
        Assert.That(Format((BigNum)200_000_000, fmt), Is.EqualTo("2억"));
        Assert.That(Format((BigNum)3_000_000_000_000L, fmt), Is.EqualTo("3조"));
        Assert.That(Format(BigNum.FromParts(92, 17), fmt), Is.EqualTo("920경"));
    }

    [Test]
    public void Fraction_truncates_to_two_digits_and_strips_zeros()
    {
        Assert.That(Format((BigNum)1_234), Is.EqualTo("1.23K"));     // 1.234 → 1.23 절사
        Assert.That(Format((BigNum)1_204), Is.EqualTo("1.2K"));      // 트레일링 0 제거
        Assert.That(Format((BigNum)1_004), Is.EqualTo("1K"));        // .00 제거
    }

    [Test]
    public void Beyond_unit_table_falls_back_to_scientific()
    {
        Assert.That(Format(BigNum.FromParts(123, 43)), Is.EqualTo("1.23e45"));
        Assert.That(Format(BigNum.FromParts(-123, 43)), Is.EqualTo("-1.23e45"));
        Assert.That(Format(BigNum.FromParts(1, 100)), Is.EqualTo("1e100"));
    }

    [Test]
    public void Custom_unit_override_is_respected()
    {
        // 스펙 §6: 단위 글자는 설정으로 오버라이드
        var custom = new BigNumFormat(3, new[] { "", "k", "m" });
        Assert.That(Format((BigNum)1_500, custom), Is.EqualTo("1.5k"));
        Assert.That(Format((BigNum)2_000_000, custom), Is.EqualTo("2m"));
        Assert.That(Format(BigNum.FromParts(3, 9), custom), Is.EqualTo("3e9"));   // 테이블 초과 → 폴백
    }

    [Test]
    public void Insufficient_destination_returns_false()
    {
        Span<char> tiny = stackalloc char[2];
        Assert.That(((BigNum)12_345).TryFormat(tiny, out _, null), Is.False);
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter BigNumFormatTests`
Expected: 컴파일 실패("BigNumFormat을 찾을 수 없음")

- [ ] **Step 3: 구현**

`gameplay/src/com.bun3.gameplay/Runtime/Numerics/BigNumFormat.cs`:

```csharp
using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// BigNum 표시 포맷 설정 — 단위 그룹 자릿수와 단위 문자 테이블. 게임은 자체 테이블로
    /// 인스턴스를 만들어 오버라이드한다(스펙 §6). 테이블을 넘는 값은 지수 표기로 폴백.
    /// </summary>
    public sealed class BigNumFormat
    {
        /// <summary>단위 하나가 감당하는 십진 자릿수(한국식 4, 알파벳식 3).</summary>
        public int GroupDigits { get; }

        /// <summary>단위 문자 테이블. [0]은 단위 없음("")이어야 한다.</summary>
        public string[] Units { get; }

        /// <summary>알파벳 축약(1K = 10^3): "", K, M, B, T, Qa, Qi.</summary>
        public static readonly BigNumFormat Alpha =
            new BigNumFormat(3, new[] { "", "K", "M", "B", "T", "Qa", "Qi" });

        /// <summary>한국식(1만 = 10^4): "", 만, 억, 조, 경, 해, 자, 양, 구, 간, 정, 재, 극.</summary>
        public static readonly BigNumFormat Korean =
            new BigNumFormat(4, new[] { "", "만", "억", "조", "경", "해", "자", "양", "구", "간", "정", "재", "극" });

        /// <summary>그룹 자릿수(1~9)와 단위 테이블로 포맷을 만든다.</summary>
        public BigNumFormat(int groupDigits, string[] units)
        {
            if (groupDigits < 1 || groupDigits > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(groupDigits));
            }

            if (units == null || units.Length == 0 || units[0].Length != 0)
            {
                throw new ArgumentException("Units[0]은 빈 문자열이어야 한다.", nameof(units));
            }

            GroupDigits = groupDigits;
            Units = units;
        }
    }
}
```

`BigNum.cs`에 추가 (ToString 앞):

```csharp
        /// <summary>
        /// 무할당 표시 포맷. 단위 테이블 안이면 "1.5만"/"3.45B" 형태(소수 최대 2자리,
        /// 트레일링 0 제거), 테이블을 넘으면 "1.23e45" 지수 표기. format이 null이면
        /// <see cref="BigNumFormat.Alpha"/>. 버퍼가 부족하면 false.
        /// </summary>
        public bool TryFormat(Span<char> destination, out int charsWritten, BigNumFormat? format = null)
        {
            format ??= BigNumFormat.Alpha;
            charsWritten = 0;

            if (IsZero)
            {
                return TryAppendChar(destination, ref charsWritten, '0');
            }

            var negative = Mantissa < 0;
            var absMantissa = (ulong)Math.Abs(Mantissa);
            var digitCount = CountDigits(absMantissa);
            var magnitude = (long)Exponent + digitCount - 1;   // 최상위 자리의 십진 지수

            if (negative && !TryAppendChar(destination, ref charsWritten, '-'))
            {
                return false;
            }

            // 1) 그룹 미만의 작은 값: 자릿수 그대로 (소수 포함, magnitude ≥ -2까지)
            if (magnitude < format.GroupDigits && Exponent >= -18 && magnitude >= -2)
            {
                return TryWritePlain(destination, ref charsWritten, absMantissa, Exponent);
            }

            // 2) 단위 테이블 범위: 선두부를 단위로 나눠 쓴다
            var unitIndex = magnitude >= 0 ? (int)(magnitude / format.GroupDigits) : -1;
            if (unitIndex >= 1 && unitIndex < format.Units.Length)
            {
                var integerDigits = (int)(magnitude - (long)unitIndex * format.GroupDigits) + 1;
                return TryWriteScaled(destination, ref charsWritten, absMantissa, digitCount,
                           integerDigits)
                       && TryAppendString(destination, ref charsWritten, format.Units[unitIndex]);
            }

            // 3) 폴백: 지수 표기 m.mm'e'EEE
            if (!TryWriteScaled(destination, ref charsWritten, absMantissa, digitCount, 1)
                || !TryAppendChar(destination, ref charsWritten, 'e'))
            {
                return false;
            }

            return TryAppendUInt(destination, ref charsWritten, (ulong)magnitude);
        }

        private static int CountDigits(ulong value)
        {
            var digits = 1;
            while (value >= 10)
            {
                value /= 10;
                digits++;
            }

            return digits;
        }

        // 정수/소수 그대로: mantissa × 10^exponent (exponent ≤ 0 구간 전용)
        private static bool TryWritePlain(
            Span<char> destination, ref int written, ulong mantissa, int exponent)
        {
            if (exponent >= 0)
            {
                // 정규형에서 이 경로의 exponent > 0은 mantissa에 0을 붙여 표기
                if (!TryAppendUInt(destination, ref written, mantissa))
                {
                    return false;
                }

                for (var i = 0; i < exponent; i++)
                {
                    if (!TryAppendChar(destination, ref written, '0'))
                    {
                        return false;
                    }
                }

                return true;
            }

            var fracDigits = -exponent;
            var divisor = 1UL;
            for (var i = 0; i < fracDigits; i++)
            {
                divisor *= 10;
            }

            var integerPart = mantissa / divisor;
            var fraction = mantissa % divisor;
            if (!TryAppendUInt(destination, ref written, integerPart)
                || !TryAppendChar(destination, ref written, '.'))
            {
                return false;
            }

            // 소수부: 선행 0 유지, 트레일링 0 제거
            while (fraction != 0 && fraction % 10 == 0)
            {
                fraction /= 10;
                fracDigits--;
            }

            Span<char> frac = stackalloc char[20];
            var f = fracDigits;
            for (var i = 0; i < fracDigits; i++)
            {
                frac[--f] = (char)('0' + (int)(fraction % 10));
                fraction /= 10;
            }

            for (var i = 0; i < fracDigits; i++)
            {
                if (!TryAppendChar(destination, ref written, frac[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // 가수의 선두 integerDigits 자리를 정수부로, 이어 최대 2자리 소수부(절사, 0 제거)
        private static bool TryWriteScaled(
            Span<char> destination, ref int written, ulong mantissa, int digitCount, int integerDigits)
        {
            // 정수부 자릿수가 가수 자릿수보다 많으면 0 패딩 (예: 가수 92, 정수부 3자리 → "920")
            if (integerDigits >= digitCount)
            {
                if (!TryAppendUInt(destination, ref written, mantissa))
                {
                    return false;
                }

                for (var i = 0; i < integerDigits - digitCount; i++)
                {
                    if (!TryAppendChar(destination, ref written, '0'))
                    {
                        return false;
                    }
                }

                return true;
            }

            // 정수부 뒤 소수 2자리까지만 남기고 절사
            var keep = integerDigits + 2;
            var drop = digitCount - keep;
            for (var i = 0; i < drop; i++)
            {
                mantissa /= 10;
            }

            var scale = 1UL;
            var fracLen = Math.Min(2, Math.Max(0, digitCount - integerDigits));
            for (var i = 0; i < fracLen; i++)
            {
                scale *= 10;
            }

            var integerPart = mantissa / scale;
            var fraction = mantissa % scale;

            while (fraction != 0 && fraction % 10 == 0)
            {
                fraction /= 10;
                fracLen--;
            }

            if (!TryAppendUInt(destination, ref written, integerPart))
            {
                return false;
            }

            if (fraction == 0)
            {
                return true;
            }

            if (!TryAppendChar(destination, ref written, '.'))
            {
                return false;
            }

            Span<char> frac = stackalloc char[4];
            var f = fracLen;
            for (var i = 0; i < fracLen; i++)
            {
                frac[--f] = (char)('0' + (int)(fraction % 10));
                fraction /= 10;
            }

            for (var i = 0; i < fracLen; i++)
            {
                if (!TryAppendChar(destination, ref written, frac[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryAppendChar(Span<char> destination, ref int written, char c)
        {
            if (written >= destination.Length)
            {
                return false;
            }

            destination[written++] = c;
            return true;
        }

        private static bool TryAppendString(Span<char> destination, ref int written, string s)
        {
            foreach (var c in s)
            {
                if (!TryAppendChar(destination, ref written, c))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryAppendUInt(Span<char> destination, ref int written, ulong value)
        {
            Span<char> digits = stackalloc char[20];
            var count = 0;
            do
            {
                digits[count++] = (char)('0' + (int)(value % 10));
                value /= 10;
            }
            while (value != 0);

            for (var i = count - 1; i >= 0; i--)
            {
                if (!TryAppendChar(destination, ref written, digits[i]))
                {
                    return false;
                }
            }

            return true;
        }
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter BigNumFormatTests`
Expected: 8건 통과. 실패 시 절사·자릿수 계산을 테스트 기대값 기준으로 수정(기대값이 계약이다)

- [ ] **Step 5: Commit**

```bash
git add gameplay
git commit -m "✨ BigNum.TryFormat: 무할당 단위 포맷 (한국식/알파벳/지수 폴백, 단위 오버라이드)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: TagRegistry + GameplayTag — 계층 등록·인터닝

**Files:**
- Create: `gameplay/src/com.bun3.gameplay/Runtime/Tags/GameplayTag.cs`
- Create: `gameplay/src/com.bun3.gameplay/Runtime/Tags/TagRegistry.cs`
- Test: `gameplay/tests/Bun3.Gameplay.Tests/TagRegistryTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces (Task 7과 이후 슬라이스가 사용):
  - `readonly struct GameplayTag : IEquatable<GameplayTag>` — `int Handle`, `static GameplayTag.None`, `bool IsValid`
  - `sealed class TagRegistry`
    - `GameplayTag GetOrRegister(string name)` — "A.B.C" 계층, 조상 자동 등록, 스레드 안전(쓰기 락)
    - `bool TryGet(string name, out GameplayTag tag)`
    - `string GetName(GameplayTag tag)` — 등록 시 인터닝된 문자열 반환(무할당)
    - `GameplayTag GetParent(GameplayTag tag)` — 루트면 None
    - `bool IsAncestorOrSelf(GameplayTag ancestor, GameplayTag tag)` — 계층 매칭 원어
  - 이름 규칙: Ordinal 비교(대소문자 구분), 빈 세그먼트/선행·후행 점 금지 → `ArgumentException`

- [ ] **Step 1: 실패하는 테스트 작성**

`gameplay/tests/Bun3.Gameplay.Tests/TagRegistryTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class TagRegistryTests
{
    [Test]
    public void Register_returns_stable_handle_and_interned_name()
    {
        var registry = new TagRegistry();
        var dead = registry.GetOrRegister("State.Dead");
        var again = registry.GetOrRegister("State.Dead");

        Assert.That(dead.IsValid, Is.True);
        Assert.That(again, Is.EqualTo(dead));
        Assert.That(registry.GetName(dead), Is.EqualTo("State.Dead"));
        // 인터닝 — 같은 참조가 반환된다(무할당 계약)
        Assert.That(ReferenceEquals(registry.GetName(dead), registry.GetName(again)), Is.True);
    }

    [Test]
    public void Ancestors_are_auto_registered()
    {
        var registry = new TagRegistry();
        var ghost = registry.GetOrRegister("State.Dead.Ghost");

        Assert.That(registry.TryGet("State.Dead", out var dead), Is.True);
        Assert.That(registry.TryGet("State", out var state), Is.True);
        Assert.That(registry.GetParent(ghost), Is.EqualTo(dead));
        Assert.That(registry.GetParent(dead), Is.EqualTo(state));
        Assert.That(registry.GetParent(state), Is.EqualTo(GameplayTag.None));
    }

    [Test]
    public void IsAncestorOrSelf_walks_hierarchy()
    {
        var registry = new TagRegistry();
        var ghost = registry.GetOrRegister("State.Dead.Ghost");
        var dead = registry.GetOrRegister("State.Dead");
        var state = registry.GetOrRegister("State");
        var rooted = registry.GetOrRegister("State.Rooted");

        Assert.That(registry.IsAncestorOrSelf(dead, ghost), Is.True);
        Assert.That(registry.IsAncestorOrSelf(state, ghost), Is.True);
        Assert.That(registry.IsAncestorOrSelf(ghost, ghost), Is.True);
        Assert.That(registry.IsAncestorOrSelf(ghost, dead), Is.False);   // 방향 확인
        Assert.That(registry.IsAncestorOrSelf(rooted, ghost), Is.False);
    }

    [Test]
    public void Unregistered_lookup_fails_but_GetOrRegister_registers_dynamically()
    {
        var registry = new TagRegistry();
        Assert.That(registry.TryGet("Never.Registered", out _), Is.False);

        // 스펙 §7: 미등록 태그는 동적 등록
        var tag = registry.GetOrRegister("Wire.Received.Later");
        Assert.That(tag.IsValid, Is.True);
        Assert.That(registry.TryGet("Wire.Received.Later", out var found), Is.True);
        Assert.That(found, Is.EqualTo(tag));
    }

    [TestCase("")]
    [TestCase(".")]
    [TestCase(".Leading")]
    [TestCase("Trailing.")]
    [TestCase("Double..Dot")]
    public void Invalid_names_throw(string name)
    {
        Assert.Throws<ArgumentException>(() => new TagRegistry().GetOrRegister(name));
    }

    [Test]
    public void Names_are_case_sensitive_ordinal()
    {
        var registry = new TagRegistry();
        var a = registry.GetOrRegister("State.Dead");
        var b = registry.GetOrRegister("state.dead");
        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public async Task Concurrent_registration_is_safe_and_consistent()
    {
        var registry = new TagRegistry();
        var tasks = new Task<GameplayTag>[16];
        for (var i = 0; i < tasks.Length; i++)
        {
            var n = i;
            tasks[i] = Task.Run(() => registry.GetOrRegister($"Load.Branch{n % 4}.Leaf{n}"));
        }

        var tags = await Task.WhenAll(tasks);
        foreach (var tag in tags)
        {
            Assert.That(tag.IsValid, Is.True);
            Assert.That(registry.TryGet(registry.GetName(tag), out var found), Is.True);
            Assert.That(found, Is.EqualTo(tag));
        }
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter TagRegistryTests`
Expected: 컴파일 실패("TagRegistry를 찾을 수 없음")

- [ ] **Step 3: 구현**

`gameplay/src/com.bun3.gameplay/Runtime/Tags/GameplayTag.cs`:

```csharp
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 계층 태그의 무할당 핸들. 정체성은 점 구분 계층 문자열("State.Dead.Ghost")이며,
    /// 등록한 <see cref="TagRegistry"/> 안에서만 유효하다 — 서로 다른 레지스트리의
    /// 핸들을 섞지 말 것(프로세스당 레지스트리 1개가 표준).
    /// </summary>
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        /// <summary>레지스트리 내부 핸들. 0 = None.</summary>
        public readonly int Handle;

        internal GameplayTag(int handle)
        {
            Handle = handle;
        }

        /// <summary>무효 태그(기본값).</summary>
        public static readonly GameplayTag None = default;

        /// <summary>등록된 태그인지 여부.</summary>
        public bool IsValid => Handle != 0;

        /// <summary>핸들 동등성.</summary>
        public bool Equals(GameplayTag other) => Handle == other.Handle;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Handle;

        /// <summary>동등 비교.</summary>
        public static bool operator ==(GameplayTag a, GameplayTag b) => a.Handle == b.Handle;

        /// <summary>비동등 비교.</summary>
        public static bool operator !=(GameplayTag a, GameplayTag b) => a.Handle != b.Handle;
    }
}
```

`gameplay/src/com.bun3.gameplay/Runtime/Tags/TagRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 계층 태그 레지스트리 — 이름("A.B.C")을 핸들로 인터닝한다. 조상 태그는 자동 등록.
    /// 등록(쓰기)은 락, 핸들 기반 조회(읽기)는 락 프리 — 심 핫패스는 등록된 핸들만 다룬다.
    /// 미등록 이름은 <see cref="GetOrRegister"/>가 동적으로 등록한다(스펙 §7).
    /// </summary>
    public sealed class TagRegistry
    {
        private readonly struct Entry
        {
            public readonly string Name;
            public readonly int Parent;

            public Entry(string name, int parent)
            {
                Name = name;
                Parent = parent;
            }
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _byName = new Dictionary<string, int>(StringComparer.Ordinal);
        private Entry[] _entries = new Entry[64];   // [0]은 None 자리로 비워둔다
        private int _count = 1;

        /// <summary>등록된 태그 수(None 제외).</summary>
        public int Count => Volatile.Read(ref _count) - 1;

        /// <summary>이름으로 태그를 얻는다. 미등록이면 조상 포함 등록한다. 스레드 안전.</summary>
        public GameplayTag GetOrRegister(string name)
        {
            Validate(name);
            lock (_gate)
            {
                return new GameplayTag(RegisterLocked(name));
            }
        }

        /// <summary>이름으로 등록된 태그를 찾는다. 등록하지 않는다.</summary>
        public bool TryGet(string name, out GameplayTag tag)
        {
            lock (_gate)
            {
                if (_byName.TryGetValue(name, out var handle))
                {
                    tag = new GameplayTag(handle);
                    return true;
                }
            }

            tag = GameplayTag.None;
            return false;
        }

        /// <summary>태그의 정식 이름. 등록 시 인터닝된 문자열이라 호출은 무할당이다.</summary>
        public string GetName(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                return string.Empty;
            }

            return Volatile.Read(ref _entries)[tag.Handle].Name;
        }

        /// <summary>부모 태그. 루트면 None.</summary>
        public GameplayTag GetParent(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                return GameplayTag.None;
            }

            return new GameplayTag(Volatile.Read(ref _entries)[tag.Handle].Parent);
        }

        /// <summary>ancestor가 tag 자신 또는 조상인지 — 계층 매칭의 원어. 무효 태그는 false.</summary>
        public bool IsAncestorOrSelf(GameplayTag ancestor, GameplayTag tag)
        {
            if (!ancestor.IsValid || !tag.IsValid)
            {
                return false;
            }

            var entries = Volatile.Read(ref _entries);
            var current = tag.Handle;
            while (current != 0)
            {
                if (current == ancestor.Handle)
                {
                    return true;
                }

                current = entries[current].Parent;
            }

            return false;
        }

        private int RegisterLocked(string name)
        {
            if (_byName.TryGetValue(name, out var existing))
            {
                return existing;
            }

            // 부모 먼저 (재귀 — 깊이는 태그 세그먼트 수)
            var parent = 0;
            var lastDot = name.LastIndexOf('.');
            if (lastDot > 0)
            {
                parent = RegisterLocked(name.Substring(0, lastDot));
            }

            var handle = _count;
            if (handle == _entries.Length)
            {
                var grown = new Entry[_entries.Length * 2];
                Array.Copy(_entries, grown, _entries.Length);
                grown[handle] = new Entry(name, parent);
                Volatile.Write(ref _entries, grown);   // 내용 기록 후 발행
            }
            else
            {
                _entries[handle] = new Entry(name, parent);
            }

            Volatile.Write(ref _count, handle + 1);
            _byName[name] = handle;
            return handle;
        }

        private static void Validate(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("태그 이름이 비어 있다.", nameof(name));
            }

            if (name[0] == '.' || name[name.Length - 1] == '.' || name.Contains(".."))
            {
                throw new ArgumentException($"잘못된 태그 이름: '{name}' (빈 세그먼트 금지)", nameof(name));
            }
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter TagRegistryTests`
Expected: 11건 통과 (TestCase 5건 포함)

- [ ] **Step 5: Commit**

```bash
git add gameplay
git commit -m "✨ GameplayTag/TagRegistry: 계층 태그 인터닝·조상 자동 등록·동적 등록" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: TagSet — 카운트 보유·계층 쿼리

**Files:**
- Create: `gameplay/src/com.bun3.gameplay/Runtime/Tags/TagSet.cs`
- Test: `gameplay/tests/Bun3.Gameplay.Tests/TagSetTests.cs`

**Interfaces:**
- Consumes: Task 6의 `GameplayTag`, `TagRegistry.IsAncestorOrSelf`
- Produces (이후 슬라이스의 Unit/EffectSpec 등이 사용):
  - `sealed class TagSet` (ctor: `TagSet(TagRegistry registry)`)
    - `void Add(GameplayTag tag, int count = 1)` / `bool Remove(GameplayTag tag, int count = 1)`
    - `bool HasExact(GameplayTag tag)` / `int ExactCount(GameplayTag tag)`
    - `bool Has(GameplayTag tag)` — 계층: 보유 태그 중 tag를 조상-혹은-자신으로 갖는 것 존재?
    - `int Count(GameplayTag tag)` — 계층 카운트 합산
    - `int KindCount { get; }` — 보유 태그 종류 수
  - 단일 스레드 전제(World 액터 소유) — 스레드 안전 비보장 문서화

- [ ] **Step 1: 실패하는 테스트 작성**

`gameplay/tests/Bun3.Gameplay.Tests/TagSetTests.cs`:

```csharp
using System;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class TagSetTests
{
    private TagRegistry _registry = null!;
    private TagSet _set = null!;
    private GameplayTag _dead;
    private GameplayTag _ghost;
    private GameplayTag _state;
    private GameplayTag _hasted;

    [SetUp]
    public void SetUp()
    {
        _registry = new TagRegistry();
        _set = new TagSet(_registry);
        _ghost = _registry.GetOrRegister("State.Dead.Ghost");
        _dead = _registry.GetOrRegister("State.Dead");
        _state = _registry.GetOrRegister("State");
        _hasted = _registry.GetOrRegister("Buff.Hasted");
    }

    [Test]
    public void Add_and_exact_queries()
    {
        _set.Add(_ghost);
        Assert.That(_set.HasExact(_ghost), Is.True);
        Assert.That(_set.HasExact(_dead), Is.False);   // 정확 일치 — 계층 아님
        Assert.That(_set.ExactCount(_ghost), Is.EqualTo(1));
        Assert.That(_set.KindCount, Is.EqualTo(1));
    }

    [Test]
    public void Hierarchical_has_matches_ancestors()
    {
        _set.Add(_ghost);
        Assert.That(_set.Has(_dead), Is.True);     // Ghost 보유 → "State.Dead" 매칭
        Assert.That(_set.Has(_state), Is.True);
        Assert.That(_set.Has(_ghost), Is.True);
        Assert.That(_set.Has(_hasted), Is.False);
    }

    [Test]
    public void Counted_semantics_two_sources_one_removal()
    {
        // 스펙 §7: 같은 태그를 부여하는 소스 2개 — 하나 꺼져도 태그 유지
        _set.Add(_hasted);
        _set.Add(_hasted);
        Assert.That(_set.ExactCount(_hasted), Is.EqualTo(2));

        Assert.That(_set.Remove(_hasted), Is.True);
        Assert.That(_set.HasExact(_hasted), Is.True);
        Assert.That(_set.ExactCount(_hasted), Is.EqualTo(1));

        Assert.That(_set.Remove(_hasted), Is.True);
        Assert.That(_set.HasExact(_hasted), Is.False);
        Assert.That(_set.KindCount, Is.EqualTo(0));
    }

    [Test]
    public void Hierarchical_count_sums_descendants()
    {
        _set.Add(_ghost, 2);
        _set.Add(_dead);
        Assert.That(_set.Count(_dead), Is.EqualTo(3));    // Dead(1) + Ghost(2)
        Assert.That(_set.Count(_state), Is.EqualTo(3));
        Assert.That(_set.Count(_ghost), Is.EqualTo(2));
        Assert.That(_set.ExactCount(_dead), Is.EqualTo(1));
    }

    [Test]
    public void Remove_missing_returns_false()
    {
        Assert.That(_set.Remove(_hasted), Is.False);
        _set.Add(_hasted);
        Assert.That(_set.Remove(_hasted, 5), Is.True);    // 초과 제거 = 0으로
        Assert.That(_set.HasExact(_hasted), Is.False);
    }

    [Test]
    public void Invalid_tag_and_nonpositive_count_throw()
    {
        Assert.Throws<ArgumentException>(() => _set.Add(GameplayTag.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => _set.Add(_hasted, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _set.Remove(_hasted, -1));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter TagSetTests`
Expected: 컴파일 실패("TagSet을 찾을 수 없음")

- [ ] **Step 3: 구현**

`gameplay/src/com.bun3.gameplay/Runtime/Tags/TagSet.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// 카운트를 갖는 태그 집합 — 같은 태그를 부여하는 소스 여럿이 공존한다(하나가 꺼져도
    /// 카운트만 줄어든다). 계층 쿼리(Has/Count)는 보유 태그의 조상 체인을 걷는다.
    /// 단일 스레드(월드 액터) 소유 전제 — 스레드 안전을 보장하지 않는다.
    /// </summary>
    public sealed class TagSet
    {
        private readonly TagRegistry _registry;
        private readonly Dictionary<int, int> _counts = new Dictionary<int, int>();

        /// <summary>레지스트리에 바인딩된 빈 집합을 만든다.</summary>
        public TagSet(TagRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>보유 태그 종류 수(카운트 무관).</summary>
        public int KindCount => _counts.Count;

        /// <summary>태그를 count만큼 추가한다.</summary>
        public void Add(GameplayTag tag, int count = 1)
        {
            RequireValid(tag);
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _counts.TryGetValue(tag.Handle, out var current);
            _counts[tag.Handle] = current + count;
        }

        /// <summary>태그를 count만큼 제거한다. 0 이하가 되면 집합에서 빠진다.
        /// 애초에 없었으면 false.</summary>
        public bool Remove(GameplayTag tag, int count = 1)
        {
            RequireValid(tag);
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (!_counts.TryGetValue(tag.Handle, out var current))
            {
                return false;
            }

            var next = current - count;
            if (next > 0)
            {
                _counts[tag.Handle] = next;
            }
            else
            {
                _counts.Remove(tag.Handle);
            }

            return true;
        }

        /// <summary>정확히 이 태그를 보유하는지(계층 아님).</summary>
        public bool HasExact(GameplayTag tag) =>
            tag.IsValid && _counts.ContainsKey(tag.Handle);

        /// <summary>정확히 이 태그의 카운트. 없으면 0.</summary>
        public int ExactCount(GameplayTag tag) =>
            tag.IsValid && _counts.TryGetValue(tag.Handle, out var count) ? count : 0;

        /// <summary>계층 매칭: 보유 태그 중 tag 자신이거나 그 하위인 것이 있는지.
        /// 예) "State.Dead.Ghost" 보유 시 Has("State.Dead") == true.</summary>
        public bool Has(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                return false;
            }

            foreach (var pair in _counts)
            {
                if (_registry.IsAncestorOrSelf(tag, new GameplayTag(pair.Key)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>계층 카운트: tag 자신·하위 태그들의 카운트 합.</summary>
        public int Count(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                return 0;
            }

            var total = 0;
            foreach (var pair in _counts)
            {
                if (_registry.IsAncestorOrSelf(tag, new GameplayTag(pair.Key)))
                {
                    total += pair.Value;
                }
            }

            return total;
        }

        private static void RequireValid(GameplayTag tag)
        {
            if (!tag.IsValid)
            {
                throw new ArgumentException("무효 태그(None)는 집합에 넣을 수 없다.", nameof(tag));
            }
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo --filter TagSetTests`
Expected: 6건 통과

- [ ] **Step 5: Commit**

```bash
git add gameplay
git commit -m "✨ TagSet: 카운트 보유 태그 집합 + 계층 Has/Count 쿼리" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: 마무리 — 전체 검증·무할당 스모크

**Files:**
- Create: `gameplay/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs`
- Delete: `gameplay/tests/Bun3.Gameplay.Tests/SmokeTests.cs` (역할 종료)

**Interfaces:**
- Consumes: 슬라이스 1 전체
- Produces: 무할당 계약의 회귀 가드

- [ ] **Step 1: 무할당 스모크 테스트 작성**

`gameplay/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs`:

```csharp
using System;
using Bun3.Gameplay.Numerics;
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

        // 워밍업 (JIT/정적 초기화 할당 배제)
        var warm = a * b + a - b / 3;
        warm.TryFormat(buffer, out _, BigNumFormat.Korean);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            var x = a * b + a - b / 3;
            x.TryFormat(buffer, out _, BigNumFormat.Korean);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, "BigNum 연산/포맷 경로에서 힙 할당 발생");
    }

    [Test]
    public void Tag_queries_do_not_allocate()
    {
        var registry = new TagRegistry();
        var set = new TagSet(registry);
        var ghost = registry.GetOrRegister("State.Dead.Ghost");
        var dead = registry.GetOrRegister("State.Dead");
        set.Add(ghost, 2);

        // 워밍업
        _ = set.Has(dead) && set.Count(dead) > 0 && registry.GetName(ghost).Length > 0;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
        {
            _ = set.Has(dead);
            _ = set.Count(dead);
            _ = set.HasExact(ghost);
            _ = registry.GetName(ghost);
            _ = registry.IsAncestorOrSelf(dead, ghost);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, "태그 쿼리 경로에서 힙 할당 발생");
    }
}
```

- [ ] **Step 2: SmokeTests.cs 삭제 후 전체 실행**

Run: `dotnet test gameplay/tests/Bun3.Gameplay.Tests --nologo`
Expected: 전체 통과(약 49건), 경고 0

- [ ] **Step 3: 솔루션 전체 회귀 확인**

Run: `dotnet build Bun3.sln -v q` — Expected: 경고 0, 오류 0
Run: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --no-build --nologo` — Expected: 기존 178건 통과 유지

- [ ] **Step 4: Commit**

```bash
git add gameplay
git commit -m "✅ 슬라이스 1 마무리: 무할당 스모크 가드 + 전체 회귀 확인" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## 후속 메모 (이 플랜 범위 밖)

- Unity `.meta` 생성·커밋: Unity 프로젝트에서 com.bun3.gameplay를 열 때 (pooled-collections 후속과 동일 트랙)
- 슬라이스 2 플랜(Attribute + Effect)이 이 플랜의 산출물(BigNum, TagSet)을 소비한다 — 슬라이스 1 완료 후 작성
- BigNum 성능이 병목으로 측정되면 Int128Math.DivRem을 Knuth D로 교체(주석에 명시됨)
