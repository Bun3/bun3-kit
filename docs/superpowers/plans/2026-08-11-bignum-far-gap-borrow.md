# BigNum Far-Gap Borrow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 반대 부호 BigNum의 magnitude 차이가 19자리 이상일 때 작은 피연산자를 무시하지 않고 무할당 sticky borrow로 정확한 상위 유효 숫자를 만든다.

**Architecture:** 정렬 가능한 gap 18 이하는 기존 unsigned 128-bit exact path를 유지한다. 더 먼 gap에서는 같은 부호만 기존 조기 반환하고, 반대 부호는 큰 operand의 상위 19자리 decimal window를 계산한 뒤 sticky borrow 1을 반영한다. runtime에는 BigInteger, 문자열, 배열을 추가하지 않는다.

**Tech Stack:** C# 9, netstandard2.1, NUnit 4, Unity Test Framework 1.6, Unity 6000.3.14f1

## Global Constraints

- BigNum public 가수 범위는 `[-long.MaxValue, long.MaxValue]`다.
- 결과는 exact decimal result를 최대 19자리 및 `long.MaxValue` 범위까지 0 방향으로 한 번 축약한 값과 같아야 한다.
- addition/CompareTo/allocation hot path에는 heap allocation을 추가하지 않는다.
- 같은 부호 far-gap의 작은 피연산자는 기존 정밀도 정책대로 무시한다.
- 반대 부호 far-gap은 모든 exponent gap에서 borrow를 반영한다.
- Gameplay .NET/UPM 버전은 `0.4.0`, Unity 최소 버전은 `2022.3`이다.
- 모든 build 결과는 경고 0/오류 0이고 public XML은 한국어다.
- 커밋은 gitmoji와 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` trailer를 사용한다.

## File Structure

- Modify `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs`: sign-aware far-gap dispatch와 `SubtractFarMagnitude` helper.
- Modify `common/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs`: gap 19/20/100/MaxExponent literal 및 BigInteger oracle.
- Modify `common/src/com.bun3.gameplay/Tests/Editor/GameplayUnitySmokeTests.cs`: Unity far-gap literal smoke.
- Modify `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`: version `0.4.0`.
- Modify `common/src/com.bun3.gameplay/package.json`: version `0.4.0`.
- Verify `unity/Packages/packages-lock.json`: local Gameplay file dependency 경로 유지. Resolver가 실제 내용을 바꾼 경우에만 생성 결과를 커밋.

---

### Task 1: 반대 부호 원거리 Sticky Borrow

**Interfaces:**

- Consumes: `BigNum.operator +`, `CountDigits64`, `Pow10`, `Canonicalize`, `ScaleDigits`.
- Produces: private `SubtractFarMagnitude(BigNum larger)`.
- Preserves: 모든 public signature, 정렬 가능한 UInt128 경로, 같은 부호 far-gap 조기 반환.

- [ ] **Step 1: far-gap borrow RED를 작성한다**

`BigNumBasicTests.cs`에 literal tests를 추가한다.

```csharp
[TestCase(19, 999_999_999_999_999_999L, 1)]
[TestCase(20, 999_999_999_999_999_999L, 2)]
[TestCase(100, 999_999_999_999_999_999L, 82)]
public void Opposite_sign_far_gap_propagates_borrow(
    int exponent, long expectedMantissa, int expectedExponent)
{
    var actual = BigNum.FromParts(1, exponent) + (BigNum)(-1);
    Assert.That(actual, Is.EqualTo(BigNum.FromParts(expectedMantissa, expectedExponent)));
}

[Test]
public void Opposite_sign_far_gap_handles_nineteen_digit_window_over_long_max()
{
    var actual = BigNum.FromParts(999, 20) + (BigNum)(-1);
    Assert.That(actual,
        Is.EqualTo(BigNum.FromParts(998_999_999_999_999_999L, 5)));
}

[Test]
public void Opposite_sign_far_gap_is_sign_symmetric()
{
    var positive = BigNum.FromParts(1, 20) + (BigNum)(-1);
    var negative = BigNum.FromParts(-1, 20) + (BigNum)1;
    Assert.That(negative, Is.EqualTo(-positive));
}

[Test]
public void Opposite_sign_far_gap_handles_max_exponent()
{
    var actual = BigNum.FromParts(1, BigNum.MaxExponent) + (BigNum)(-1);
    Assert.That(actual, Is.EqualTo(BigNum.FromParts(
        999_999_999_999_999_999L, BigNum.MaxExponent - 18)));
}

[Test]
public void Same_sign_far_gap_still_ignores_out_of_precision_operand()
{
    Assert.That(
        BigNum.FromParts(1, 20) + (BigNum)1,
        Is.EqualTo(BigNum.FromParts(1, 20)));
}
```

기존 BigInteger oracle table에 반대 부호 gap 19, 20, 100과 양·음 대칭을 추가한다. production helper를 oracle에서 호출하지 않는다.

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumBasicTests"
```

Expected RED: 반대 부호 far-gap tests는 현재 큰 operand를 그대로 반환해 실패하고, 같은 부호 test는 통과한다.

- [ ] **Step 2: sign-aware far-gap dispatch를 구현한다**

`operator +`의 두 조기 반환을 다음 의미로 교체한다.

```csharp
if (magnitudeDifference > ScaleDigits)
{
    return aNegative == bNegative ? a : SubtractFarMagnitude(a);
}

if (magnitudeDifference < -ScaleDigits)
{
    return aNegative == bNegative ? b : SubtractFarMagnitude(b);
}
```

`aDecimalMagnitude > bDecimalMagnitude`이면 절댓값도 반드시 `a > b`이고, 반대 방향도 동일하므로 result sign은 larger의 sign이다.

- [ ] **Step 3: 무할당 sticky-borrow helper를 구현한다**

`BigNum.cs`에 다음 helper를 추가한다.

```csharp
private static BigNum SubtractFarMagnitude(BigNum larger)
{
    var negative = larger.Mantissa < 0;
    var magnitude = (ulong)(negative ? -larger.Mantissa : larger.Mantissa);
    var digitCount = CountDigits64(magnitude);

    long retainedExponent = (long)larger.Exponent + digitCount - MantissaMaxDigits;
    var decimalShift = (int)((long)larger.Exponent - retainedExponent);
    var retainedMantissa = magnitude * Pow10[decimalShift];

    if (retainedMantissa > long.MaxValue)
    {
        retainedMantissa /= 10;
        retainedExponent++;
    }

    retainedMantissa--; // nonzero smaller operand below the retained window borrows exactly one
    var signedMantissa = negative ? -(long)retainedMantissa : (long)retainedMantissa;
    return Canonicalize(signedMantissa, retainedExponent);
}
```

`decimalShift`는 `19 - digitCount`, 즉 0~18이다. `retainedMantissa`는 10^19 미만이므로 `ulong`에 안전하다. initial 19-digit window가 `long.MaxValue`를 넘는 경우 10으로 한 번 나누면 반드시 범위 안이며, retained mantissa는 최소 10^17 이상이므로 decrement underflow가 없다.

`operator +` XML을 “보존 범위 밖 같은 부호 피연산자는 무시하고, 반대 부호는 borrow를 반영한 뒤 0 방향으로 절사한다”는 한국어 계약으로 보완한다.

- [ ] **Step 4: focused GREEN과 mutation check를 수행한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumBasicTests"
dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~AllocationSmokeTests"
```

Expected: 모두 PASS, allocation 0. `retainedMantissa--`를 일시적으로 제거했을 때 `1e19-1` literal이 RED가 되는지 확인하고 즉시 원복해 GREEN을 재확인한다.

- [ ] **Step 5: Gameplay version과 Unity smoke를 갱신한다**

- `Bun3.Gameplay.csproj`: `<Version>0.4.0</Version>`
- `package.json`: `"version": "0.4.0"`
- `GameplayUnitySmokeTests.cs`: 다음 assertion 추가

```csharp
Assert.That(
    BigNum.FromParts(1, 19) + (BigNum)(-1),
    Is.EqualTo(BigNum.FromParts(999_999_999_999_999_999L, 1)));
```

Unity 6000.3.14f1 resolver/import를 실행한다. local package lock의 `version`은 package semantic version이 아니라 `file:../../common/src/com.bun3.gameplay` 경로를 유지해야 한다. 실제 semantic version은 gameplay `package.json`의 `0.4.0`과 Unity import/smoke 성공으로 검증한다. 새 `.meta`는 없어야 하며 계획 밖 `ProjectSettings.asset` 변경은 diff에서 제외한다. Resolver가 lock 내용을 실제로 바꾼 경우에만 Unity 생성 결과를 커밋한다.

- [ ] **Step 6: fresh 전체 검증을 수행한다**

```powershell
dotnet clean Bun3.sln --nologo
dotnet build Bun3.sln --nologo
dotnet test Bun3.sln --no-build --nologo
dotnet pack common/src/com.bun3.gameplay/Bun3.Gameplay.csproj -c Release --nologo
git diff --check
```

Expected: build warning/error 0, .NET 전체 PASS, Gameplay nupkg `0.4.0`, allocation 0.

Unity EditMode는 `-quit` 없이 실행해 XML과 `Run completed`를 모두 확인한다.

```powershell
$resultPath = Join-Path $env:TEMP 'bun3-far-gap-borrow-editmode.xml'
$logPath = Join-Path $env:TEMP 'bun3-far-gap-borrow-editmode.log'
& 'E:\Unitys\6000.3.14f1\Editor\Unity.exe' -batchmode `
    -projectPath (Resolve-Path 'unity') `
    -runTests -testPlatform EditMode `
    -testResults $resultPath -logFile $logPath
```

Expected: Gameplay smoke의 far-gap borrow를 포함한 전체 EditMode PASS.

- [ ] **Step 7: self-review하고 commit한다**

확인:

- 같은 부호 far-gap 조기 반환이 유지되는가.
- 반대 부호 gap 19부터 MaxExponent까지 borrow가 적용되는가.
- 정렬 가능한 gap 18 UInt128 경로가 바뀌지 않았는가.
- helper가 allocation과 BigInteger를 사용하지 않는가.
- Gameplay .NET/UPM이 모두 0.4.0이고 Unity local lock 경로가 유지되는가.
- 계획 밖 파일 변경이 없고 `git diff --check`가 통과하는가.

Commit:

```powershell
git add -- common/src/com.bun3.gameplay common/tests/Bun3.Gameplay.Tests
git commit -m "🐛 BigNum 원거리 borrow를 결정론적으로 반영" `
    -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

보고서에는 root cause, RED/GREEN/mutation 증거, .NET/Unity/package 검증, 변경 파일, self-review, 우려사항을 기록한다.
