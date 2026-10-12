# Gameplay 틱 페이즈 순회 최적화 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ②④ 페이즈가 활성 인스턴스 전부 대신 필요한 인스턴스만 훑고, ③④⑤를 유닛 단위 한 순회로 합쳐
대규모 월드의 틱 비용을 줄인다. 관찰 가능한 동작·결정론은 그대로다.

**Architecture:** `EffectTarget`이 활성 목록 옆에 Id 순 부분 목록 두 개(시간 진행·Ongoing)를 유지하고,
`EffectPipeline`의 페이즈 루프가 그것을 순회한다. ③④⑤는 유닛별 `RebuildDirty → Ongoing → RebuildDirty`.

**Tech Stack:** C# 9 / netstandard2.1, NUnit, `WorldStressBenchmarkTests`(Release).

**Spec:** `docs/superpowers/specs/2026-08-20-gameplay-slice3-ability-unit-world-design.md` §19

## Global Constraints

- 동작 불변: 골든 해시 `0xC2D703366D22082E`·`0x85B06EF573A05FCB`와 스냅샷 복원 비트 동일 테스트 유지.
- 틱 경로 무할당(`AllocationSmokeTests` 통과), 빌드 경고 0, 코드 영어·최소 주석.
- 0.14.0 미퍼블리시 — 버전 유지.

## Review Focus

- 제거 경로(만료·`RemoveById`·`RemoveByTags`·`RemoveOnApplyTags`·스냅샷 복원)에서 부분 목록이 활성 목록과 어긋나지 않는다.
- `RemoveOneAndRefresh` 스택 만료는 인스턴스를 남기므로 시간 진행 목록에도 남는다.
- 같은 틱에 만든 인스턴스(`CreatedTick == CurrentTick`) 건너뛰기가 그대로다.
- ④ 토글 중 활성 목록이 바뀌지 않는다는 기존 전제를 유지한다(부분 목록 순회 중 변경 없음).

---

### Task 1: 페이즈별 부분 목록

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Effects/EffectTarget.cs` — `InsertActive/RemoveActive`가
  `CompiledEffect`를 받아 `TimedEffects`·`OngoingEffects`를 함께 갱신, 복원 시 재구성.
- Modify: `common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.cs` — 호출부, `AdvanceTime`·`EvaluateOngoing`이 부분 목록 순회.

- [ ] Step 1: 회귀 감지력 확인 — 부분 목록 제거를 일부러 빠뜨린 상태에서 기존 스위트(골든 해시)가 실패하는지 본다.
- [ ] Step 2: 구현 후 `dotnet test common/tests/Bun3.Gameplay.Tests` 전부 통과(골든 해시 불변).
- [ ] Step 3: 커밋.

### Task 2: ③④⑤ 유닛 단위 한 순회

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.cs` — `RunPhases`, 유닛별 정착 루프.

- [ ] Step 1: 구현 후 Gameplay 스위트 전부 통과(골든 해시 불변), 솔루션 빌드 경고 0.
- [ ] Step 2: 부하 벤치 전후 측정(Release, normal·intense) — 커밋 메시지에 기록.
- [ ] Step 3: 커밋.
