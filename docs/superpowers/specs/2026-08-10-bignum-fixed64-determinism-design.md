# BigNum 결정론 보강과 Fixed64 도입 설계

- 날짜: 2026-08-10
- 상태: 문서 검토 대기
- 상위 설계: `2026-08-10-gameplay-framework-design.md`
- 범위: `Bun3.Gameplay.Numerics.BigNum`, `BigNumFormat`, `Bun3.Common`의 고정소수점 의존성

## 1. 목적

BigNum 코드 리뷰에서 확인한 경계값·부동소수 변환·포맷 불변식 문제를 수정하고,
향후 이동 및 결정론적 락스텝에서 사용할 공간 수치 타입을 확정한다.

BigNum은 Attribute·경제처럼 큰 십진 범위가 필요한 값에 계속 사용한다. 위치·속도·각도와
공간 기하는 직접 만든 `FixedFloat` 대신 `FixedMathSharp.Fixed64`를 사용한다. idlez 구현은
참고 자료일 뿐이며 저장 데이터, Raw 값, 연산 결과와의 호환성은 요구하지 않는다.

## 2. 비목표

- 이번 변경에서 이동 동기화, 보간, 예측 또는 락스텝 프로토콜을 구현하지 않는다.
- `Fixed64`를 감싸는 `Bun3.FixedFloat` 타입을 만들지 않는다.
- FixedMathSharp의 벡터·행렬·기하 API를 다시 구현하지 않는다.
- BigNum을 위치나 물리 수치 타입으로 확장하지 않는다.

## 3. 결정 요약

| 축 | 결정 |
|---|---|
| BigNum 지원 가수 | `-long.MaxValue`부터 `long.MaxValue`까지 대칭 범위 |
| `long.MinValue` | 정밀도 손실 정규화 대신 `ArgumentOutOfRangeException` |
| BigNum 극값 | 공개 `MinValue`·`MaxValue`, 가수 극값과 `MaxExponent` 조합 |
| 부동소수 변환 | 명시적 변환만 제공, float 7자리·double 16자리 절사 |
| 포맷 단위 | 생성 시 복제하고 읽기 전용 뷰만 공개 |
| 공간 수치 | `FixedMathSharp.Fixed64` Q32.32 |
| 외부 의존성 | 서버와 Unity 모두 Lean `7.0.0` 고정 |
| 직렬화 | BigNum은 `(Mantissa, Exponent)`, Fixed64는 Q32.32 Raw `long` |

## 4. BigNum 표현 범위와 경계값

BigNum의 정규 가수는 대칭 범위 `[-9_223_372_036_854_775_807,
9_223_372_036_854_775_807]`만 허용한다. `long.MinValue`는 절댓값과 부호 반전이 같은 타입에
들어오지 않으며 현재 구현처럼 `/ 10`으로 한 자리를 버리면 정수 변환의 정확성 계약을
깨뜨린다. 따라서 `FromParts(long.MinValue, ...)`와 `(BigNum)long.MinValue`는
`ArgumentOutOfRangeException`을 던진다.

공개 극값은 다음 의미로 고정한다.

- `BigNum.MinValue = FromParts(-long.MaxValue, MaxExponent)`
- `BigNum.MaxValue = FromParts(long.MaxValue, MaxExponent)`

두 값은 서로 정확히 부호 반전 가능하다. 이 대칭성 덕분에 단항 음수, 곱셈, 나눗셈에서
`Math.Abs(long.MinValue)` 특례가 필요하지 않다. 지수가 `MaxExponent`를 넘는 정상 연산은
기존대로 `BigNumOverflowException`, `-MaxExponent` 아래로 내려가는 결과는 0으로 수렴한다.

## 5. BigNum 부동소수 변환

부동소수 변환은 데이터 로드, 에디터 입력, 외부 API 수신 같은 결정론 경계에서만 허용한다.
틱 상태와 네트워크 상태에는 변환 전 부동소수 값을 보관하지 않는다.

- `float`: 절댓값을 `[10^6, 10^7)`로 정규화한 뒤 정수 가수로 0 방향 절사한다.
- `double`: 절댓값을 `[10^15, 10^16)`로 정규화한 뒤 정수 가수로 0 방향 절사한다.
- `NaN`과 양·음의 무한대는 `ArgumentException`을 던진다.
- 0과 음수는 양수와 같은 자릿수 정책을 적용한 뒤 부호만 복원한다.

float 변환을 double 변환에 위임하지 않는다. 위임하면 float가 원래 보장하지 않는 8자리
이상의 이진 표현 흔적이 BigNum에 들어올 수 있기 때문이다. 동일 입력 비트는 실행 환경과
무관하게 동일한 `(Mantissa, Exponent)`가 나와야 한다.

## 6. BigNumFormat 불변성

`BigNumFormat`은 생성 후 완전히 불변이어야 한다.

- 생성자는 전달받은 단위 배열을 복제한다.
- 공개 `Units`는 배열 대신 `IReadOnlyList<string>`으로 노출한 `ReadOnlyCollection<string>`
  뷰를 반환한다.
- `units`, 빈 배열, `units[0]`, 나머지 모든 원소의 null 여부를 순서대로 검증한다.
- `units[0]`은 반드시 빈 문자열이어야 한다.
- `Base`와 `Korean`의 기본 `OverflowStyle`은 현재 동작인 `TopUnit`을 유지하며 XML 문서도
  그 동작으로 고친다.

포맷 핫패스는 내부 복제 배열을 직접 인덱싱하여 추가 할당이 발생하지 않게 한다.

## 7. Fixed64 선택과 패키지 경계

공간 수치는 `FixedMathSharp.Lean` 7.0.0의 `FixedMathSharp.Fixed64`를 공개 타입 그대로
사용한다. Q32.32는 약 ±2,147,483,648 범위와 `1 / 2^32` 해상도를 제공하므로 일반적인
게임 월드의 위치·속도·각도에 충분하다. 큰 절대 좌표가 필요하면 정밀도를 낮춘 별도 타입을
만들지 않고 청크 상대 좌표나 원점 재설정을 사용한다.

래퍼를 두지 않는 이유는 다음과 같다.

- 스칼라 연산자를 다시 노출해도 외부 의존성이 실질적으로 사라지지 않는다.
- 패키지의 `Vector2d`, `Vector3d`, `FixedQuaternion`, 기하 타입과 래퍼 사이 변환이 늘어난다.
- 변환 경계와 중복 테스트가 늘어나는 반면 교체 가능성은 낮다.

의존성은 양쪽 런타임에서 정확히 같은 릴리스로 고정한다.

- .NET/NuGet: `FixedMathSharp.Lean` `7.0.0`을 `Bun3.Common`의 전이 의존성으로 추가한다.
- Unity/UPM: `FixedMathSharp-Unity`의 `v7.0.0` 태그와
  `com.mrdav30.fixedmathsharp.lean` 경로를 Git URL로 고정한다.
- `com.bun3.common/package.json`은 `com.mrdav30.fixedmathsharp.lean: 7.0.0` 의존성을
  선언하고, 프로젝트 manifest는 해당 Git URL을 실제 해석 경로로 제공한다.
- Bun3.Common은 .NET과 UPM 패키지 버전을 모두 `0.3.0`으로 맞춘다.
- Bun3.Gameplay는 공개 API 변경을 포함하므로 .NET과 UPM 패키지 버전을 모두 `0.2.0`으로
  올린다.

FixedMathSharp 업그레이드는 단순 의존성 갱신으로 취급하지 않는다. 연산 반올림, 오버플로,
Raw 표현 또는 벡터 수학 결과가 바뀔 수 있으므로 결정론 호환성 변경으로 검토한다.

참고 자료:

- <https://www.nuget.org/packages/FixedMathSharp.Lean/7.0.0>
- <https://github.com/mrdav30/FixedMathSharp-Unity/tree/v7.0.0/com.mrdav30.fixedmathsharp.lean>
- <https://github.com/mrdav30/FixedMathSharp/blob/v7.0.0/docs/wiki/fixed64-representation.md>

## 8. 결정론적 데이터 흐름

틱 입력 경계에서 사람이 작성한 십진 값은 `Fixed64.Parse`, `FromDecimal`, `FromFraction`
같이 의도가 드러나는 API로 한 번 변환한다. float/double 변환은 Unity Transform이나 렌더링
등 엔진 경계에서만 사용한다. 틱 내부 위치, 속도, 각도와 공간 계산에는 `Fixed64` 및 패키지의
고정소수 벡터만 사용한다.

네트워크, 저장, 스냅샷, 리플레이에는 표현을 그대로 기록한다.

- BigNum: `sint64 mantissa`, `int32 exponent`
- Fixed64: Q32.32 원시 비트를 보존하는 `sfixed64` 또는 동등한 signed 64-bit 필드

역직렬화는 Fixed64의 `FromRaw(long)` 경로를 사용한다. 값 공간의 정수 변환과 Raw 복원은
서로 다른 연산이므로 혼용하지 않는다. 상태 해시도 표시 문자열이나 float 변환값이 아니라
BigNum 두 필드와 Fixed64 Raw 64비트를 입력으로 사용한다.

## 9. 오류 처리

- BigNum의 지원하지 않는 `long.MinValue` 입력은 즉시 예외로 실패한다.
- BigNum 지수 오버플로와 0 나눗셈 정책은 유지한다.
- Fixed64 연산은 선택한 7.0.0 패키지의 오버플로·반올림 정책을 그대로 따르고 골든
  테스트로 잠근다.
- 외부 부동소수 입력의 NaN, 무한대, 표현 범위 초과는 경계에서 거부한다.
- 패키지 버전이나 Raw 직렬화 규격이 서버와 Unity에서 다르면 빌드/적합성 테스트를
  실패시킨다.

## 10. 테스트 전략

BigNum 회귀 테스트는 다음을 고정한다.

- `long.MinValue`의 암시적 변환과 `FromParts`가 예외를 던진다.
- `MinValue == -MaxValue`, `MaxValue == -MinValue`이고 필드가 설계값과 같다.
- float는 7자리, double은 16자리에서 절사되며 float가 double 경로를 타지 않는다.
- NaN·무한대 입력이 거부된다.
- 단위 배열 원본과 공개 뷰를 통해 생성 이후 포맷을 바꿀 수 없다.
- `units[0] == null`을 포함한 모든 null 입력이 NRE가 아닌 인자 예외를 낸다.
- 기존 BigNum 산술·포맷·무할당 테스트가 모두 통과한다.

Fixed64 적합성 테스트는 같은 골든 벡터를 .NET 테스트와 Unity EditMode 테스트에서 실행한다.

- 상수, `FromRaw` 왕복, 최소 증분, 최소·최대값
- 양수·음수의 덧셈, 뺄셈, 곱셈, 나눗셈과 중간 오버플로 경계
- midpoint 반올림, 제곱근, 삼각함수, 벡터 정규화
- 60Hz 누적 이동처럼 반복 오차에 민감한 시나리오
- Raw `long` 직렬화 왕복과 상태 해시 입력 바이트

각 골든 케이스는 결과 Fixed64의 Raw 64비트를 비교한다. `ToDouble()`이나 허용 오차 비교는
결정론 적합성 판정에 사용하지 않는다. 틱 핵심 연산은 워밍업 후 할당 0 스모크도 유지한다.

## 11. 완료 기준

- BigNum의 승인된 다섯 가지 리뷰 항목이 테스트로 재현되고 수정된다.
- Bun3.Common과 Unity 샘플 프로젝트가 동일한 FixedMathSharp Lean 7.0.0을 해석한다.
- .NET 및 Unity 골든 벡터가 같은 Raw 결과를 낸다.
- 모든 솔루션 테스트와 Unity EditMode 테스트가 경고 없이 통과한다.
- 변경한 Bun3.Common과 Bun3.Gameplay 패키지 버전이 각 배포 형식에서 일치한다.
