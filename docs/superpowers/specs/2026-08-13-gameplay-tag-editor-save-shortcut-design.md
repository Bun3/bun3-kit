# GameplayTag 에디터 저장 단축키 설계

- 상태: 검토 대기
- 작성일: 2026-08-13
- 적용 패키지: `com.bun3.gameplay`

## 목적

`GameplayTagCatalogWindow`가 포커스를 가진 동안 `Ctrl+S`를 누르면 Unity의 일반 저장 대신
현재 GameplayTag 카탈로그 JSON을 저장한다. macOS에서는 같은 의미의 `Cmd+S`를 지원한다.

## 동작

1. 창의 `OnGUI`가 `KeyDown` 이벤트에서 Shift·Alt가 없는 `Ctrl+S` 또는 `Cmd+S`를 감지한다.
2. 일치하는 이벤트는 카탈로그 상태와 관계없이 소비하여 Unity 씬·에셋 저장으로 전파하지 않는다.
3. 카탈로그가 열려 있고 변경 사항이 있을 때만 기존 `SaveChanges()` 경로를 실행한다.
4. 카탈로그가 없거나 변경 사항이 없으면 파일을 쓰지 않고 종료한다.
5. 저장 실패는 기존 검증 경고 창으로 알리고 dirty 상태를 유지한다.

창의 `OnGUI`가 키 이벤트를 받는 범위만 처리하므로 다른 Unity 창에 포커스가 있을 때의
`Ctrl+S` 동작은 바꾸지 않는다. 전역 `MenuItem` 단축키와 Shortcut Manager 등록은 범위 밖이다.

## 검증

사용자 결정에 따라 신규 자동화 테스트는 추가하지 않는다. Unity 에디터 어셈블리 컴파일과
포커스된 Tag Editor에서의 수동 단축키 동작으로 검증한다.
