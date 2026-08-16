# 게임플레이 리뷰 보완 설계 (BigNum · GameplayTag · 패키지 경계)

- 상태: 승인됨
- 작성일: 2026-08-15
- 적용 패키지: `Bun3.Gameplay`, `Bun3.Gameplay.Authoring`(신규), `com.bun3.gameplay`
- 기반 명세: [`2026-08-10-gameplay-framework-design.md`](2026-08-10-gameplay-framework-design.md),
  [`2026-08-12-gameplay-tag-catalog-design.md`](2026-08-12-gameplay-tag-catalog-design.md)

## 1. 목적과 범위

BigNum·GameplayTag 코드 리뷰에서 확인된 항목을 보완한다. 산술 코어는 BigInteger 오라클
차등 테스트 40만 케이스를 통과했으므로 **수치 의미는 바꾸지 않는다.** 이 스펙이 바꾸는 것은
어셈블리·패키지 경계, 죽은 코드, 계약 명시, 테스트 보강뿐이다.

태그 의미(계층 매칭, 결정적 index, fingerprint 값), JSON schema, B3DK 형식은 그대로 둔다.

## 2. 결정 요약

| 항목 | 결정 |
|---|---|
| 런타임의 Newtonsoft 의존 | 레거시 JSON 로더를 저작 어셈블리로 이동해 제거 |
| 저작 어셈블리 배포 | `Bun3.Gameplay.Authoring` 별도 NuGet 패키지로 분리 |
| `Bun3.Common`의 FixedMathSharp | **유지** — 서버·Unity 양쪽에서 쓸 표준 고정소수 타입 |
| 카탈로그 다중 인스턴스 | 프로세스 내 단일 카탈로그가 원칙, 위반은 런타임 예외 |
| 컨테이너 쿼리 중복 | 공통 추상 기반 클래스로 통합 |
| 정렬 불변식 | 허용 문자가 `.`(0x2E)보다 크다는 전제를 주석으로 명시 |

## 3. 어셈블리 경계 — 런타임에서 저작을 걷어낸다

### 3.1 문제

`Bun3.Gameplay`(런타임, Unity 플레이어에 실림)가 Newtonsoft.Json에 의존했다. 유일한 사용처는
`TagCatalog.Load(Stream)` — 이미 `[Obsolete]`가 붙은 레거시 JSON 경로다. 실제 런타임 로딩
경로는 `TagCatalogBinary.Load`(B3DK)이므로, 플레이어 빌드는 쓰지도 않는 리플렉션 무거운
라이브러리를 링크하고 IL2CPP 스트리핑 비용을 낸다.

### 3.2 이동 대상

`Runtime/Tags/TagCatalog.Json.cs`의 `Loader`와 `StrictJsonSyntax`를 `Bun3.Gameplay.Catalog`
어셈블리로 옮기고, 공개 진입점을 `TagCatalogJson.Load(Stream)`으로 바꾼다.
`TagCatalog.Load(Stream)`은 **삭제한다**(파괴적 변경).

카탈로그 조립 코어(`Build`, `Create`, `ComputeFingerprint`)는 JSON을 모르므로 런타임에 남는다.
저작 어셈블리는 이미 `InternalsVisibleTo`를 받고 있으므로 `Create`와 `RedirectDefinition`을
`internal`로 승격하는 것 외에 추가 배관이 없다.

`StrictJsonSyntax`의 다른 소비자인 `Catalog/Source/TagSourceJson.cs`는 같은 어셈블리로
들어오므로 오히려 참조가 짧아진다.

### 3.3 패키지 분리

`Bun3.Gameplay.csproj`가 `Bun3.Gameplay.Catalog.dll`을 자기 nupkg의 `lib/`에 동봉하던 타깃
(`IncludeCatalogBuildOutput`)을 제거하고, 저작 어셈블리를 자기 패키지로 낸다.

| 패키지 | 내용 | 외부 의존 |
|---|---|---|
| `Bun3.Gameplay` 0.12.0 | 런타임 코어(BigNum, 태그, B3DK 로더) | **없음** |
| `Bun3.Gameplay.Authoring` 0.1.0 | 태그 Source 컴파일러, B3DK writer, 레거시 JSON 로더 | Newtonsoft.Json |

어셈블리 이름은 `Bun3.Gameplay.Catalog`를 유지한다 — Unity asmdef와 기존 참조가 이 이름에
걸려 있고, 이름 변경의 이득이 없다. PackageId만 의도를 드러낸다.

UPM `com.bun3.gameplay`는 저작 코드를 같은 패키지에 계속 담으므로
`com.unity.nuget.newtonsoft-json` 의존을 유지한다. Unity에서의 이득은 **플레이어 빌드에
Newtonsoft가 링크되지 않는 것**이며, 에디터 설치 요건은 그대로다.

### 3.4 런타임 성능 픽스처

`Tests/Runtime/TagPerformanceFixture.cs`는 IL2CPP/Mono 플레이어에서 도는 유일한 런타임
테스트이므로 에디터 전용 저작 어셈블리를 참조할 수 없다. JSON 문자열을 만들어 로드하던 방식을
버리고 런타임 조립 코어(`TagCatalog.Create`)를 직접 호출한다. `Bun3.Gameplay.Runtime.Tests`를
`InternalsVisibleTo`에 추가한다.

## 4. 단일 카탈로그 계약

`GameplayTag`는 맨 `ushort` index다. 다른 카탈로그에서 얻은 태그를 컨테이너에 넣으면 index가
유효 범위 안이라 통과하고 계층 쿼리가 조용히 오답을 낸다.

**원칙: 프로세스 내 카탈로그는 하나다.** 여러 Source를 하나의 카탈로그로 합쳐 런타임에
동작시키는 것이 이 설계의 전제이므로, 다중 카탈로그는 지원 대상이 아니다.

강제 수단:

- `TagContainer`·`TagCountContainer`의 mutation 경로가 카탈로그 범위 밖 index를
  `ArgumentOutOfRangeException`으로 거부한다.
- 조회 경로는 관대하게 유지한다(범위 밖 = 미일치) — 틱 핫패스에서 예외를 던지지 않는다.
- 계약을 public XML 문서에 명시한다.

같은 크기의 서로 다른 카탈로그를 섞는 경우는 index만으로 탐지할 수 없다. 이건 `ushort` 태그의
구조적 비용이며, 계약 문서화로 처리한다.

## 5. 컨테이너 쿼리 통합

`TagContainer`와 `TagCountContainer`가 `HasAny`/`HasAll`/`HasAnyExact`/`HasAllExact`를 각각
동일하게 구현하고 있었다. 공통 추상 기반 `TagQueryContainer`로 올린다.

- 기반이 카탈로그 참조, 카탈로그 동일성 검증, fan-out 쿼리 4종을 소유한다.
- 파생이 `Has`/`HasExact` 단일 태그 판정만 구현한다.
- 생성자는 `internal` — 외부 파생을 막아 기존 `sealed` 의미를 유지한다.

가상 호출은 조회 태그당 1회로, 저장 태그 수와 무관하다. 무할당 규율에 영향 없다.

## 6. 죽은 코드 · 잔여 정리

- `TagCatalog.Build`의 DisplayName 배관 전체를 삭제한다. `Create`가 결과를 canonical 이름으로
  덮어쓰므로 `ExplicitTag.DisplayName`, `BuildNode.DisplayName`, `AddPath`의 display 이중 스캔은
  값이 어디에도 도달하지 않는다. `Build`의 입력은 canonical 이름 목록으로 단순화한다.
- ordinal 정렬이 서브트리 연속 구간을 보장하는 근거(허용 문자 `0-9`·`a-z`가 전부 `.`보다 크다)를
  `BuildCanonicalNames`에 주석으로 명시한다. 허용 문자 집합이 고정이므로 별도 가드 테스트는 두지 않는다.
- `TagCatalogBinary.ReadToEnd`에 크기 상한을 둔다(잘린/거대 파일이 OOM으로 죽지 않게).
- `BigNum.ToString` XML 문서에 남은 플랜 태스크 번호를 지운다.

## 7. 테스트

- **BigNum 차등 테스트 신규**: 시드 고정 랜덤 입력을 BigInteger 유리수 오라클과 대조한다.
  `+ - * /`의 상대오차 1e-17, 정규형 불변식, `CompareTo` 부호 일치, `a + (-a) == 0`.
  손으로 고른 케이스만 있던 기존 오라클 테스트의 빈틈을 메운다.
- 이동한 JSON 로더의 기존 테스트는 진입점 이름만 바꿔 그대로 유지한다 — 오류 메시지, 줄·열
  위치, 예외 타입 계약을 회귀 검사한다.
- 단일 카탈로그 계약 위반이 mutation에서 예외가 되는지 검사한다.

## 8. 버전

| 패키지 | 이전 | 이후 | 이유 |
|---|---|---|---|
| `Bun3.Gameplay` | 0.11.1 | 0.12.0 | `TagCatalog.Load` 제거(파괴적) |
| `Bun3.Gameplay.Authoring` | — | 0.1.0 | 신규 |
| `com.bun3.gameplay` (UPM) | 0.11.1 | 0.12.0 | 동일 소스 |
