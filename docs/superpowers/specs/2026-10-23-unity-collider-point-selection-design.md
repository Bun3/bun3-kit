# 2D 콜라이더 점 여러 개 선택·삭제 도구 설계 (`com.bun3.unity.colliders`)

- 날짜: 2026-10-23
- 상태: 확정
- 범위: 새 에디터 전용 패키지 `unity/Packages/com.bun3.unity.colliders` 0.1.0
- 첫 소비자: jurassic-paradise의 시야 콜라이더(`VisionBlocker` 레이어 Trigger `PolygonCollider2D`) 정리

## 1. 목적과 경계

Unity 기본 Edit Collider는 `PolygonCollider2D`·`EdgeCollider2D`의 점을 Ctrl+클릭으로 하나씩만 지운다. 이동 충돌에서 복사한 폴리곤처럼 점이 많은 콜라이더를 다듬을 때, Scene 뷰에서 점 여러 개를 골라 한 번에 지우는 도구를 둔다.

| 포함 | 제외(사용자 결정) |
|---|---|
| 점 클릭 선택, Shift/Ctrl 클릭 토글, 빈 곳 드래그 사각형 선택(Shift/Ctrl은 추가) | 점 이동·추가 — 기본 Edit Collider가 맡는다 |
| Delete(Edit > Delete 단축키)로 선택한 점 삭제, Undo | 자동 점 정리(허용 오차 단순화) |
| `PolygonCollider2D`(여러 경로), `EdgeCollider2D` | 콜라이더 하나에 분리된 영역 추가 |
| 활성 선택 콜라이더 하나 | 여러 콜라이더 동시 편집 |

## 2. 규칙

- 도구는 Scene 뷰 Tools 오버레이의 컴포넌트 도구(`EditorTool`)로, 해당 콜라이더를 선택했을 때만 나타난다.
- 삭제 후 점이 3개 미만인 폴리곤 경로는 경로째 지운다. 남는 경로가 없으면 아무것도 바꾸지 않고 경고를 남긴다.
- `EdgeCollider2D`는 점이 2개 미만으로 남으면 아무것도 바꾸지 않는다.
- 삭제는 `Undo.RecordObject`로 기록해 Ctrl+Z로 되돌리고 씬·프리팹 변경으로 남긴다. Undo/Redo 뒤에는 선택을 비운다.
- Delete는 Unity 표준 `SoftDelete`/`Delete` 명령으로 받는다. 선택한 점이 있을 때만 명령을 가로채므로, 점을 고르지 않았으면 GameObject 삭제가 그대로 동작한다.

## 3. 구조

- `ColliderPoint`: 경로 번호와 점 번호. `EdgeCollider2D`는 경로 0.
- `ColliderPoints`(정적): 경로 개수·점 읽기·월드 좌표 변환·삭제. 삭제 규칙을 담아 EditMode 테스트로 검증한다.
- `ColliderPointTool`(추상 `EditorTool`) + `PolygonColliderPointTool`·`EdgeColliderPointTool`: Scene 뷰 입력과 그리기만 맡는다. IMGUI 상호작용은 자동 테스트 대상에서 제외한다.
