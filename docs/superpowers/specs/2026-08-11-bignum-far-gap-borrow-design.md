# BigNum 반대 부호 원거리 Borrow 보완 설계

> 상태: 승인
> 작성일: 2026-08-11
> 선행 설계: `2026-08-11-bignum-fixed64-final-review-remediation-design.md`

## 문제

현재 덧셈은 두 절댓값의 decimal magnitude 차이가 19자리 이상이면 작은 피연산자가 보존 정밀도 밖이라고 판단해 큰 피연산자를 그대로 반환한다. 같은 부호에서는 작은 값이 최종 19자리에 영향을 주지 않지만, 반대 부호에서는 낮은 자리의 뺄셈이 연속된 0 전체에 borrow를 전파할 수 있다.

```text
1e19 + (-1)
= 9999999999999999999
→ BigNum 가수 범위로 한 번 축약
= 999999999999999999e1
```

현재 결과는 작은 `-1`을 계산 전에 무시한 `1e19`이므로 정확하지 않다. 연산은 런타임마다 동일하게 틀리기 때문에 즉시 lockstep desync를 만들지는 않지만, 게임 상태의 수치 결과가 잘못된다.

## 결정

정렬 가능한 범위의 기존 unsigned 128-bit exact intermediate는 유지한다. magnitude 차이가 보존 범위를 넘는 경우에는 부호에 따라 경로를 나눈다.

- 같은 부호: 작은 피연산자는 최종 유효 숫자에 영향을 주지 않으므로 큰 피연산자를 그대로 반환한다.
- 반대 부호: 작은 피연산자가 결과 부호를 바꿀 수는 없지만 borrow를 만들 수 있으므로 큰 피연산자의 상위 decimal window를 계산하고 sticky borrow 1을 반영한다.
- runtime hot path에 `BigInteger`, 문자열, 배열 또는 heap allocation을 사용하지 않는다.

## Far-gap sticky-borrow 알고리즘

`larger`는 절댓값이 큰 operand, `smaller`는 0이 아닌 작은 operand이며 두 부호는 반대라고 가정한다. 이 경로는 `magnitudeGap > ScaleDigits`일 때만 사용한다.

1. `retainedExponent = largerMagnitude - ScaleDigits`로 상위 19자리 window의 지수를 구한다.
2. `decimalShift = larger.Exponent - retainedExponent`를 구한다. canonical mantissa의 자릿수가 1~19자리이므로 이 값은 0~18 범위다.
3. `retainedMantissa = abs(larger.Mantissa) * 10^decimalShift`를 unsigned 64-bit로 계산한다.
4. `retainedMantissa > long.MaxValue`이면 10으로 한 번 나누고 `retainedExponent`를 1 증가시킨다. 이 단계 후 가수는 항상 18자리 이하 또는 `long.MaxValue` 이하다.
5. `smaller`의 magnitude는 `retainedExponent`보다 낮고 0이 아니며, `larger`는 `10^retainedExponent`의 정확한 배수다. 따라서 절사 window 아래에서 반드시 borrow가 발생한다. `retainedMantissa`를 정확히 1 감소시킨다.
6. 큰 operand의 부호를 붙이고 `Canonicalize`를 호출한다.

private helper의 계약은 다음과 같다.

```csharp
private static BigNum SubtractFarMagnitude(BigNum larger);
```

helper가 `smaller`를 인자로 받지 않는 이유는 진입 조건이 이미 “반대 부호, smaller nonzero, smaller magnitude < retained window”를 보장하며, 이 구간에서 작은 값의 구체적인 숫자는 sticky borrow 1 이외에 상위 window에 영향을 주지 않기 때문이다.

## 경계 사례

다음 literal을 반드시 고정한다.

```text
1e19 - 1                = 999999999999999999e1
1e20 - 1                = 999999999999999999e2
999e20 - 1              = 998999999999999999e5
1eMaxExponent - 1       = 999999999999999999e(MaxExponent-18)
-1e20 + 1               = -999999999999999999e2
1e20 + 1                = 1e20  // 같은 부호의 작은 값은 기존 정책대로 무시
```

`999e20 - 1`은 초기 19자리 window가 `long.MaxValue`를 넘을 때 18자리로 한 번 더 축약한 뒤 borrow가 적용되는 경계를 검증한다.

추가 property test는 다음을 포함한다.

- magnitude gap 19, 20, 100, 최대 지수 근처
- 양수 큰 값/음수 작은 값과 그 전체 부호 대칭
- 같은 부호 원거리 연산의 기존 조기 반환
- 정렬 가능한 gap 18 경로가 계속 exact UInt128 계산을 사용하는지
- BigInteger oracle과 literal 결과 일치

## 문서와 버전

public API 시그니처는 바뀌지 않는다. `operator +`의 19자리/0 방향 절사 XML 계약을 실제 구현과 일치하도록 보완한다.

Gameplay runtime 내용이 다시 바뀌므로 같은 `0.3.0`으로 재발행하지 않고 .NET/UPM 버전을 `0.4.0`으로 올린다. Common은 변경하지 않고 `0.4.0`을 유지한다. Unity의 local package lock은 package version이 아니라 `file:../../common/src/com.bun3.gameplay` 경로를 기록하므로 lock에 0.4.0을 강제로 쓰지 않는다. 대신 package.json 0.4.0과 실제 Unity import/smoke로 버전을 검증한다.

## 검증

- 새 literal/property tests를 먼저 추가해 현재 조기 반환이 RED임을 확인한다.
- 최소 sticky-borrow helper를 구현하고 focused BigNum tests를 GREEN으로 만든다.
- BigNum allocation smoke가 계속 0인지 확인한다.
- Gameplay 전체 및 solution 전체 test를 clean build 뒤 실행한다.
- Gameplay `0.4.0` nupkg와 UPM metadata를 확인하고 Unity local lock 경로가 유지되는지 확인한다.
- Unity Gameplay smoke에 `1e19 - 1` literal을 추가하고 전체 EditMode를 실행한다.
- build 경고·오류 0, `git diff --check`, clean working tree를 확인한다.

## 비목표

- BigNum 정밀도 확대
- arbitrary precision runtime 연산
- Fixed64/Common 재변경
- 같은 부호 원거리 작은 피연산자 보존
- 기존 256자 출력 예산 또는 패키지 구조 변경
