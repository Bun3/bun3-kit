# GameplayTag 에디터 Unreal 워크플로 설계

- 상태: 승인됨
- 작성일: 2026-08-13
- 적용 패키지: `Bun3.Gameplay`, `com.bun3.unity.window`
- 기반 명세: [`2026-08-12-gameplay-tag-catalog-design.md`](2026-08-12-gameplay-tag-catalog-design.md)
- 참고: [Unreal Engine Gameplay Tags](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-gameplay-tags-in-unreal-engine), [GameplayTagsEditor API](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Plugins/GameplayTagsEditor/IGameplayTagsEditorModule)

## 1. 목적과 범위

현재 GameplayTag 카탈로그 편집기를 Unreal Engine의 태그 관리 흐름에 가깝게 바꾼다. 작성자는
루트와 자식을 구분하지 않고 점으로 구분한 전체 경로 하나를 입력한다. 에디터는 전체 활성 태그를
계층 트리로 보여 주며, 암시적으로 만들어진 부모도 선택하고 관리할 수 있는 독립 태그로 취급한다.

같은 트리는 향후 Unity Inspector의 단일 `GameplayTag` 선택기에서도 사용한다. 이번 범위에서는
Inspector 직렬화와 PropertyDrawer를 구현하지 않지만, 관리 창에 종속되지 않는 트리 모델과 뷰를
만들어 이후 선택기에서 그대로 재사용할 수 있게 한다.

이름 변경으로 생기는 redirect는 런타임 호환성 데이터다. 목록을 보여 주는 데 그치지 않고 프로젝트
내 이전 경로 참조를 찾고, 참조가 없는 후보를 사용자가 확인해 제거할 수 있는 관리 흐름을 제공한다.
외부 세이브, 서버 설정과 이미 배포된 빌드는 에디터가 검사할 수 없으므로 자동으로 안전하다고
단정하거나 redirect를 무인 삭제하지 않는다.

## 2. 결정 요약

| 축 | 결정 |
|---|---|
| Tag Editor 메뉴 | `Gameplay/Tag Editor` |
| Overlay 검증 메뉴 | `Window/Validate Overlay Settings` |
| 추가 입력 | 전체 `Tag Name`과 `Comment` 한 쌍 |
| 트리 생성 | `.` 경계를 기준으로 모든 활성 노드를 자동 구성 |
| 암시 부모 | 명시 태그와 동일하게 선택·우클릭 가능한 독립 노드 |
| 행 메뉴 | Rename, Edit Comment, Add Sub-Tag, Copy Tag, Delete Tag |
| 이름 변경 | 마지막 세그먼트만 수정하고 부모 경로는 고정하며 전체 subtree를 함께 변경 |
| Redirect | 읽기 전용 `from → to`, 참조 검색과 확인 기반 제거만 허용 |
| 현재 참조 검색 | 프로젝트 소유 텍스트 파일의 exact 경로 후보 검색 |
| 향후 참조 검색 | Inspector 직렬화 필드의 구조화 검색과 안전한 참조 migration 추가 |
| Picker 기반 | 검색, 확장 상태, 양방향 스크롤을 포함한 공용 트리 모델·뷰 |

## 3. 메뉴와 창 구성

### 3.1 메뉴 경로

GameplayTag 관리 창의 메뉴는 `Bun3/Gameplay Tags`에서 `Gameplay/Tag Editor`로 옮긴다. 이후
Ability, Effect 등 gameplay 관련 도구가 같은 최상위 메뉴를 사용할 수 있다.

별도 `com.bun3.unity.window` 패키지의 Overlay 설정 검증 메뉴는
`Bun3/Window/Validate Overlay Settings`에서 `Window/Validate Overlay Settings`로 옮긴다.
두 변경은 기존 메뉴 alias를 남기지 않는다.

### 3.2 Tag Editor 세로 배치

관리 창은 위에서 아래로 다음 영역을 가진다.

1. New, Open, Reload, Save와 현재 파일 경로를 표시하는 도구 모음
2. 전체 태그 경로를 대소문자 무시로 검색하는 필터
3. `Add New Gameplay Tag` 영역의 `Tag Name`, `Comment`, `Add` 입력
4. 전체 활성 태그의 계층 트리
5. 접을 수 있는 읽기 전용 Redirect 목록과 관리 명령

창과 트리는 가로·세로 스크롤을 모두 제공한다. 긴 전체 경로와 comment가 창 너비 때문에 잘려도
가로 스크롤과 tooltip로 확인할 수 있어야 한다.

## 4. 공용 트리 모듈

관리 창의 파일·dirty 상태와 트리 표현을 분리한다.

### 4.1 `GameplayTagTreeModel`

카탈로그와 작성 세션으로부터 다음 정보를 갖는 불변 행 모델을 만든다.

- canonical identity와 표시용 전체 경로
- 마지막 세그먼트와 계층 깊이
- 부모·자식 관계
- comment
- JSON에 작성 행이 있는지 나타내는 explicit 여부
- 검색 결과인지, 검색 결과의 조상 문맥인지 여부

필터는 전체 경로를 ASCII 대소문자 무시로 검색한다. 검색 중에는 일치 행과 그 조상을 반환하고,
조상은 자동으로 펼친다.

### 4.2 `GameplayTagTreeView`

뷰는 계층 표시, 선택, 사용자 확장 상태, 가로·세로 스크롤과 context action event만 담당한다.
관리 창의 파일 저장이나 JSON mutation을 직접 호출하지 않는다. 다음 intent를 상위 소비자에게
알린다.

- `Selected`
- `RenameRequested`
- `CommentEditRequested`
- `SubTagRequested`
- `CopyRequested`
- `DeleteRequested`

검색이 비어 있을 때는 사용자의 expand/collapse 상태를 보존한다. 검색 중에는 결과 조상을 임시로
펼치고, 검색을 지우면 검색 전 상태로 복원한다.

향후 Inspector picker는 같은 모델과 뷰를 selection-only mode로 사용한다. Inspector의 한 줄
필드를 누르면 크기 조절 가능한 popup 또는 utility window를 열고, 이름 필터와 양방향 스크롤을
제공한다. picker의 실제 구현과 직렬화 소유권은 이번 범위가 아니다.

## 5. 태그 추가와 행 동작

### 5.1 추가

루트와 자식 전용 입력을 제거한다. 작성자는 `State.Movement.Run` 같은 전체 경로와 comment를
입력하고 `Add`를 누른다. 저장되는 작성 행은 입력한 전체 경로 하나뿐이다. `State`와
`State.Movement`는 카탈로그가 암시 부모로 생성하며 즉시 트리에 나타난다.

중복, 문법, 깊이, 길이와 최대 노드 수 검증은 공용 `TagCatalog` 계약을 사용한다. 실패하면 세션,
선택, 입력값과 dirty 상태를 보존하고 기존 validation UI로 원인을 표시한다.

### 5.2 공통 context menu

명시 태그와 암시 부모를 포함한 모든 태그 행은 다음 메뉴를 제공한다.

- `Rename`: 마지막 세그먼트 이름 변경
- `Edit Comment`: comment 변경
- `Add Sub-Tag`: 하위 태그 입력 준비
- `Copy Tag`: 전체 경로 복사
- `Delete Tag`: 태그 또는 subtree 삭제

`Add Sub-Tag`는 즉시 태그를 만들지 않는다. 선택한 전체 경로 뒤에 `.`을 붙여 `Tag Name` 입력에
채우고 해당 입력에 focus한다. `Copy Tag`는 표시용 전체 경로를 시스템 clipboard에 넣는다.

암시 부모에서 comment를 편집하면 동일 경로의 explicit JSON 행을 추가한다. 빈 comment로 편집해도
작성 의사를 보존하기 위해 explicit 행으로 승격한다.

### 5.3 Unreal 방식 이름 변경

이름 변경 창은 선택 태그의 부모 경로를 읽기 전용으로 보여 주고, 편집 가능한 `Tag Name`에는 마지막
세그먼트만 넣는다. 루트 태그에는 부모 경로가 없다. 새 세그먼트에는 `.`을 허용하지 않으며 기존
태그 이름 문법을 적용한다.

예를 들어 `State.Movement.Run`의 입력값을 `Sprint`로 바꾸면 결과는
`State.Movement.Sprint`다. 이름 변경 창에서 `State.Other.Sprint`처럼 다른 부모로 이동할 수 없다.

선택 노드와 모든 descendant는 한 트랜잭션으로 함께 이름을 바꾼다. JSON의 explicit 작성 행을
새 경로로 고치고, 변경 전 존재했던 모든 활성 경로에서 대응하는 새 활성 경로로 direct redirect를
만든다. 암시 부모도 선택·직렬화 가능한 태그로 취급하므로 변경 전 활성 경로에 포함한다. 이동된
subtree를 가리키던 기존 redirect target도 새 최종 경로로 다시 쓴다.

대소문자만 변경하는 경우에는 표시 casing만 바꾸고 redirect를 만들지 않는다. 목적 경로 충돌,
redirect source 충돌 또는 카탈로그 검증 실패가 하나라도 있으면 변경 전체를 거부하고 이전 세션을
보존한다.

### 5.4 삭제

leaf는 확인 후 해당 explicit 작성 행을 삭제한다. descendant가 있는 행은 subtree 삭제임을 명확히
알리는 별도 확인을 요구하고, 승인되면 해당 경로 아래 explicit 작성 행을 모두 삭제한다. 삭제된
경로를 target으로 삼는 redirect는 dangling 상태가 되지 않도록 같은 트랜잭션에서 제거한다.

삭제는 rename이 아니므로 자동 redirect를 만들지 않는다. 삭제 확인을 취소하거나 검증이 실패하면
아무 상태도 바꾸지 않는다.

## 6. Redirect 표시와 수명 관리

### 6.1 표시 계약

Redirect 영역은 `Old Tag → New Tag` 행을 대소문자 무시 old path 순서로 보여 준다. `from`과
`to`를 inline 편집할 수 없으며 수동 Redirect 추가도 제공하지 않는다. Redirect는 rename
트랜잭션만 생성·갱신한다.

각 행은 `Find References`와 `Remove Redirect` 명령을 제공한다. 영역 도구 모음은
`Find All References`와 `Remove Obsolete Redirects`를 제공한다. 여기서 읽기 전용은 mapping의
임의 수정을 금지한다는 뜻이며, 검증된 제거 작업은 허용한다.

### 6.2 현재 단계의 text reference provider

현재는 GameplayTag Inspector 직렬화 형식이 없으므로 정확한 필드 참조를 식별할 수 없다. 이번
단계의 provider는 Unity 프로젝트가 소유한 읽을 수 있는 텍스트 파일에서 redirect의 이전 전체
경로와 일치하는 token을 찾는다.

- `Assets`, `ProjectSettings`, embedded/local package의 텍스트 소스를 검색한다.
- `Library`, `Temp`, package cache, 빌드 산출물, `.meta`와 binary 파일은 제외한다.
- 현재 열린 GameplayTag 카탈로그의 `redirects` 배열 자체는 self-match가 되지 않게 제외한다.
- ASCII 대소문자를 무시하되 전체 태그 token 경계가 일치해야 한다. `State.Old` 검색이
  `State.Old.Child`를 참조로 세지 않는다.
- 모든 redirect를 검색할 때 파일을 redirect 수만큼 반복해서 읽지 않는다. 파일을 한 번 순회하며
  후보 tag token을 canonical set에 대조한다.
- 결과는 경로, line, 미리보기와 일치한 old tag를 담고 클릭 시 해당 asset을 ping하거나 파일을 연다.

텍스트 결과는 주석이나 일반 문장도 잡을 수 있으므로 `Text Match` 후보로 표시한다. 검색 취소와
읽기 오류는 부분 성공으로 간주하지 않는다. 검색이 완전하게 끝나지 않으면 제거 가능 판정을
내리지 않고 오류 또는 취소 상태를 보여 준다.

### 6.3 향후 structured reference provider

Inspector 직렬화가 추가될 때 알려진 GameplayTag 경로 property를 검색하는 provider를 같은 검색
서비스에 추가한다. 구조화 결과는 component/object, property path와 asset 위치를 제공하는
`Serialized Reference`로 표시한다.

`Migrate References`는 구조화 결과만 Undo 가능한 Unity 직렬화 변경으로 새 경로에 갱신한다. 일반
코드, 임의 JSON과 text match는 자동 치환하지 않고 검색 결과만 제공한다. 이 확장은 트리 picker와
Inspector 직렬화 설계에서 구현하며 이번 단계의 완료 조건에는 포함하지 않는다.

### 6.4 제거 정책

`Remove Redirect`는 먼저 해당 old path의 최신 참조 검색을 수행한다. match가 있으면 결과를 먼저
보여 주고 `Open References`, `Remove Anyway`, `Cancel` 중 하나를 명시적으로 고르게 한다. 이는
주석이나 일반 문장인 text match 때문에 실제로 불필요한 redirect를 영원히 제거하지 못하는 상황을
피하기 위한 개별 override다. 검색이 취소되거나 불완전하면 `Remove Anyway`도 제공하지 않는다.

`Remove Obsolete Redirects`는 모든 redirect를 한 번에 검색하고 프로젝트 내부 match가 0인 항목만
제거 후보로 보여 준다. 일괄 작업에는 referenced 항목을 강제로 포함하는 기능을 제공하지 않는다.

`Obsolete`는 프로젝트 검사 결과를 뜻할 뿐 전역 안전을 뜻하지 않는다. 제거 확인 창에는 세이브
데이터, 서버 설정, 외부 파일과 배포된 빌드는 검사하지 못했다는 경고를 항상 표시한다. 사용자가
선택한 후보만 제거하며, 검색이 취소됐거나 일부 파일을 읽지 못했으면 일괄 제거를 허용하지 않는다.

제거는 편집 세션 mutation으로 처리해 창을 dirty로 만들고 기존 Save/Discard/Cancel lifecycle을
그대로 따른다. 에디터는 참조가 있다는 이유로 redirect를 자동 삭제하지도, 참조가 없다는 이유로
무인 삭제하지도 않는다.

## 7. Redirect 런타임 성능 계약

Redirect는 계속 런타임 의미에 포함된다. `TagCatalog.TryGet`은 활성 이름을 먼저 찾고 실패한 경우에만
exact redirect dictionary를 조회한다. 정상적인 새 경로 조회에는 redirect 수와 무관하게 추가 lookup이
없고, 한번 해석된 `GameplayTag`와 두 컨테이너의 핫패스는 redirect에 접근하지 않는다.

로드 시간, JSON 크기와 메모리는 redirect 수에 비례하고 fingerprint용 canonical 정렬은
`O(R log R)`이다. 따라서 redirect는 영구 축적하는 별칭 목록이 아니라 이전 문자열 참조를
migration하는 동안 유지하는 호환성 데이터로 취급한다. exact mapping 계약은 유지하며 wildcard나
prefix redirect를 추가하지 않는다. 새 이름의 미래 descendant가 과거에 존재하지 않았던 old
경로로 뜻밖에 해석되는 것을 막기 위해서다.

이번 변경은 런타임 lookup 자료구조나 fingerprint 형식을 바꾸지 않는다. 에디터에는 redirect 수를
표시하고 reference/cleanup 흐름을 제공해 누적을 관리한다.

## 8. 상태, 오류와 동시성

태그 추가, comment 변경, rename, delete와 redirect 제거는 모두 현재 작성 세션을 복제한 뒤 mutation,
직렬화, 공용 `TagCatalog` 재검증을 완료한 후보만 교체하는 원자적 흐름을 사용한다. 실패 시 현재
세션, 선택, 트리 확장 상태, 입력과 dirty 상태를 보존한다.

파일 저장과 창 lifecycle은 기존 dirty 보호 계약을 그대로 사용한다. Reference Find는 읽기 전용
Editor 작업이며 취소할 수 있어야 한다. 진행 UI는 예외나 취소 시에도 반드시 닫고 어떤 파일도
수정하지 않는다. 향후 migration만 Unity Undo와 asset save를 사용한다.

## 9. 테스트와 완료 조건

### 9.1 트리와 메뉴

- 두 Unity 메뉴 경로가 새 경로로 열리고 기존 경로는 존재하지 않는다.
- 단일 전체 경로 추가가 explicit 행 하나와 올바른 암시 부모 트리를 만든다.
- 명시·암시 행 모두 동일한 context menu intent를 발생시킨다.
- Add Sub-Tag가 `선택 경로 + "."`를 입력하고 focus하며 태그를 즉시 만들지 않는다.
- Copy Tag가 전체 표시 경로를 clipboard에 복사한다.
- 검색은 대소문자를 무시하고 결과와 조상만 보여 주며 임시 확장 후 사용자 상태를 복원한다.
- 긴 행에서 가로·세로 스크롤이 가능하다.

### 9.2 편집 mutation

- 이름 변경 창은 마지막 세그먼트만 편집하고 부모 경로를 보존한다.
- 부모 rename이 모든 active descendant, explicit 작성 행과 기존 redirect target을 원자적으로 갱신한다.
- 이름이 바뀐 각 이전 활성 경로는 새 활성 경로로 direct redirect된다.
- 대소문자만 바꾼 rename에는 redirect가 생기지 않는다.
- 충돌하거나 무효한 이름 변경은 세션을 전혀 바꾸지 않는다.
- 암시 부모 comment 편집이 explicit 행으로 승격한다.
- subtree 삭제 확인, 취소와 dangling redirect 제거가 계약대로 동작한다.

### 9.3 Redirect 관리

- Redirect mapping은 UI에서 편집할 수 없고 rename 이외 경로로 추가되지 않는다.
- text provider가 대소문자를 무시한 exact token만 찾고 descendant prefix 오탐을 만들지 않는다.
- catalog redirect self-match, binary, cache와 생성 산출물을 제외한다.
- 전체 검색이 대상 파일을 한 번만 순회하고 redirect별 결과를 올바르게 분류한다.
- 결과에서 asset ping 또는 파일 열기가 가능하다.
- 취소 또는 읽기 실패가 있는 scan은 obsolete 제거를 허용하지 않는다.
- 참조가 있는 개별 항목은 결과 확인과 `Remove Anyway` 선택 없이는 제거되지 않는다.
- 참조 0 후보도 외부 데이터 경고와 명시적 선택 없이는 제거되지 않는다.
- 제거 후 dirty lifecycle과 저장 실패 보존 계약이 유지된다.

### 9.4 회귀 검증

- 전체 .NET 테스트가 실패 없이 통과한다.
- Unity EditMode 전체 테스트가 실패 없이 통과한다.
- 관련 Release build가 경고 0으로 통과한다.
- 기존 TagCatalog redirect lookup, fingerprint와 컨테이너 무할당 계약이 변하지 않는다.

## 10. 호환성과 버전

JSON schema와 런타임 public API는 바뀌지 않는다. `Bun3.Gameplay`에는 큰 Unity Editor 기능이
추가되므로 NuGet과 UPM 버전을 함께 `0.7.0`으로 올린다. `com.bun3.unity.window`는 메뉴 경로만
바뀌는 patch change이므로 `0.2.1`로 올린다. publish와 push는 별도 요청 없이는 수행하지 않는다.

## 11. 비목표

- 이번 단계에서 GameplayTag PropertyDrawer 또는 Inspector picker 구현
- 이번 단계에서 구조화된 Unity 참조 검색과 자동 migration 구현
- 코드, 임의 JSON 또는 text match의 자동 문자열 치환
- 세이브, 서버 설정, 외부 저장소와 이미 배포된 빌드의 자동 검색
- 참조가 없다는 추론만으로 수행하는 자동 redirect 삭제
- redirect chain, wildcard 또는 prefix redirect 도입
- 이름 변경 창에서 태그를 다른 부모 아래로 이동

## 12. 기각한 대안

### 12.1 Redirect를 만들지 않고 카탈로그 JSON만 변경

카탈로그 자체는 단순하지만 Scene, Prefab, ScriptableObject, 세이브와 외부 설정에 남은 이전 경로를
복원할 수 없다. 향후 Inspector 직렬화를 계획하고 있으므로 호환성 경계를 없애지 않는다.

### 12.2 Redirect를 영구 보존하고 관리 도구를 제공하지 않음

런타임 exact lookup은 빠르지만 JSON, 로드 시간과 메모리가 rename 이력에 따라 계속 증가한다.
참조 검색과 확인 기반 cleanup으로 수명을 관리한다.

### 12.3 현재 단계에서 text match를 자동 치환

일반 문자열과 주석을 GameplayTag 참조로 확정할 수 없어 잘못된 파일을 수정할 수 있다. 현재는
후보 위치만 제공하고, 향후 알려진 직렬화 property에만 Undo 가능한 migration을 적용한다.
