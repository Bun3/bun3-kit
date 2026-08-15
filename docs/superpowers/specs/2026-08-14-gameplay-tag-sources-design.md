# GameplayTag Source와 Catalog 배포 설계

- 상태: 승인됨
- 작성일: 2026-08-14
- 적용 범위: `Bun3.Gameplay`, GameplayTag Unity Editor 도구, Native .NET 서버 통합, Catalog 작성 도구
- 기반 명세: [`2026-08-12-gameplay-tag-catalog-design.md`](2026-08-12-gameplay-tag-catalog-design.md), [`2026-08-13-gameplay-tag-editor-unreal-workflow-design.md`](2026-08-13-gameplay-tag-editor-unreal-workflow-design.md)
- 참고 조사: [`2026-08-12-unreal-gas-gameplay-tag-ownership.md`](../../research/2026-08-12-unreal-gas-gameplay-tag-ownership.md)

## 1. 목적과 범위

프레임워크 코드가 요구하는 태그와 게임이 작성하는 태그를 하나의 의미 체계로 합친다. 프레임워크
태그가 설치된 실행 대상에서 누락되지 않아야 하며, 서로 다른 저장소와 파이프라인에서 빌드되는 Unity
클라이언트와 Native .NET 서버도 같은 태그 이름, 계층, wire index와 redirect를 사용해야 한다.

작성 단계에서는 태그의 출처와 Source별 comment를 보존한다. 실행 단계에서는 Source를 namespace로
취급하지 않고 같은 canonical 이름을 하나의 태그로 병합한다. 실행 대상이 각자 설치된 패키지를 스캔해
Catalog를 만들지 않고, 게임 단위로 한 번 게시한 Catalog를 공용 리소스 계약으로 사용한다.

사람이 편집하는 JSON과 실행 리소스를 구분한다. 게임 JSON, 패키지 JSON과 C# Native 선언은 작성
원본이고, 실행 리소스는 이 원본들을 컴파일한 단일 바이너리 `GameplayTags.catalog`다. 바이너리화의
목적은 보안이나 난독화가 아니라 우발적인 수동 편집과 파일 손상을 막는 것이다.

## 2. 결정 요약

| 주제 | 결정 |
|---|---|
| 런타임 태그 identity | Source가 아닌 전체 canonical 태그 이름 |
| canonical 표기 | `ToLowerInvariant()`로 정규화한 전체 소문자 |
| Source 종류 | Game JSON, Package JSON, C# Native |
| 중복 선언 | 여러 Source의 같은 태그를 하나의 런타임 태그로 병합 |
| comment | Source별로 독립 보존하고 런타임에서는 제거 |
| implicit 부모 | 명시 태그와 동일하게 선택·조회 가능한 실제 태그 |
| Game Source | `ProjectSettings/GameplayTags.json`에 고정된 하나의 편집 가능 Source |
| Tag Editor | Source를 가상 최상위 root로 하는 작성 뷰 |
| Inspector Picker | Source 중복을 제거한 병합 런타임 뷰 |
| Runtime Catalog | 모든 Source를 병합한 불변 Catalog |
| 배포 포맷 | `B3DK`로 시작하는 단일 `GameplayTags.catalog` |
| 배포 모델 | 게임 단위로 한 번 게시하고 Unity와 서버가 같은 버전을 고정 |
| 개발 모델 | OS 공용 dev cache를 Unity, CLI와 서버 IDE가 공유 |
| Catalog Version | 저장마다 증가하지 않고 게시 시 명시적으로 결정 |
| Development Version | `0.0.0-dev`; 실제 의미 차이는 fingerprint로 구분 |
| 실패 정책 | fallback 없이 팝업 또는 명시적인 예외로 작업·시작 차단 |

## 3. 용어와 불변식

프로젝트의 도메인 용어는 루트 [`CONTEXT.md`](../../../CONTEXT.md)를 따른다. 이 명세에서 사용하는
핵심 불변식은 다음과 같다.

1. `Tag Source`는 작성 출처와 소유 단위이지 런타임 namespace가 아니다.
2. 같은 canonical 이름은 Source 수와 관계없이 하나의 `GameplayTag`다.
3. explicit 선언이 없어도 descendant가 있으면 모든 조상은 유효한 implicit 태그다.
4. implicit 태그는 폴더가 아니며 선택, 직렬화, 조회와 계층 질의가 가능하다.
5. 한 Source의 선언을 제거해도 다른 Source가 선언하거나 descendant가 남아 있으면 태그는 유지된다.
6. 하나의 simulation world와 network session은 하나의 고정된 Runtime Catalog만 사용한다.
7. Source root, comment, 편집 권한과 Source 표시 순서는 runtime fingerprint에 영향을 주지 않는다.

## 4. 작성 모델

### 4.1 Tag Source

각 Source는 다음 정보를 가진다.

- 안정적인 `SourceId`
- 에디터 표시 이름
- 종류: `GameJson`, `PackageJson`, `Native`
- 편집 가능 여부
- 명시적으로 선언한 태그와 Source별 comment
- Source가 소유한 redirect

`SourceId`는 패키지 이름이나 게임 소유권을 안정적으로 나타내며 대소문자를 무시한 중복을 허용하지
않는다. `game`은 고정 Game Source ID다. Source 정렬은 ID의 ordinal 순서를 사용하지만 정렬 결과는
태그 identity나 fingerprint에 들어가지 않는다.

Game JSON만 기본 편집 가능 Source다. Package JSON과 Native Source는 읽기 전용이며 탐색, 태그 복사,
참조 검색만 허용한다. 이후 DLC나 게임 모듈용 추가 writable Source가 필요하면 별도 설계로 확장한다.

### 4.2 canonical 태그 이름

태그 문법과 길이·깊이 제한은 기존 Catalog 명세를 유지하되 저장과 병합 전에
`ToLowerInvariant()`를 적용한다.

```text
입력:   Ability.Movement.Jump
저장:   ability.movement.jump
```

- Tag Editor의 추가·이름 변경은 대문자를 오류로 처리하지 않고 소문자로 저장한다.
- Package와 Native Source도 compiler input에서 소문자로 정규화한다.
- 런타임 문자열 조회도 동일하게 정규화한다.
- case-only 차이에는 redirect를 만들지 않는다.
- Source별 원래 casing은 별도로 보존하지 않는다.

### 4.3 명시 태그와 implicit 부모

Source가 다음 태그 하나를 선언하면:

```text
ability.movement.jump
```

Runtime Catalog에는 다음 세 태그가 존재한다.

```text
ability
ability.movement
ability.movement.jump
```

`ability`와 `ability.movement`는 implicit이지만 완전한 태그다. 마지막 explicit descendant가 제거되면
그때 더 이상 필요하지 않은 implicit 부모도 제거된다. implicit 부모에 comment를 작성하면 해당 Source의
explicit 선언으로 승격한다.

### 4.4 Source별 comment

여러 Source가 같은 태그에 서로 다른 comment를 제공해도 오류가 아니다. comment에는 우선순위나
대표값을 만들지 않는다.

```text
Game/ability.jump             "플레이어의 점프 능력"
Bun3.Gameplay/ability.jump    "점프 동작에 쓰는 기본 능력 태그"
```

Tag Editor는 각각의 comment를 Source tree에서 보여 준다. Inspector Picker는 태그를 한 번만 표시하고
tooltip이나 Source 상세 정보에서 각 comment를 보여 준다. Runtime Catalog에는 comment를 넣지 않는다.

## 5. Source 제공과 발견

### 5.1 고정 Game Source

게임 작성 원본은 다음 경로로 고정한다.

```text
<UnityProject>/ProjectSettings/GameplayTags.json
```

경로 설정, Open, New와 임의 경로 탐색 기능은 제거한다. 파일은 Git으로 추적하며 Unity Asset이나
runtime resource로 import하지 않는다. 파일이 없으면 Tag Editor는 빈 Source로 조용히 처리하지 않고
`Create Game Source`를 제공한다. Play Mode, Local Development Catalog compile과 release publish는
파일이 없으면 실패한다. 게임 태그가 없어도 다음 빈 파일로 의도를 명시한다.

```json
{
  "schemaVersion": 1,
  "tags": [],
  "redirects": []
}
```

Catalog ID와 Source package dependency는 태그 선언이 아니므로 이 JSON에 중복 기록하지 않는다.
게임의 기존 리소스 빌드·의존성 계층이 compiler에 제공한다. Catalog 도구는 구체적인 package registry나
저장소를 직접 알지 않고, resolve된 Source Metadata 목록을 입력으로 받는다.

### 5.2 Game Catalog Build Context

작성·게시 host는 `GameCatalogBuildContext`라는 하나의 논리 입력으로 다음 값을 제공한다.

- 안정적인 Catalog ID
- resolve된 제품 전체 Source Metadata 목록
- Development 또는 Publish build mode
- Publish mode에서 사용할 Catalog Version

이 context는 태그 원본이 아니며 `GameplayTags.json`에 직렬화하지 않는다. Unity Editor integration,
headless CLI와 CI adapter가 각 환경의 기존 게임 리소스 설정을 읽어 같은 context를 만든다. Context가
없으면 Tag Editor에서 Game Source를 작성할 수는 있지만 Preview의 framework Source를 완전하게 만들 수
없으므로 dev compile, Play와 publish를 차단하고 configuration error를 표시한다.

Runtime Tags와 Catalog Compiler는 context가 어디에 저장됐는지 알지 않는다. 이 경계 덕분에 각 게임이
현재 `Items.xml` 같은 공용 리소스 dependency를 고정하는 방식을 재사용하고, GameplayTag framework가
두 번째 제품 manifest 형식을 강제하지 않는다.

### 5.3 Package JSON Source

프레임워크 패키지는 읽기 전용 Tag Source Metadata를 패키지 산출물에 포함한다. 게임 Catalog 게시
과정은 Unity 전용, 서버 전용과 공통 패키지를 합친 제품 전체 Source dependency set을 resolve한다.
Unity와 서버가 각자 설치한 패키지만 발견해 Catalog를 다시 만들지 않는다.

제품 전체 dependency set은 `Items.xml` 같은 공용 리소스가 참조되는 계층과 같은 소유권을 가진다.
Unity 프로젝트는 Tag Editor와 Catalog 게시의 작성 권한을 가지지만, 게시된 Catalog는 Unity 빌드의
부산물이 아니라 클라이언트와 서버가 함께 소비하는 게임 리소스다.

### 5.4 C# Native Source

Native 태그는 runtime static registration이나 reflection scan으로 발견하지 않는다. 패키지 또는 게임
어셈블리를 빌드할 때 전용 선언 attribute/analyzer가 compile-time 상수 선언을 Source Metadata로 만든다.
개념적인 작성 형태는 다음과 같다.

```csharp
[assembly: GameplayTagSource("bun3.enhanced-input", "Bun3.EnhancedInput")]

public static class EnhancedInputTags
{
    [NativeGameplayTag("점프 입력")]
    public const string Jump = "input.jump";
}
```

- assembly-level source declaration은 안정적인 ID와 표시 이름을 정한다.
- field-level declaration은 compile-time 태그 이름과 comment를 제공한다.
- analyzer는 허용되지 않은 필드 형태와 잘못된 태그 문법을 build error로 보고한다.
- metadata producer는 이름을 canonical 소문자로 기록한다.
- 생성된 Source Metadata는 package tooling input이며 최종 게임 배포물에는 포함하지 않는다.
- runtime framework code는 Catalog가 고정된 뒤 const path를 `GetRequired`로 해석한다.

이 contract는 package가 같은 태그를 선언하더라도 runtime 초기화 순서나 assembly unload에 의미가
흔들리지 않게 한다.

## 6. Catalog Compiler와 모듈 경계

### 6.1 논리 모듈

구현은 다음 경계를 유지한다. 첫 구현에서 물리적인 NuGet/UPM package를 모두 분리할 필요는 없지만
Unity 의존성과 runtime reader 의존성은 역전시키지 않는다.

| 모듈 | 책임 | 의존성 |
|---|---|---|
| Runtime Tags | `GameplayTag`, container, immutable `TagCatalog`, binary reader | BCL |
| Catalog Tooling | Source parsing, merge, validation, provenance, binary writer | Runtime Tags, JSON parser |
| Unity Editor Adapter | 고정 Source, Source tree, Picker, Play/build gate, popup | Catalog Tooling, UnityEditor |
| Catalog CLI | headless compile과 local cache 작성 | Catalog Tooling |
| Server Adapter | 환경별 artifact resolve와 startup freeze | Runtime Tags, hosting integration |

Runtime Tags는 파일 경로나 package registry를 알지 않는다. binary reader는 readable `Stream`과 기대값을
받는다. Unity와 서버 adapter가 각각 stream을 연다. Catalog Tooling은 Unity API를 사용하지 않으므로
CLI와 CI에서 같은 compiler를 실행할 수 있다.

### 6.2 Compiler input과 output

`TagCatalogCompiler`의 개념적인 입력과 출력은 다음과 같다.

```text
Compile(Source Documents, Catalog Identity)
    -> Runtime Catalog
    -> Provenance Index
    -> Diagnostics
```

- Runtime Catalog: canonical 태그, hierarchy, deterministic index, redirect와 fingerprint
- Provenance Index: 태그별 contributing Source, explicit/implicit 여부와 Source별 comment
- Diagnostics: Source ID, 파일 또는 declaration 위치, 태그 경로와 오류 코드

Runtime Catalog와 Provenance Index를 분리한다. Runtime Catalog는 Source를 알지 않고 Unity와 서버에서
공유한다. Provenance Index는 Editor Workspace와 진단에만 사용한다.

### 6.3 결정론

Source 입력 순서와 package resolve 순서는 결과에 영향을 주지 않는다.

1. Source ID 중복과 Source 자체 형식을 검사한다.
2. 모든 이름을 canonical 소문자로 정규화한다.
3. 같은 태그 선언을 set union으로 병합한다.
4. implicit 부모를 만든다.
5. 전체 canonical tree를 ordinal 순서로 정렬한다.
6. preorder로 `ushort` index와 subtree 범위를 부여한다.
7. redirect를 병합하고 최종 target index로 평탄화한다.
8. semantic fingerprint를 계산한다.

태그 수가 65,535개를 초과하면 compile error다. `0`은 계속 `GameplayTag.None`으로 예약한다.

## 7. Redirect 병합과 수명

Redirect는 작성 Source가 소유하지만 runtime에서는 하나의 mapping으로 병합한다.

- 같은 `old -> new`가 여러 Source에 있으면 하나로 병합한다.
- 같은 `old`가 서로 다른 target으로 향하면 compile error다.
- self redirect와 cycle은 compile error다.
- 최종 target이 활성 태그가 아니면 compile error다.
- chain은 compile할 때 최종 활성 target으로 평탄화한다.
- fingerprint에는 canonical old name과 최종 target이 들어간다.

활성 태그와 같은 old name을 가진 redirect는 허용한다. 이 상황은 다른 Source가 예전 이름을 계속
선언하는 동안 source-scoped rename이 발생하면 생긴다. lookup은 활성 태그를 먼저 찾고 실패했을 때만
redirect를 찾으므로 redirect는 이전 활성 선언이 사라질 때까지 shadowed 상태다. Compiler와 Editor는
이를 warning으로 표시하고 오류로 처리하지 않는다.

이 결정은 활성 이름과 redirect source의 충돌을 금지했던 기존 단일 Source contract를 대체한다.

## 8. Editor Workspace와 두 가지 tree projection

### 8.1 Editor Workspace

Editor time에는 전역으로 영구 고정된 Runtime Catalog를 유지하지 않는다. Source Documents가 정본이고,
Editor Workspace가 필요할 때 Source를 compile해 disposable `Preview Snapshot`을 만든다.

```text
Source Documents
      -> Catalog Compiler
          -> Preview Runtime Catalog
          -> Provenance Index
```

Preview Snapshot은 Tag Editor, Inspector Picker, validation, implicit parent 계산과 reference tooling에
공유한다. Source나 package metadata가 바뀌면 snapshot을 폐기하고 다시 만든다.

### 8.2 Tag Editor projection

Tag Editor는 Source를 가상 최상위 root로 보여 준다.

```text
Game                                      Editable
  ability
    movement
      jump                                "플레이어 점프"

Bun3.Gameplay                             Read Only
  ability
    movement
      jump                                "기본 점프 능력"
```

- Source root는 GameplayTag가 아니며 runtime과 fingerprint에 들어가지 않는다.
- 동일 태그가 여러 Source에 있으면 Source마다 중복 표시한다.
- implicit 부모도 실제 태그 node로 표시한다.
- Source별 comment와 read-only 상태를 node에서 확인한다.
- Redirect도 Source별 목록과 read-only 상태를 보여 준다.

### 8.3 Inspector Picker projection

Inspector Picker는 병합된 Runtime Catalog tree를 보여 준다.

```text
ability
  movement
    jump                                  2 sources
```

- Source root와 중복 태그를 표시하지 않는다.
- 선택 결과는 Source가 아닌 canonical 경로 문자열이다.
- Source 수, Source 목록과 Source별 comment는 badge 또는 tooltip으로 확인한다.
- 이름 filter는 canonical 전체 경로를 대소문자와 무관하게 검색한다.
- 검색 중에는 일치 태그의 조상을 자동으로 펼친다.
- 검색을 끝내면 사용자의 이전 expand 상태를 복원한다.
- 전체 영역에 수직·수평 scroll을 제공한다.

Tag Editor와 Picker는 별개의 tree data를 다시 만들지 않고 같은 Preview Snapshot을 서로 다른 projection
mode로 사용한다.

## 9. Source별 편집 transaction

### 9.1 공통 mutation 원칙

모든 mutation은 선택 Source 범위에서 수행한다.

```text
Source 복사본 mutation
      -> 전체 Workspace compile
          -> 성공: session 교체와 dirty 표시
          -> 실패: 복사본 폐기와 경고 popup
```

실패하면 선택, 입력, expand state와 기존 Preview Snapshot을 보존한다. 사용자의 action이 만든 오류는
즉시 popup으로 보여 준다. 외부 파일이나 package 변경으로 생긴 오류는 창 상단 persistent banner와
diagnostic list로 보여 준다.

### 9.2 Rename

Rename은 전역 태그 rename이 아니라 선택한 writable Source의 선언 rename이다.

- 부모 경로를 고정하고 마지막 segment만 편집한다.
- 입력은 canonical 소문자로 정규화한다.
- 선택 node와 그 아래 Source-local subtree를 함께 rename한다.
- implicit node rename은 그 아래 explicit declaration들의 prefix를 변경한다.
- comment는 해당 Source의 새 경로로 이동한다.
- 변경 전 존재한 Source-local active path마다 direct `old -> new` redirect를 만든다.
- 같은 Source에 destination이 이미 있으면 조용히 merge하지 않고 popup 후 취소한다.
- 다른 Source에 destination이 있으면 허용하며 runtime에서는 하나의 태그로 병합한다.

다른 Source가 old name 또는 old subtree를 계속 선언하면 popup에서 old 태그가 Runtime Catalog에 남고
redirect가 shadowed됨을 명확히 경고한다. Read-only Package와 Native Source에는 Rename을 제공하지 않는다.

### 9.3 나머지 context action

- `Edit Comment`: 선택 Source의 comment만 변경한다. implicit node는 explicit 선언으로 승격한다.
- `Add Sub-Tag`: `선택한 canonical 경로 + "."`를 입력란에 채우고 focus할 뿐 즉시 추가하지 않는다.
- `Copy Tag`: canonical 전체 경로를 clipboard에 복사한다.
- `Delete Tag`: 선택 Source의 exact explicit 선언만 제거한다. descendant와 다른 Source 선언은 제거하지
  않는다. implicit node에는 Delete를 제공하지 않는다. 삭제 전 local reference를 검색하고 match가
  있으면 삭제를 막으며 rename과 달리 redirect를 자동 생성하지 않는다.

### 9.4 Redirect 관리

Redirect mapping은 직접 inline 편집하지 않고 rename으로 생성·갱신한다. 기존 Reference Find와
Remove Obsolete workflow를 Source별 목록에 적용한다.

- `Find References`는 해당 old path의 local project 참조를 찾는다.
- `Remove Redirect`는 최신 검색 결과와 확인 popup을 요구한다.
- `Remove Obsolete`는 local match가 0인 redirect만 후보로 보여 주고 사용자가 선택한 항목만 제거한다.
- scan이 취소되거나 일부 파일을 읽지 못하면 제거 가능 판정을 내리지 않는다.
- 외부 save data, 서버 설정과 이미 배포된 build는 local scan으로 검증하지 못했음을 항상 경고한다.

## 10. 저장과 고정 경로 마이그레이션

### 10.1 저장

현재 범위에는 writable Game Source가 하나뿐이지만 adapter는 임시 파일을 먼저 쓰고 검증한 뒤 원본을
교체한다. 저장 실패 시 기존 파일과 dirty state를 보존한다.

Tag Editor에 focus가 있을 때 `Ctrl+S` 또는 `Cmd+S`는 Unity의 일반 Save가 아니라 Game Source를
저장한다. 저장이 성공하면 Local Development Catalog compile을 시도한다. 전체 Source compile이
실패하면 저장한 작성 원본은 보존하고 기존 dev Catalog를 교체하지 않으며 popup을 표시한다.

### 10.2 기존 사용자 지정 경로 import

기존 설정이 가리키는 JSON을 발견하면 자동으로 이동하거나 삭제하지 않는다.

1. 기존 경로와 새 고정 경로를 popup에 표시한다.
2. 사용자가 `Import`를 선택하면 기존 JSON을 읽고 검증한다.
3. 태그와 redirect를 소문자로 정규화한다.
4. 임시 파일을 거쳐 `ProjectSettings/GameplayTags.json`에 복사한다.
5. 전체 Source set을 compile해 결과를 확인한다.
6. 기존 파일은 그대로 남겨 사용자가 확인 후 직접 정리하게 한다.

검증이나 쓰기가 실패하면 새 파일을 만들거나 기존 파일을 수정하지 않는다. 기존 Unity 직렬화 문자열은
case-insensitive lookup으로 계속 해석하고 이후 Inspector 저장부터 소문자 canonical 값을 사용한다.

## 11. 바이너리 Catalog 포맷

### 11.1 파일 구조

초기 포맷은 별도 Resource Type 없이 `B3DK` 자체가 GameplayTag Catalog를 뜻한다.

```text
Offset  Size       Field
0       4          ASCII Magic: B3DK
4       2          Schema Version (UInt16)
6       2          Catalog ID byte length (UInt16)
8       2          Catalog Version byte length (UInt16)
10      4          Payload byte length (UInt32)
14      32         Semantic Fingerprint (SHA-256)
46      32         Content Checksum (SHA-256)
78      variable   Catalog ID (UTF-8)
...     variable   Catalog Version (UTF-8)
...     variable   GameplayTag Payload
```

- 모든 integer는 little-endian이다.
- 문자열은 strict UTF-8이다.
- 압축, 암호화, 전자서명과 Resource Type은 포함하지 않는다.
- 동일 input, Catalog ID와 Catalog Version은 byte-identical output을 만든다.
- Content Checksum은 checksum field를 0으로 채운 전체 파일의 SHA-256이다.

### 11.2 Payload

Payload에는 runtime에 필요한 정보만 들어간다.

```text
Tag Count                                      UInt32
  repeated Tag Entry in index order
    Index                                      UInt16
    Canonical Name byte length                 UInt16
    Canonical Name                             UTF-8 bytes
    Parent Index                               UInt16
    Subtree End Index                          UInt16

Redirect Count                                 UInt32
  repeated Redirect Entry in old-name order
    Old Canonical Name byte length             UInt16
    Old Canonical Name                         UTF-8 bytes
    Final Target Index                         UInt16
```

Source, comment, explicit 여부와 editor permission은 포함하지 않는다. Redirect는 target 문자열 대신 최종
tag index를 기록한다. binary reader는 중복 index, 잘못된 parent/subtree range, 범위를 벗어난 target과
canonical 순서 위반을 모두 거부한다.

### 11.3 Checksum과 fingerprint

- Content Checksum은 파일 손상과 우발적인 byte 변경을 검출한다. ID, Version과 payload 변경도 잡는다.
- Semantic Fingerprint는 schema, canonical 태그 이름, hierarchy, index와 최종 redirect 의미만 포함한다.
- Source, comment, Catalog ID와 Catalog Version은 Semantic Fingerprint에 포함하지 않는다.

따라서 같은 의미를 다른 배포 버전으로 다시 게시하면 checksum은 달라질 수 있지만 fingerprint는 같다.
network peer 호환성은 fingerprint로 확인한다.

## 12. Catalog ID, Version과 게시

Catalog ID는 게임 제품을 나타내는 안정적인 값이다. 예시는 `jurassic-paradise`다. Catalog Version은
게시된 공용 리소스를 선택하는 명시적인 배포 버전이다.

- Tag Editor 저장은 Catalog Version을 올리지 않는다.
- source JSON `schemaVersion`은 JSON 형식 migration 때만 변경한다.
- 태그와 redirect 의미가 바뀌면 fingerprint는 자동으로 바뀐다.
- publish job이 게임 release/content version을 Catalog Version으로 주입한다.
- 같은 Catalog ID와 Version에 다른 checksum 또는 fingerprint를 다시 게시하는 것을 금지한다.
- 이미 게시된 artifact는 immutable이다.

게시 결과는 추가 manifest나 checksum sidecar 없이 단일 파일이다.

```text
GameplayTags.catalog
```

Unity와 서버 pipeline은 같은 Catalog ID와 Version을 고정해 artifact registry에서 동일 파일을 받는다.
어느 실행 대상도 release build 중 자신의 package set으로 Catalog를 재합성하지 않는다.
각 pipeline은 내려받은 binary header를 read back하고 ID, Version과 fingerprint를 해당 build의 생성된
상수 또는 기존 build metadata에 고정한다. Runtime loader의 expected fingerprint는 이 build metadata에서
오며 Catalog file 안의 값을 자기 자신과 비교하지 않는다. 별도 GameplayTag manifest file은 배포하지
않는다.

## 13. Local Development Catalog

### 13.1 공용 cache

개발 중에는 registry download 대신 OS 공용 cache를 사용한다. root는
`Environment.SpecialFolder.LocalApplicationData`로 구한다.

```text
<LocalApplicationData>/Bun3/GameplayTags/<catalog-id>/dev/GameplayTags.catalog
```

Development Catalog는 `0.0.0-dev` Version을 사용하고 태그 변경은 fingerprint로 구분한다.

Unity는 다음 시점에 dev Catalog compile을 시도한다.

- Tag Editor 저장 성공 직후
- Play Mode 진입 전
- `Gameplay/Build Local Tag Catalog` 명령

`bun3-tags compile --development` CLI도 같은 compiler와 cache path를 사용한다. Unity를 열지 않고 서버만
개발할 때는 게임 콘텐츠 저장소에서 이 명령을 태그 변경 시 실행한다. 서버 IDE 실행마다 compile이나
download를 강제하지 않는다.

### 13.2 서버 환경별 resolve

서버 adapter는 다음 두 mode를 제공한다.

```text
LocalDevelopment
  -> OS 공용 dev cache

Packaged
  -> publish 결과에 포함된 GameplayTags.catalog
```

Development 설정은 Catalog ID와 mode만 요구한다. 필요할 때만 사용자별 환경 변수
`BUN3_GAMEPLAY_TAG_CATALOG_PATH`로 explicit file을 override할 수 있다. 절대 경로는 저장소 설정에
commit하지 않는다.

Development loader는 magic, schema, checksum과 Catalog ID를 검사하고 `0.0.0-dev`를 허용한다.
Production loader는 build가 고정한 ID, Version과 expected fingerprint를 모두 검사한다.
어느 mode도 실행 중 hot reload하지 않는다. 태그 변경 후 프로세스를 재시작해야 한다.

dev compile이 실패하면 이전에 성공한 cache file은 원자적 교체 대상이 아니므로 그대로 남는다. Unity
Editor는 현재 invalid Workspace에서 이 이전 file로 Play하지 않는다. Source Workspace를 갖지 않은 별도
서버 IDE 실행은 마지막으로 성공한 dev artifact를 명시적 입력으로 계속 읽을 수 있으며 시작 로그에
Catalog ID, Version과 fingerprint를 출력한다. 새 Source 의미가 필요하면 Unity compile 또는 CLI를 먼저
성공시켜야 한다. 이는 손상된 새 file을 이전 file로 대체하는 runtime fallback이 아니다.

## 14. Lifecycle과 데이터 흐름

### 14.1 Editor와 Local Play

```text
Source Documents
    -> in-memory Preview Snapshot
    -> local compile
    -> GameplayTags.catalog
    -> 실제 binary reader로 재로드
    -> Play session 동안 freeze
```

Play Mode는 Preview object를 runtime에 직접 넘기지 않는다. 실제 binary round-trip으로 format과 loader를
검증한 뒤 첫 gameplay object/world/network simulation 전에 Catalog를 고정한다. Source가 invalid하거나
Game Source가 없으면 Play 진입을 차단한다.

### 14.2 Release Unity Client

```text
Pinned Published Catalog
    -> Unity build에 포함
    -> 첫 gameplay scene 이전 load/validate/freeze
```

Release Unity build는 working tree의 Game JSON에서 Catalog를 몰래 만들지 않는다. pipeline이 선택한
published artifact만 포함한다.

### 14.3 Native .NET Server

```text
Pinned Published Catalog 또는 Local Development Catalog
    -> stream open
    -> binary load/validate
    -> IHost.StartAsync 이전 freeze
```

서버가 hosted service, request handling이나 gameplay state를 시작한 뒤 Catalog를 바꾸지 않는다.

### 14.4 Network compatibility

연결 handshake에서 양쪽이 로드한 Semantic Fingerprint를 비교한다. 다르면 어떤 `ushort` tag index도
해석하기 전에 연결을 거부한다. 서버가 Unity 전용 태그를 사용하지 않더라도 같은 Catalog를 읽어야 index
mapping이 동일하다.

## 15. 오류와 예외

### 15.1 Editor 오류 UX

사용자 action이 실패하면 경고 popup을 띄우고 mutation을 적용하지 않는다. 외부 JSON, Native Metadata나
package dependency 변경으로 Workspace가 invalid해지면 persistent error banner와 Source별 diagnostic을
보여 준다.

- Tag Editor는 오류를 확인하고 고치기 위해 계속 열 수 있다.
- 기존 직렬화 태그 이름은 raw text로 표시할 수 있다.
- Inspector Picker의 신규 선택은 compile 성공 전까지 비활성화한다.
- Play, local compile과 publish를 차단한다.
- Unity Editor는 마지막 정상 Preview나 binary로 현재 invalid Workspace를 조용히 대체하지 않는다.

Diagnostic은 가능한 경우 Source ID, 파일/declaration 위치, canonical tag path와 오류 코드를 포함한다.

### 15.2 Runtime 예외

검증 순서는 다음과 같다.

```text
B3DK
 -> supported schema
 -> lengths and bounds
 -> checksum
 -> expected ID/version/fingerprint
 -> payload invariants
 -> immutable TagCatalog
```

- 파일 형식, 손상, 잘린 파일과 payload invariant 오류는 `TagCatalogFormatException`이다.
- 기대 ID, Version 또는 fingerprint 불일치는 `TagCatalogCompatibilityException`이다.
- 빈 Catalog, 이전 파일이나 내장 기본값으로 fallback하지 않는다.

## 16. 기존 public JSON API migration

현재 `TagCatalog.Load(Stream)`은 UTF-8 JSON을 직접 읽는 공개 API다. 즉시 의미를 binary load로 바꾸지
않는다. 한 호환 release 동안 JSON API로 유지하고 `[Obsolete]`로 binary runtime 경로를 안내한다.

새 runtime API는 명시적인 binary entry point를 사용한다.

```csharp
TagCatalogBinary.Load(stream, expectations)
```

JSON과 binary API는 magic sniffing으로 자동 전환하지 않는다. 잘못된 포맷을 넘기면 해당 loader에서
명확히 실패한다. JSON parsing은 Catalog Tooling과 기존 migration을 위해 유지하고 다음 breaking release에
별도로 승인된 migration 명세가 있을 때 공개 Runtime API에서 제거한다.

## 17. 테스트와 완료 조건

### 17.1 순수 .NET

- 여러 Source의 동일 태그가 하나의 runtime tag/index로 병합된다.
- 한 Source 제거 후에도 다른 declaration 또는 descendant가 있으면 태그가 유지된다.
- implicit 부모가 생성되고 선택·조회 가능한 태그가 된다.
- 모든 입력 경로가 소문자로 정규화된다.
- Source별 comment가 독립적으로 보존되고 runtime output에서 제거된다.
- Source input 순서를 바꿔도 index, fingerprint와 binary byte가 같다.
- redirect 중복, conflict, cycle, missing target, flatten과 shadow warning이 contract대로 동작한다.
- 동일 input과 identity는 byte-identical binary를 만든다.
- binary round-trip 뒤 name/index/parent/subtree/redirect/fingerprint가 같다.
- magic, schema, UTF-8, length, checksum, truncation과 payload invariant 손상을 거부한다.
- 65,535 tag 한계를 검증한다.
- Development와 Production expectation 정책이 다르게 적용된다.
- JSON legacy API와 binary API가 포맷을 자동 감지하지 않는다.

### 17.2 Unity EditMode

- 고정 Game Source 경로만 사용하고 누락 상태를 명시적으로 표시한다.
- 기존 사용자 지정 JSON import가 검증·정규화·비파괴 방식으로 동작한다.
- Source root, 중복 태그, Source별 comment와 read-only 권한이 표시된다.
- Picker가 병합 tag, source detail, filter, auto-expand와 양방향 scroll을 제공한다.
- source-scoped rename, implicit rename, shadowed redirect warning과 destination collision이 동작한다.
- context action과 redirect reference/cleanup 정책이 Source별로 동작한다.
- `Ctrl+S`가 Game Source를 저장하고 성공 후 dev compile을 시도한다.
- save/compile 실패가 기존 파일, dirty state와 Preview contract를 지킨다.
- invalid workspace가 Picker 선택과 Play Mode를 차단한다.
- Play Mode가 binary round-trip 뒤 Catalog를 고정한다.

### 17.3 통합·출시 gate

한 번 만든 binary를 Unity loader와 Native .NET loader가 각각 읽었을 때 모든 name/index/parent/subtree,
redirect와 fingerprint가 같아야 한다. 다음 gate를 모두 만족한다.

- 관련 Release build warning 0
- 전체 .NET test failure 0
- Unity EditMode test failure 0
- binary determinism과 corruption test 통과
- client/server fingerprint mismatch handshake test 통과
- NuGet/UPM dependency와 package metadata readback 통과
- publish와 push는 별도 사용자 요청 없이 수행하지 않음

이번 변경은 기존 JSON API를 유지하면서 public Source·binary API와 Editor 기능을 추가하므로
`Bun3.Gameplay` NuGet/UPM package version을 `0.8.0`에서 `0.9.0`으로 올린다. 새 package를 물리적으로
분리하는 결정은 이번 명세에 포함하지 않으며 Catalog Tooling은 우선 같은 배포 package 안의 독립 assembly
경계를 사용한다.

## 18. 범위 밖

- 전자서명, 암호화와 난독화
- `B3DK` Resource Type field
- runtime Source discovery, static registration과 reflection scan
- runtime Catalog hot reload
- 태그 저장마다 Catalog Version 자동 증가
- 여러 editable Game Source
- wildcard와 prefix redirect
- client와 server가 각자 설치된 package로 Catalog 재생성
- Tag Editor에서 Artifact Registry로 자동 publish
- local reference search가 외부 save, 서버 설정과 과거 배포물의 안전성을 보증하는 것

## 19. 기각한 대안

### 19.1 실행 시 Source를 동적으로 등록

Unreal의 global manager lifecycle과 가장 유사하지만 assembly 초기화 순서, Unity IL2CPP/reflection,
server target별 package 차이와 network fingerprint invalidation을 runtime으로 끌고 온다. 이번 framework는
session Catalog를 고정하고 build/publish 경계에서 합친다.

### 19.2 Unity와 서버가 각자 Catalog 생성

compiler가 결정론적이어도 입력 package set이 다르면 index가 달라진다. Unity 전용 Source를 서버가
사용하지 않더라도 같은 게임 전체 Catalog를 배포해야 한다.

### 19.3 생성된 runtime JSON 배포

사람이 편집하는 Game Source와 생성물을 혼동하고 실수로 수정할 경로가 늘어난다. 단일 binary와 checksum을
사용한다.

### 19.4 binary에 전자서명 추가

현재 목표는 의도적인 공격이 아니라 우발적인 편집과 손상 검출이다. `B3DK`, strict format validation과
SHA-256 checksum으로 충분하며 서명은 실제 위협 요구가 생길 때 별도 schema로 추가한다.

### 19.5 설정 가능한 Game Source 경로 유지

경로 이동, 저장소별 설정 차이와 잘못된 파일 선택 가능성이 남는다. 프로젝트 전체 설정인
`ProjectSettings/GameplayTags.json`으로 고정한다.
