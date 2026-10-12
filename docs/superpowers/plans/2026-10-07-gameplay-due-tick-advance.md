# Gameplay 기한 기반 시간 진행 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ② 시간 진행이 기한이 된 유닛만 처리하도록 바꿔, 현실 인구 틱 비용의 최대 항목(36~48%)을 줄인다.

**Architecture:** 유닛별 `NextDueTick`·`AdvancedThrough`와 소유 파이프라인. 카운터는 상대값 그대로 두고 처리·읽기·병합
직전에 밀린 틱을 한꺼번에 뺀다(따라잡기).

**Tech Stack:** C# 9 / netstandard2.1, NUnit, `WorldStressBenchmarkTests.Realistic_population_*`(Release).

**Spec:** `docs/superpowers/specs/2026-08-20-gameplay-slice3-ability-unit-world-design.md` §21

## Global Constraints

- 골든 해시 `0xC2D703366D22082E`·`0x85B06EF573A05FCB`, 스냅샷 복원 비트 동일 유지.
- 틱 경로 무할당, 빌드 경고 0, 코드 영어·최소 주석, 0.14.0 유지.

## Review Focus

- 기한이 늦게 계산되는 경로(카운터를 바꾸면서 기한을 되돌리지 않음) — 발화·만료 누락.
- 이번 틱 생성 인스턴스의 따라잡기 양, RemoveOneAndRefresh 뒤 기한.
- 파이프라인 이동·복원·디스폰 때 기준 시점.

---

### Task 1: 경계 테스트와 방문 계약

- [ ] 유휴 뒤 남은 틱·주기 발화 시점, 유휴 뒤 병합, 디스폰 중 정지, 유휴 뒤 쿨다운 조회 테스트(기존 코드에서 통과 확인).
- [ ] 진단 카운터 `AdvanceVisitCount`로 "기한 아닌 유닛 미방문" 테스트(RED).

### Task 2: 지연 차감 구현

- [ ] `EffectTarget` 기한·기준·소유자, 파이프라인 `CatchUp/Track/Untrack`, `AdvanceTime` 기한 처리, 병합·삽입·복원의 기한 리셋.
- [ ] 읽기 경로 따라잡기, 테스트·해시의 직접 저장값 읽기를 관찰값으로 이관.
- [ ] 골든 해시 불변, 변이 검사, 솔루션 통과, 현실 인구 벤치 전후 비교 후 커밋.
