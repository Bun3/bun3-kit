# Gameplay 유닛 메모리 축소 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 유닛당 메모리를 약 5.0KB에서 3KB 안팎으로 줄여, 캐시에 상주하는 유닛 수와 틱 사이 캐시 이탈 비용을 개선한다.

**Architecture:** 관찰 버퍼를 파이프라인 단위로 옮기고, 속성 슬롯에서 합산 캐시·수정자 목록을 떼어 필요한 슬롯에만
두며, 아키타입 단위 레이아웃과 시작 어빌리티를 공유한다.

**Tech Stack:** C# 9 / netstandard2.1, NUnit, `WorldStressBenchmarkTests`(Release).

**Spec:** `docs/superpowers/specs/2026-08-20-gameplay-slice3-ability-unit-world-design.md` §20

## Global Constraints

- 골든 해시 `0xC2D703366D22082E`·`0x85B06EF573A05FCB`와 스냅샷 복원 비트 동일 테스트 유지.
- 틱 경로 무할당(`AllocationSmokeTests`), 빌드 경고 0, 코드 영어·최소 주석, 0.14.0 유지(미퍼블리시).

## Review Focus

- 관찰 로그: 스폰 전 변경 미기록, 디스폰 뒤 미기록, 같은 틱 안 순서, 단독 파이프라인의 연결 시점.
- 슬롯: 처음 수정자가 붙는 슬롯, 마지막 수정자가 떨어진 슬롯, Override 마지막 승리, 클램프 전파 순서.
- 부여 공유: 한 유닛의 부여·회수가 같은 아키타입의 다른 유닛에 새지 않음, Version 동일.

---

### Task 1: 관찰 버퍼 파이프라인 단위화

**Files:** `Runtime/Attributes/AttributeChange.cs`, `AttributeSet.cs`, 새 `Runtime/Attributes/AttributeChangeLog.cs`(+.meta),
`Runtime/Effects/EffectLifecycleEvent.cs`, `EffectTarget.cs`, `EffectPipeline.cs`, `EffectPipeline.Events.cs`, `World.cs`; 테스트 4개 파일 이관.

- [ ] Step 1: 새 API로 실패하는 테스트 작성(월드 변경 로그에 Target·순서, 스폰 전·디스폰 뒤 미기록, 단독 파이프라인 기록).
- [ ] Step 2: 컴파일 실패(RED) 확인 → 구현 → 기존 테스트 이관 → 솔루션 전체 통과.
- [ ] Step 3: 메모리 분해 벤치로 전후 확인, 커밋.

### Task 2: 슬롯·수정자 재구성과 레이아웃 공유

**Files:** `Runtime/Attributes/AttributeSet.cs`, 새 `Runtime/Attributes/AttributeLayout.cs`(+.meta), `Runtime/ArchetypeCatalog.cs`,
`ArchetypeCatalogBuilder.cs`, `Runtime/Effects/EffectTarget.cs`, `Runtime/Unit.cs`, `Runtime/World.cs`.

- [ ] Step 1: 경계 사례 테스트 보강(첫 수정자·마지막 수정자 제거·Override 순서·아키타입 유닛 레이아웃 공유) — 기존 코드에서 통과 확인.
- [ ] Step 2: 구현, 골든 해시 불변, 변이 검사로 테스트 감지력 확인.
- [ ] Step 3: 벤치 전후, 커밋.

### Task 3: 아키타입 시작 어빌리티 공유

**Files:** `Runtime/Abilities/AbilitySet.cs`, `Runtime/ArchetypeCatalog.cs`, `ArchetypeCatalogBuilder.cs`, `Runtime/World.cs`.

- [ ] Step 1: 격리 테스트(한 유닛 부여·회수가 다른 유닛에 안 샘, Version 동일) — 기존 코드에서 통과 확인.
- [ ] Step 2: 구현, 변이 검사(복사 누락 시 실패), 솔루션 전체 통과.
- [ ] Step 3: 벤치 전후(메모리·부하·작은 월드 다수), 커밋.
