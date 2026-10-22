# 2D 콜라이더 점 선택·삭제 도구 구현 계획

스펙: `docs/superpowers/specs/2026-10-23-unity-collider-point-selection-design.md`

## 작업

1. 패키지 뼈대: `unity/Packages/com.bun3.unity.colliders`에 `package.json`(0.1.0, unity 2022.3), Editor 전용 asmdef `Bun3.Unity.Colliders.Editor`, 테스트 asmdef `Bun3.Unity.Colliders.Editor.Tests`, README.
2. 테스트 먼저: `ColliderPointsTests`
   - 폴리곤 선택 점 삭제 시 나머지 순서 유지
   - 3개 미만으로 남는 경로는 경로째 삭제
   - 남는 경로가 없으면 거부하고 변경 없음
   - 엣지 점 삭제, 2개 미만이면 거부
   - Undo로 원래 점 복구
3. `ColliderPoint`, `ColliderPoints` 구현으로 테스트 통과.
4. `ColliderPointTool`과 두 하위 도구: 점 그리기, 클릭·드래그 선택, `SoftDelete`/`Delete` 명령 삭제, Esc 선택 해제, Undo 뒤 선택 비우기.
5. 검증: bun3-kit Unity 프로젝트(6000.3.14f1)에서 EditMode 테스트 1회 실행, 컴파일 경고 0. Unity가 만든 `.meta` 포함.
6. 커밋 후 `main` 푸시(사용자 승인). jurassic-paradise `manifest.json`에 git URL로 추가.
