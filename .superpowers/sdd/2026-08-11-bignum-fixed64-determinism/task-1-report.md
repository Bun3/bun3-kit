# Task 1 보고서: BigNum 가수 범위 대칭화와 공개 극값

## 구현 내용

- `BigNum.MinValue`와 `BigNum.MaxValue`를 `±long.MaxValue × 10^MaxExponent`로 공개했습니다.
- `Canonicalize(long, long)`에서 `long.MinValue`를 더 이상 잘라내지 않고 `ArgumentOutOfRangeException`으로 즉시 거부합니다.
- 해당 동작과 공개 극값의 대칭성을 검증하는 두 개의 NUnit 테스트를 추가했습니다.

## RED / GREEN

- RED: `dotnet test common/tests/Bun3.Gameplay.Tests --nologo --filter "FullyQualifiedName~BigNumBasicTests"`
  - `BigNum`에 `MaxValue`/`MinValue` 정의가 없어 CS0117 컴파일 오류 8건.
- GREEN: 같은 명령
  - 실패 0, 통과 14, 건너뜀 0, 전체 14.

## 전체 테스트

- `dotnet test common/tests/Bun3.Gameplay.Tests --nologo`
  - 실패 0, 통과 66, 건너뜀 0, 전체 66.

## 변경 파일

- `common/src/com.bun3.gameplay/Runtime/Numerics/BigNum.cs`
- `common/tests/Bun3.Gameplay.Tests/BigNumBasicTests.cs`

## 셀프리뷰

- 변경 범위는 브리프에 지정된 두 파일로 제한했습니다.
- 공개 멤버에 한국어 XML 문서를 추가했고, `long.MinValue` 경로는 정확한 예외 타입을 보장합니다.
- `git diff --check`를 통과했습니다.

## 우려사항

- 저장소의 현재 Gameplay 테스트 프로젝트 전체가 기준 브리프의 270개가 아니라 66개를 보고합니다. 명령 자체는 성공했으며, 이는 현재 체크아웃의 테스트 구성 차이로 보입니다.

## Fix round 1

- `long.MinValue` 예외 메시지를 허용 하한을 정확히 설명하는 `-long.MaxValue 이상이어야 합니다`로 교정했습니다.
- 커밋 제목을 브리프가 요구한 `🐛 BigNum 가수 범위를 대칭화하고 극값 공개`로 amend했습니다.
- 재검증: 집중 BigNumBasicTests 14/14 통과, 전체 Gameplay 66/66 통과, `git diff --check` 통과.
