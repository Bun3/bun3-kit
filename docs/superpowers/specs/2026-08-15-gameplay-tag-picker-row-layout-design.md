# GameplayTag Picker 선택 표시와 행 레이아웃 설계

## 목표

Inspector에서 여는 병합 GameplayTag Picker의 현재 선택 태그를 즉시 알아볼 수 있게 하고,
태그 이름 옆에 붙어 있던 Source 개수 표기를 행 오른쪽 끝으로 분리한다.

## 행 표현

- 현재 값과 canonical path가 일치하는 태그 행은 이름 왼쪽에 체크 아이콘을 표시한다.
- 체크 이미지는 Unity 내장 `TestPassed` 아이콘을 우선 사용한다.
- Unity 버전에서 내장 이미지를 찾지 못하면 `✓` 글리프로 대체한다.
- 기존 TreeView 선택 하이라이트와 foldout/disclosure 위치는 변경하지 않는다.
- 선택되지 않은 행에는 체크 자리나 빈 아이콘을 강제로 표시하지 않는다.
- 태그 이름은 왼쪽 영역에 표시한다.
- `1 source` 또는 `N sources`는 별도 영역에서 행 오른쪽 끝에 정렬한다.
- 창 폭이 좁아지면 Source 개수 영역은 유지하고 태그 이름 영역만 먼저 줄어든다.
- 태그 전체 canonical path와 Source 상세 tooltip은 기존과 동일하게 제공한다.

## 상태와 책임

`GameplayTagPickerRow`는 Runtime Catalog projection 데이터만 유지한다. 선택 여부는 행 모델에
저장하지 않는다. `GameplayTagPickerTreeView`가 현재 raw 경로를 보유하고 각 행의 canonical
path와 대소문자를 무시해 비교한다.

초기화, 필터 적용, live Workspace 갱신 후에도 현재 경로를 다시 동기화한다. Picker에서 새
태그를 선택하면 callback을 호출하기 전에 TreeView의 현재 경로도 갱신한다. 따라서 창을 닫지
않는 programmatic 선택 경로에서도 체크 표시가 즉시 바뀐다.

## 렌더링 경계

Picker TreeView만 행 렌더링을 확장한다. Source 기반 Tag Editor TreeView와 공용 projection
모델에는 선택 아이콘이나 우측 badge 개념을 추가하지 않는다.

행의 disclosure 이후 label rect를 기준으로 다음 두 영역을 계산한다.

1. 오른쪽 끝에서 Source 개수 텍스트의 실제 너비만큼 Source 영역을 확보한다.
2. 일정 간격을 제외한 나머지 왼쪽 영역을 태그 이름과 체크 아이콘에 사용한다.

이 geometry 계산은 GUI 상태와 분리해 EditMode 테스트에서 직접 검증할 수 있게 한다.

## 실패와 호환성

- 빈 값, malformed raw 값, 현재 Catalog에 없는 값은 어떤 태그 행에도 체크를 표시하지 않는다.
- redirect 등으로 raw 경로와 표시 행 경로가 직접 일치하지 않으면 기존 선택 동기화 규칙을
  유지하며 자동으로 다른 경로를 체크하지 않는다.
- Source 개수와 tooltip 내용은 변경하지 않고 위치만 변경한다.
- 전용 이미지 에셋이나 새 직렬화 필드는 추가하지 않는다.

## 검증

- 선택 경로와 일치하는 행에만 체크 이미지가 지정되는지 검증한다.
- 내장 아이콘이 없을 때 `✓` fallback이 표시되는지 검증한다.
- Source 영역의 오른쪽 끝이 행 label 영역의 오른쪽 끝과 일치하는지 검증한다.
- Source 영역과 이름 영역이 겹치지 않는지 검증한다.
- 필터와 Workspace 갱신 후에도 현재 경로가 유지되는지 검증한다.
- 기존 Picker EditMode 테스트와 전체 Unity EditMode suite를 실행한다.
- 생성된 Unity Editor/Test 프로젝트를 warning-as-error로 빌드한다.
