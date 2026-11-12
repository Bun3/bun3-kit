# 피드백 모션 Juice 구현 계획

> 스펙: [2026-11-12-unity-core-juice-design.md](../specs/2026-11-12-unity-core-juice-design.md)
> 브랜치: `Bun3/juice-motion`

1. `unity/Packages/com.bun3.unity.core/Runtime/Motion/Juice.cs`에 스펙 §3 API를 둔다. 쥬라기 낙원 `UiJuice`의 계산식을 그대로 옮기고, 숨은 GameObject 대신 `PlayerLoopSystemHelper` 틱을 쓴다.
2. `Tests/Runtime/JuiceTests.cs`: 펀치 재호출 뒤 정확한 크기 복원, 흔들림 위치 복원, 섬광 색 복원과 파괴된 대상 무시.
3. `package.json` 0.6.0과 설명, `CHANGELOG.md`, `README.md`, `Documentation/unity.core.md` 갱신.
4. kit Unity 프로젝트에서 배치 모드로 `Bun3.Unity.Core.Tests`를 실행하고 컴파일 경고 0을 확인한다.
5. 퍼블리시 뒤 쥬라기 낙원에서 `packages-lock.json`을 갱신하고 게임 쪽 `UiJuice.cs`를 지운다(현재 게임 안 호출부 없음).
