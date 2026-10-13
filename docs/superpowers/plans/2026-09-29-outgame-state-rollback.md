# Outgame State Rollback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 가변 상태의 최초 값 복원과 Transaction 미사용 시 롤백 보관 비용 제거를 검증한다.

**Architecture:** 기존 즉시 적용/이전 값 복원 모델을 유지한다. 첫 가변 편집은 최초 상태의 독립 복사본을 확보하고, 모듈별 롤백 전용 자료구조는 첫 트랜잭션 변경에서 생성해 재사용한다.

**Tech Stack:** .NET, C# 9/netstandard2.1 packages, NUnit/net10.0 tests.

**Spec:** `docs/superpowers/specs/2026-09-29-outgame-state-rollback-design.md`

## Global Constraints

- 기존 미커밋 수정 보존. 이번 작업에서 stage/commit/push/publish 금지.
- 공개 시그니처 유지. Items 0.4.0, Achievements 0.4.1, Core 기존 0.7.0 유지.
- 패키지 코드/주석/public XML은 영어. netstandard2.1/C# 9, 경고 0.
- 임의 객체 그래프 자동 추적이나 외부 참조 제자리 복원 기능을 추가하지 않는다.
- 구현 에이전트는 하위 에이전트를 만들지 않는다. 컨트롤러가 별도 리뷰한다.

## Review Focus

- 클래스 교체 전후가 리스트를 공유해도 최초 값이 복원되는가: Task 1 회귀 테스트.
- 반복 scope/교체가 최초 깊은 복사본을 덮어쓰거나 다시 복사하지 않는가: Task 1 호출 수/내용 검증.
- 복사 델리게이트가 실패해도 후속 scope 및 다음 트랜잭션이 정상 동작하는가: Task 1 예외 테스트.
- 미사용/비활성/빈 트랜잭션이 보관 자료구조를 만들지 않는가: Task 1 할당 및 독립 사용 검증.
- lazy 업적 보관이 커밋 알림 재진입/예외 처리와 재사용을 깨뜨리지 않는가: Task 1 기존 OutgameNotification/Transaction 테스트 및 반복 commit/rollback.

### Task 1: 가변 상태 복원과 선택적 보관 구현

**Files:**
- Modify: `server/src/Bun3.Server.Items/ItemInventory.Transaction.cs`, `ItemInventory.cs`, `ItemInstance.cs`, `ItemStateScope{TState}.cs`, `Bun3.Server.Items.csproj`.
- Modify: `server/src/Bun3.Server.Achievements/AchievementManager.Transaction.cs`, `AchievementManager.cs`, `Bun3.Server.Achievements.csproj`.
- Modify: `server/tests/Bun3.Server.Tests/ItemStateRollbackTests.cs`.
- Create: `server/tests/Bun3.Server.Tests/OutgameRollbackStorageTests.cs` if needed for cost/standalone tests.
- Controller-owned: spec/plan, `docs/outgame-rollback.md`, historical spec/research pointers.

**Interfaces:**
- Keep `Func<TState, TState>? snapshotState`, `Transaction? transaction = null`, `BeginStateChange()`, `InventoryTransaction` signatures.
- First mutable scope clones saved baseline irrespective of class/struct/reference identity, once per instance per transaction.
- Private lazy storage per module owns rollback-only collections. Keep notification ordering and cleanup established by prior work.

- [x] **Step 1: Add failing behavior tests before product changes.** Class original `Level=1, Options=[10]`; replace with `Level=5` and same list, scope adds `30`; dispose transaction => level 1/options [10], not same mutable state reference. Add repeated replacement/scopes => snapshot delegate called once; second transaction captures new baseline. Adapt two existing mixed-path identity assertions to value restoration, and preserve separate setter-only identity assertion. Verify struct reference fields remain covered by existing regression. Add throwing snapshot delegate then retry and rollback => original values, reusable next transaction.
- [x] **Step 2: Add meaningful lazy/standalone checks.** Measure warmed constructor/ordinary operation allocation with cached delegates and compare null vs inactive Transaction. For AchievementManager compare constructor cost at large catalog sizes against required AchievementState array so an eager extra bool array is detectable (derive object/array cost at runtime, avoid platform constants). For Items use measured baseline or first-active delta to show storage allocation moves to first active mutation and does not occur for empty scopes; avoid reflection/private field assertions. Validate actual state and immediate notifications alongside costs. Existing builder and no-transaction state scope remain green.
- [x] **Step 3: Run focused tests and record RED in this plan's workspace.** Use `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ItemStateRollbackTests|FullyQualifiedName~OutgameRollbackStorageTests' --verbosity minimal`. Confirm behavioral failures rather than compile errors. Send controller RED result before production edits.
- [x] **Step 4: Implement state baseline promotion, lazy private storage, XML contracts and versions.** Snapshot failure must not set the deep-copy-complete flag. Clear held references on both commit and rollback but retain reusable capacity. Inactive calls must not instantiate storage. Update XML with scope lifetime, independent-copy requirement, value-vs-reference restoration and ownership restrictions. No new generic abstraction or runtime graph inspection.
- [x] **Step 5: Run focused GREEN then full Debug and Release suites once, and Release solution build with warnings as errors.** Commands: `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Debug --no-restore --verbosity minimal`; same with `-c Release`; `dotnet build Bun3.sln -c Release --no-restore -warnaserror --verbosity minimal`. Save outputs to plan workspace. Do not rerun passing suites unless code changes or new evidence warrants it.
- [x] **Step 6: Self-review and report without committing.** Report changed files, compatibility semantics, RED/GREEN/full test counts, build warnings and remaining limits. Root produces user guide and frozen review diff; fresh task reviewer and final reviewer inspect before final response.

## Controller deliverables

- [x] Record approval/context and plan self-review in scoped ledger; capture pre-task working-tree baseline.
- [x] Produce `docs/outgame-rollback.md` explaining snapshot payload and step order with a compact Mermaid flow and executable-shaped examples.
- [x] Review task against immutable baseline diff, resolve important findings, then whole-change final review.
- [x] Update document pointers and checked steps with actual validation outcomes; report modifications and exact rollback structure to user.


## ?? ?? (2026-09-29)

- ?? ??? 10? ??. ?? ?? 22/22, ?? ?? Debug 433/433, Release 433/433 ??.
- ?? Release ??? ??: ?? 0, ?? 0. ?? XML ?? ?? ? ?????.
- Task ?? ? ?? ??? ?? ?? ?? ??. ?? ??/?? ???/?? ? ?? ??? ?? ??? ??? ??.
- ?? ??? ?? ??. ?? ???stage?push???? ???? ???.
- ???? ??? ?? ??: [?? ???](../../outgame-rollback.md).
