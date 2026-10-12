# Gameplay 미룬 정리 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 슬라이스 3 리뷰에서 미룬 항목 중 동작 결함 4건과 문서 2건을 정리한다.

**Architecture:** 공개 API 동작 보정(호출 시점 검증, 무변경 재부여, 복원 시 대기 작업 폐기)과 선택 인터페이스
`IRestorableRng` 추가. 새 파일 없음(`IRestorableRng`는 `IRng.cs`의 형제 타입).

**Tech Stack:** C# 9 / netstandard2.1, NUnit (`common/tests/Bun3.Gameplay.Tests`).

**Spec:** `docs/superpowers/specs/2026-08-20-gameplay-slice3-ability-unit-world-design.md` §22

## Global Constraints

- 골든 해시 `0xC2D703366D22082E`·`0x85B06EF573A05FCB`, 스냅샷 복원 비트 동일 유지.
- 틱 경로 무할당, 빌드 경고 0, 코드 영어·최소 주석, 모든 public 멤버 XML 문서, 0.14.0 유지(미퍼블리시).
- 테스트: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`.

## Review Focus

- 복원 실패 경로(유닛 불일치·RNG 길이 불일치)가 대기 작업·RNG를 이미 바꿔 버리지 않는지 — 검증 전 무변경.
- 복원이 비운 관찰 버퍼와 `PendingActivations`가 다음 틱 결과와 섞이지 않는지.
- 0 상태·짧은 버퍼 등 `XorShiftRng.RestoreState`/`SaveState` 경계 입력.
- `EnqueueApply` 음수 id, 카탈로그 크기와 같은 id.
- 같은 레벨 재부여가 아키타입 공유 목록을 복사하지 않는지(공유 상태 유지).

---

### Task 1: 효과 id 호출 시점 검증

- [ ] RED: `World.EnqueueApply`·`EffectPipeline.EnqueueApply`에 카탈로그 밖 id(음수, `Count`)를 넣으면
      `ArgumentOutOfRangeException`, 대기 명령·적용 큐는 그대로(0).
- [ ] GREEN: 두 public 진입점에 `(uint)effectId >= (uint)catalog.Count` 검사. 내부 오버로드(체인)는 그대로.
- [ ] 커밋.

### Task 2: 같은 레벨 재부여는 무변경

- [ ] RED: 같은 출처·같은 레벨 재부여 뒤 `Version` 불변. 아키타입 공유 세트에서 같은 재부여 뒤 공유 유지
      (`AbilitySet` 내부 공유 여부를 테스트에서 확인할 수 있게 internal 조회 추가).
- [ ] GREEN: `exists && level == 기존 레벨`이면 즉시 반환.
- [ ] 커밋.

### Task 3: 복원은 대기 작업을 버린다

- [ ] RED: 스냅샷 뒤 명령 적재 → 복원 → `PendingCommandCount == 0`, 다음 틱에 명령 효과 없음.
      복원 뒤 `PendingActivations`·관찰 버퍼 3종 비어 있음. 예산 초과로 남은 적용 큐도 0.
- [ ] GREEN: `RestoreSnapshot` 검증 통과 뒤 `_commands.Clear()`, `_activationCount = 0`, 파이프라인 적용 큐·관찰 버퍼 비움.
- [ ] 커밋.

### Task 4: `IRestorableRng`

- [ ] RED: `XorShiftRng` 저장→복원 뒤 같은 수열, 길이 불일치·0 상태·짧은 대상 버퍼 거부.
      World 스냅샷 뒤 RNG 진행 → 복원 → 다음 값이 스냅샷 직후 값과 같음. 복원 불가 RNG로 `CreateSnapshot` 거부.
      RNG 길이 불일치 스냅샷 복원은 아무것도 바꾸지 않고 거부.
- [ ] GREEN: `IRng.cs`에 인터페이스, `XorShiftRng` 구현, `WorldSnapshot.RngState`, World 저장·복원.
- [ ] 커밋.

### Task 5: 문서

- [ ] `Spawn`·`Despawn`·`Tick` XML 문서 보강(§22.4). 빌드 경고 0 확인.
- [ ] 커밋.
