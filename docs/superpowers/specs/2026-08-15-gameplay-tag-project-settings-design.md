# GameplayTag Project Settings 기본 구성 설계

## 1. 목적

게임 프로젝트가 Tag Editor를 처음 열기 위해 `IGameplayTagBuildContextProvider` 구현 스크립트를 직접
작성해야 하는 진입 장벽을 제거한다. 일반적인 로컬 개발은 Unity `ProjectSettings`에 저장된 Catalog ID만으로
동작하고, 외부 Source resolve 또는 게시 파이프라인 연동이 필요한 프로젝트만 기존 코드 Provider를 사용한다.

이 변경은 Game Source와 build context 설정을 합치지 않는다.

- `ProjectSettings/GameplayTags.json`: 게임이 작성한 태그와 redirect를 소유하는 Game Source
- `ProjectSettings/GameplayTagSettings.asset`: 게임 제품의 Catalog ID를 소유하는 Unity 프로젝트 설정
- `IGameplayTagBuildContextProvider`: 외부 Source와 Published Catalog를 연결하는 선택적 고급 어댑터

## 2. 범위

### 포함

- `ProjectSettings/GameplayTagSettings.asset` 기반 Catalog ID 설정
- `Project Settings > Gameplay Tags` 설정 페이지
- Tag Editor의 인라인 `Configure Catalog` UI
- `PlayerSettings.productName`에서 만든 초기 Catalog ID 제안값
- 코드 Provider가 없는 development context의 Project Settings fallback
- Project Settings와 코드 Provider를 함께 사용할 때의 일치 검증
- Provider가 없는 Published Player build의 명확한 차단

### 제외

- Published artifact 다운로드 또는 업로드
- Catalog Version 자동 증가
- fingerprint 또는 게시 artifact 경로를 Project Settings에 저장하는 기능
- 외부 Source 경로를 Project Settings에서 직접 편집하는 기능
- Provider 스크립트 자동 생성
- Game Source JSON schema 변경

## 3. 저장 모델

Editor 패키지에 `GameplayTagProjectSettings`를 둔다. Unity `ScriptableSingleton<T>`와
`FilePathAttribute`를 사용하며 저장 위치는 다음으로 고정한다.

```text
ProjectSettings/GameplayTagSettings.asset
```

첫 버전의 직렬화 필드는 Catalog ID 하나뿐이다.

```csharp
[SerializeField]
private string _catalogId = string.Empty;
```

설정 파일이 없거나 Catalog ID가 비어 있으면 아직 구성되지 않은 상태다. 읽기만으로 설정 파일을 만들지
않는다. 사용자가 Project Settings의 Apply 또는 Tag Editor의 Save Settings를 실행했을 때만 Unity
`ScriptableSingleton.Save(true)`를 호출한다.

Game Source의 태그, comment, redirect는 이 asset으로 이동하지 않는다. 설정 저장은
`GameplayTags.json`의 내용과 Catalog Version을 변경하지 않는다.

## 4. Catalog ID 정규화와 검증

`GameplayTagCatalogId.Normalize(string)`은 UI 입력과 product name 제안에 공통으로 사용한다.

1. invariant lowercase로 변환한다.
2. ASCII 소문자와 숫자는 유지한다.
3. 그 밖의 문자가 연속되면 하나의 `-`로 바꾼다.
4. 앞뒤 `-`를 제거한다.
5. 결과가 비면 유효하지 않다.

예시는 다음과 같다.

| 입력 | 결과 |
| --- | --- |
| `Jurassic Paradise` | `jurassic-paradise` |
| `Bun3.Game.Core` | `bun3-game-core` |
| `  GAME__SERVER  ` | `game-server` |

저장 시에도 같은 정규화를 적용한다. 정규화 결과가 비어 있으면 파일을 저장하지 않고 경고 팝업을 한 번
표시한다. development cache 경로가 요구하는 소문자 ID 규칙과 결과가 일치해야 한다.

## 5. Development Provider 선택 규칙

코드 Provider 후보는 기존과 같이 Unity `TypeCache`에서 찾되 NUnit 테스트 어셈블리, abstract type,
open generic type, parameterless constructor가 없는 type은 제외한다. Project Settings fallback은 TypeCache에
노출되는 가짜 Provider type을 만들지 않고 resolver가 명시적으로 선택한다.

선택 규칙은 다음 표로 고정한다.

| 코드 Provider 수 | Project Settings | Development 결과 |
| --- | --- | --- |
| 0 | 없음 | `B3TAG3004` 미구성 진단, Game-only validation만 허용 |
| 0 | 유효함 | settings Catalog ID와 빈 외부 경로로 완전한 context 생성 |
| 1 | 없음 | 기존 Provider의 ID와 외부 경로 사용 |
| 1 | 유효함, ID 일치 | settings ID를 기준으로 Provider 외부 경로 사용 |
| 1 | 유효함, ID 불일치 | `B3TAG3002` 설정 오류, 완전한 context 생성 금지 |
| 2 이상 | 무관 | `B3TAG3001`, ordinal 정렬된 후보 이름 표시 |

설정 fallback의 외부 Source 목록은 비어 있다. 설치된 Unity package의
`Bun3/GameplayTags/TagSource.json`은 기존 discovery가 계속 자동 병합한다. package 밖의 추가 metadata를
resolve해야 할 때만 코드 Provider가 필요하다.

`GameplayTagBuildContextResolution`에는 메시지 문자열을 파싱하지 않고 UI 정책을 결정할 수 있는 internal
상태를 추가한다. 미구성 결과는 `RequiresCatalogConfiguration == true`이고 나머지 실패는 false다.

## 6. Published build 규칙

Project Settings fallback은 development 전용이다. 게시 artifact 경로, Catalog Version과 외부에서 고정한
32바이트 fingerprint를 임의 기본값으로 만들지 않는다.

- Published Player build에는 concrete 코드 Provider가 정확히 하나 필요하다.
- 코드 Provider가 없으면 artifact 파일을 열기 전에 build를 실패시킨다.
- 오류는 Project Settings가 development만 구성하며 Published Provider가 필요하다는 내용을 포함한다.
- settings와 코드 Provider가 모두 있으면 Catalog ID가 정확히 같아야 한다.
- 여러 코드 Provider가 있으면 기존 후보 목록과 함께 실패한다.
- `GetPublishedCatalog()`의 artifact ID도 설정/Provider ID와 같아야 한다.

따라서 기본 설정만으로 Tag Editor, Picker, local Play와 Local Development Catalog build는 가능하지만
릴리스 Player build는 게시 파이프라인이 코드 Provider를 연결하기 전까지 fail-closed다.

## 7. Project Settings UI

Unity `SettingsProvider`를 사용해 다음 경로를 등록한다.

```text
Project Settings > Gameplay Tags
```

페이지는 다음 요소만 가진다.

- 설명: Catalog ID는 게임 제품의 안정적인 ID이며 태그 저장마다 변경하지 않는다는 안내
- `Catalog ID` text field
- `Apply` button
- 코드 Provider 상태: 없음, 단일 Provider 전체 타입 이름, 또는 여러 후보
- 단일 Provider와 settings ID가 다르면 오류 HelpBox
- Published build에는 코드 Provider가 필요하다는 정보 HelpBox

설정 파일이 없으면 필드는 `Normalize(PlayerSettings.productName)`으로 채워 보이지만 Apply 전에는 저장된
것으로 취급하지 않는다. 기존 설정이 있으면 product name이 바뀌어도 저장값을 바꾸지 않는다.

Apply는 정규화된 ID가 기존 저장값과 다를 때만 저장한다. 성공 후 열려 있는 Tag Editor와 Picker가 기존
Workspace refresh 경로를 통해 새 설정을 관찰할 수 있도록 repaint를 요청한다.

## 8. Tag Editor 인라인 구성 UI

`GameplayTagBuildContextResolution.RequiresCatalogConfiguration`이 true일 때 진단 영역 바로 아래에 설정
box를 표시한다.

- 제목: `Configure GameplayTag Catalog`
- `Catalog ID` text field
- 초기 표시값: 저장된 값이 없으므로 `Normalize(PlayerSettings.productName)`
- `Save Settings` button
- `Open Project Settings` button

Save Settings가 성공하면 controller가 Workspace를 즉시 다시 resolve하고 트리, redirect, picker 상태를
갱신한다. 스크립트 생성과 domain reload는 발생하지 않는다. Game Source가 아직 없으면 Catalog 설정 오류는
사라지되 기존 `Create Game Source` 동작과 missing Game Source 진단은 그대로 남는다.

Provider가 하나 이미 있으면 인라인 구성 box를 표시하지 않는다. Provider가 여러 개인 경우에는 설정
생성이 중복 Provider 문제를 해결하지 못하므로 box를 표시하지 않고 `B3TAG3001` 후보 진단만 유지한다.

## 9. 오류와 상태 보존

- 빈 ID: 저장 없음, 경고 팝업 한 번
- settings/Provider ID 불일치: mutation, local build와 Play 차단
- settings save 예외: 기존 Workspace 유지, 경고 팝업 한 번
- 여러 Provider: 후보 타입 이름을 ordinal 순서로 표시
- Published Provider 없음: Player build inclusion 전에 실패
- Project Settings 읽기: 파일을 자동 생성하거나 Game Source를 수정하지 않음

Tag Editor에 저장하지 않은 태그 변경이 있어도 Catalog 설정 저장은 Game Source를 저장하거나 버리지 않는다.
Workspace refresh는 현재 in-memory Game Source session을 보존하는 기존 dirty refresh 규칙을 사용한다.

## 10. 테스트 계약

### 순수 Editor 단위/EditMode

- product name과 사용자 입력 정규화 결과
- 빈/기호-only ID 저장 거부
- 설정 파일이 없는 read가 파일을 생성하지 않음
- Apply 이후 저장값 readback
- Provider 0 + settings 없음은 `B3TAG3004`와 `RequiresCatalogConfiguration`
- Provider 0 + settings 있음은 완전한 development context
- Provider 1 + settings 없음은 기존 호환 동작
- Provider 1 + settings 일치는 외부 Source 병합
- Provider 1 + settings 불일치는 `B3TAG3002`
- Provider 2 이상은 `B3TAG3001` 후보 진단
- Published Provider 0은 settings 존재 여부와 무관하게 build 실패
- Project Settings provider가 등록되고 기존 저장값을 표시
- Tag Editor 인라인 저장 성공 후 Workspace 재해석
- 인라인 저장 실패 시 기존 session/dirty/selection 보존과 경고 한 번

### 회귀 검증

- 관련 Workspace, Window, Picker와 BuildPlayer fixture 전체
- generated Editor 및 Unity Tests 프로젝트 warning-as-error build
- 전체 Unity EditMode suite
- Unity runner가 변경하는 `ProjectSettings.asset`은 실행 전후 exact diff를 확인하고 runner 소유 변경만 복원

## 11. 완료 조건

- 새 게임 프로젝트에서 Provider 스크립트 없이 Catalog ID를 설정할 수 있다.
- 설정 저장 후 `B3TAG3001 ... found 0` 대신 완전한 development context가 생성된다.
- Game Source 생성과 Catalog 설정이 독립된 동작으로 유지된다.
- 기존 단일 코드 Provider 프로젝트는 설정 파일 없이 계속 동작한다.
- 기본 settings와 고급 Provider를 함께 쓰면 ID 일치가 강제된다.
- Published Player build는 실제 코드 Provider와 고정 artifact가 없으면 실패한다.
- 사용자 소유 Game Source, artifact와 unrelated ProjectSettings 변경은 커밋하지 않는다.
