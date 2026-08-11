# BigNum·Fixed64 최종 리뷰 보완 설계

> 상태: 검토 요청
> 작성일: 2026-08-11
> 기준: `2026-08-10-bignum-fixed64-determinism-design.md` 및 최종 whole-branch 리뷰

## 목표

최종 브랜치 리뷰에서 확인된 BigNum 산술·비교·해시·표시 경계와 Fixed64 패키지 고정·골든 벡터·Unity 패키징 결함을 해결한다. 결정론적 락스텝의 상태 표현은 계속 BigNum의 정규 가수/지수 또는 Fixed64의 signed Raw `long`만 사용한다.

이번 보완은 새로운 수치 타입이나 래퍼를 추가하지 않는다. 기존 `Bun3.Gameplay.Numerics.BigNum`과 `FixedMathSharp.Fixed64`의 계약을 완성하고, 두 런타임에서 그 계약을 실제로 컴파일·검증하는 데 한정한다.

## 1. BigNum 덧셈과 뺄셈

현재 구현은 피연산자를 먼저 절사한 후 `long`으로 더한다. 이 때문에 유효한 두 음수의 합이 내부적으로 `long.MinValue`가 되면 예외가 발생하고, carry 및 근접 상쇄에서 보존 가능한 유효 숫자를 잃는다.

보완 구현은 다음 규칙을 따른다.

- 0과의 연산은 기존 빠른 경로를 유지한다.
- 두 피연산자의 부호, 절댓값 자릿수, 십진 magnitude를 먼저 계산한다.
- 두 값의 magnitude 차이가 보존 정밀도보다 커 작은 피연산자가 결과의 19자리 안에 영향을 줄 수 없으면 큰 피연산자를 그대로 반환한다.
- 그 외에는 더 작은 지수에 맞춰 두 절댓값을 unsigned 128-bit 부호-크기 값으로 정확히 정렬한다.
- 같은 부호는 128-bit 덧셈, 다른 부호는 magnitude 비교 후 128-bit 뺄셈을 수행한다.
- 정확한 중간 결과에 대해서만 한 번 `ReduceToLong`을 적용하고 마지막에 부호와 `Canonicalize`를 적용한다.
- 내부 계산은 `long.MinValue`를 만들지 않는다. public 가수 범위 `[-long.MaxValue, long.MaxValue]`는 그대로 유지한다.
- hot path는 정수 연산과 stack/local 값만 사용하며 heap allocation을 추가하지 않는다.

독립 literal 기대값으로 다음 회귀를 고정한다.

- `(-long.MaxValue) + (-1)`이 예외 없이 정규화된다.
- `long.MaxValue + long.MaxValue`는 정확한 합을 한 번 절사한 결과를 낸다.
- 반대 부호의 근접 상쇄가 정확히 표현 가능한 경계값을 보존한다.
- carry, exponent 차이 임계점, 양·음 대칭을 BigInteger 오라클과 literal 사례로 검증한다.

## 2. 비교와 해시

`CompareTo`는 더 이상 뺄셈을 호출하지 않는다.

- 부호가 다르면 부호만으로 판정한다.
- 같은 부호에서는 십진 magnitude를 비교한다.
- magnitude가 같으면 128-bit 정렬 가수를 비교한다.
- 음수끼리는 magnitude 비교 결과를 반전한다.
- `MinValue`, `MaxValue`, 0, 반대 부호, 같은 magnitude의 다른 가수/지수 조합에서 total ordering을 보장한다.

`GetHashCode`는 프로세스별 seed를 사용하는 `HashCode.Combine`을 제거한다. Mantissa의 하위/상위 32비트와 Exponent를 고정 FNV-1a 32-bit 순서로 혼합한다. overflow는 명시적인 `unchecked` 산술이며, 별도 프로세스와 런타임에서도 같은 literal golden hash를 반환해야 한다.

## 3. 안전한 문자열 표시

caller-owned `TryFormat`은 bounded API이므로 현재 계약을 유지한다. 편의 API `ToDisplayString`만 무제한 배열 성장으로부터 보호한다.

- 기존 `ToDisplayString(BigNumFormat? format = null)` 시그니처는 바이너리 호환성을 위해 유지하며 기본 출력 예산 256자를 적용한다.
- 새 overload `ToDisplayString(BigNumFormat? format, int maxLength)`를 추가해 호출자가 더 큰 출력 예산을 명시할 수 있게 한다.
- `maxLength`가 1 미만이면 `ArgumentOutOfRangeException`을 던진다.
- 구현은 128자 stack buffer를 먼저 사용한다. 부족하면 최대 `maxLength` 크기의 배열을 한 번만 만들고, 여전히 부족하면 `InvalidOperationException`을 던진다. 무제한 성장 루프는 제거한다.
- 예외 메시지와 XML 문서는 `BigNumOverflowStyle.Scientific`, 더 큰 `maxLength`, 또는 caller-owned `TryFormat` 사용을 안내한다.
- 기본 호출은 어떤 입력에서도 256자를 넘는 임시 출력 배열을 만들지 않는다.
- `MaxValue`의 기본 `TopUnit` 표시는 256자 이내에서 안전하게 실패해야 하며, `Scientific` 포맷은 정상 출력되어야 한다.

디버그용 `ToString()`은 `InvariantCulture`를 명시한다. `long.MinValue`를 거부하는 public 변환과 `FromParts`에는 한국어 `<exception>` 문서를 추가한다.

## 4. FixedMathSharp 의존성 및 골든 벡터

.NET 의존성은 NuGet exact range인 `[7.0.0]`으로 고정한다. UPM은 기존 공식 v7.0.0 tag와 revision `168b6f4f2a7dcf4164aab93db81754bae737de40`을 유지한다.

패키지 검증은 다음을 포함한다.

- 생성된 `Bun3.Common` nupkg의 nuspec에 `[7.0.0]` exact dependency가 기록되는지 확인한다.
- restore 결과가 7.0.0인지 확인한다.
- NuGet과 UPM FixedMathSharp 본체가 같은 upstream 계열임을 기존 증거와 함께 유지한다.

공유 conformance 테스트는 구현에서 계산한 기대값을 재사용하지 않고, 독립적인 고정 literal을 사용한다. 각 literal에는 수학식 또는 외부 고정밀 기준으로 산출했다는 짧은 provenance 주석을 둔다.

- 양수·음수 덧셈, 뺄셈, 곱셈, 나눗셈
- 음수 midpoint와 ties-to-even
- `MaxValue + One`, `MinValue - One`을 포함한 add/sub saturation
- 중간 곱셈·나눗셈 overflow 경계
- 0이 아닌 각도의 `Sin`/`Cos`와 비제곱 입력의 `Sqrt`
- `(3,4)` 벡터 정규화
- signed negative Raw long의 little-endian byte 순서
- 실제 fractional delta를 사용하는 600 tick 누적 golden

같은 소스가 .NET NUnit과 Unity EditMode에서 모두 실행되어야 한다.

## 5. Gameplay UPM 실제 연결

`com.bun3.gameplay`은 C# 9/netstandard2.1을 사용하므로 Unity 최소 버전을 Common과 동일한 `2022.3`으로 올린다.

- Unity 샘플 manifest에 `com.bun3.gameplay` 로컬 file dependency를 추가한다.
- Unity가 gameplay 패키지의 폴더, asmdef, C# 파일, package.json에 필요한 `.meta`를 실제로 생성하게 하고 이를 커밋한다.
- Unity 테스트 assembly에서 `Bun3.Gameplay`을 참조하는 smoke test를 추가해 BigNum 생성, 덧셈, 비교, 결정론 해시, scientific 표시를 실제 Unity 컴파일러와 런타임에서 확인한다.
- 최종 Unity EditMode 결과는 Common Fixed64 conformance와 Gameplay smoke를 모두 포함해야 한다.
- Mono/IL2CPP Player build smoke는 중복 번들 DLL 호환성 감시 항목으로 문서화하되 이번 보완의 merge gate에는 포함하지 않는다.

## 6. TagSet 카운트 경계

`TagSet`의 반복 `Add` 및 계층 카운트 합산은 `checked` 문맥을 사용한다. `int.MaxValue`를 넘으면 음수로 wrap하지 않고 `OverflowException`으로 fail-fast한다. 경계 테스트는 최대값까지의 유효 상태와 다음 증가의 예외를 검증한다.

## 7. 버전 및 호환성

이미 게시 후보로 커밋된 버전과 같은 버전으로 재발행하지 않는다.

- `Bun3.Gameplay`: `0.2.0` → `0.3.0`
- `Bun3.Common`: `0.3.0` → `0.4.0`
- Common UPM의 FixedMathSharp dependency는 계속 `7.0.0`이다.
- Gameplay/Common의 .NET과 UPM 메타데이터 버전은 각각 일치해야 한다.

BigNum의 잘못된 산술 결과를 수정하는 것은 호환성보다 정확성·결정론을 우선한다. public 타입명과 핵심 생성 API는 유지한다.

## 8. 검증 및 완료 조건

모든 동작 수정은 TDD로 진행한다. 각 결함마다 구현 전 RED와 구현 후 GREEN을 보고서에 남긴다.

- BigNum 집중 테스트: 음수 경계, carry, 상쇄, 비교, hash golden, 표시 상한
- TagSet overflow 테스트
- Fixed64 공유 conformance의 확장된 literal golden을 .NET과 Unity에서 모두 실행
- Gameplay Unity smoke test
- `dotnet clean`, `dotnet build` 경고/오류 0, 전체 `dotnet test --no-build`
- `dotnet pack` 후 nuspec exact dependency와 새 패키지 버전 확인
- Unity 전체 EditMode PASS와 package/meta/lock 정합성 확인
- hot path allocation 테스트 0 유지
- 최종 working tree clean 및 `git diff --check` 통과

## 비목표

- 별도 `Bun3.FixedFloat` 래퍼 추가
- BigNum 정밀도 확대 또는 arbitrary precision 타입 도입
- FixedMathSharp fork/vendor
- Unity 중복 번들 DLL 제거를 위한 다른 패키지 수정
- 결정론 상태에 float/double 원본 저장
