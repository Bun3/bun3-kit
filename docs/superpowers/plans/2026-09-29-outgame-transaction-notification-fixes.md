# Outgame Transaction and Notification Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair the four reproduced rollback/notification failures and make participant finalization independent of game callbacks.

**Architecture:** Changes still apply immediately. Core finalizes every participant before delivering committed notifications; modules own stable notification batches across synchronous reentry and discard them after delivery even on failure. TState rollback always preserves the original value.

**Tech Stack:** C# 9 / netstandard2.1; net10.0 + NUnit 4 tests.

**Spec:** `docs/superpowers/specs/2026-09-29-outgame-transaction-notification-fixes-design.md`

## Global Constraints

- Package code: netstandard2.1 + C# 9, block namespaces; public English XML documentation; zero build warnings.
- Code/comments/README/metadata English only; Korean rationale belongs under docs/.
- No new external dependency. No closures, LINQ, or string allocations on normal hot paths.
- Preserve immediate application, single session actor, no nested transactions, no Begin during finalization/notification, no transactional transfers/load/drain.
- Core 0.7.0, Items 0.3.0, Achievements 0.4.0. Keep TransactionScope naming.
- Work in this existing branch/worktree. Do not stage, commit, push, publish, or merge; leave a reviewable working-tree patch.

## Review Focus

1. One participant's dirty/applied/completed or log sink throws: every participant is finalized, independent notifications are attempted, next rollback works, no replay.
2. An earlier participant changes a later participant during notification: frozen committed batches stay correct and ordinary reactions notify separately.
3. A callback recursively changes the same inventory: its current span remains stable and ledger contains original and derived rows exactly once in causal order.
4. Transfer followed by only a field mutation: no old delta survives into the new transaction; both inventories remain independently reusable after an exception.
5. Struct state replacement with shared or replaced reference fields: rollback restores original scalar values and collection contents; existing class identity behavior remains correct.

## File Structure

- `server/src/Bun3.Server.Core/Transaction.cs`: participant phases, exception fan-out and public contract.
- `server/src/Bun3.Server.Items/ItemInventory.Transaction.cs`: internal finalization, frozen committed batch, original-state capture.
- `server/src/Bun3.Server.Items/ItemInventory.Operations.cs`, `.Transfer.cs`, `.cs`: delivery ownership, shared emit path, reusable buffers.
- `server/src/Bun3.Server.Items/ItemInventory.Regen.cs`: if a focused regression confirms it, finish basis/scratch bookkeeping before notification so callback failure/reentry cannot repeat a refill.
- `server/src/Bun3.Server.Items/ItemInventory.Notifications.cs` may be added for the cohesive delivery implementation if needed; keep namespace and partial layout documentation consistent.
- `server/src/Bun3.Server.Achievements/AchievementManager.Transaction.cs` and `.cs`: finalized state and independent committed notification delivery.
- The three package csproj files: version bumps.
- Existing Transaction/ItemStateRollback/ItemInventoryTransaction/ItemTransfer/AchievementTransaction/OutgameLedgerIntegration tests: public behavior regressions. A cohesive `OutgameNotificationTests.cs` fixture is allowed.
- `server/src/Bun3.Server.Core/ActionLog.cs` only if preserving sink span ownership under the required reentry exposes a directly related failure; cover it first and explain the scope.

## Task 1: Repair participant lifecycle, state rollback and notification ownership

**Files:** All scoped files above; one integrated task because the notification interface and both implementers must change together.

**Interfaces:**
- Add `void OnCommitted()` to `ITransactionParticipant`.
- `OnCommit()` does internal finalization only; `OnCommitted()` invokes notifications frozen by the first phase; `OnRollback()` restores/discards.
- Keep all inventory/achievement gameplay entry points and optional constructor parameters compatible.
- One error rethrows the original exception with its stack; multiple errors report `AggregateException` after processing all participants/independent callbacks.

- [x] **Step 1: Add failing behavior tests before production edits.**
  - Two inventories enlist; first dirty throws on Commit; second has gold=5. Its subsequent uncommitted removal of 2 must restore 5 and later dirty must work.
  - Struct `State(1,[10])`, then State replacement `(5,[20])`, BeginStateChange Add(30), no Commit => `(1,[10])`; include a replacement sharing the original list.
  - Transfer one sword outside transaction, then target sword Flags change + Commit => exactly one transfer +1 notification total, one matching log row.
  - onApplied for gold +2 recursively grants reward +3 => original span unchanged through callback and log has gold +2 then reward +3 exactly once.
  - Add the five Review Focus cases with literal expected outputs, real inventories/managers, and applicable successful/exception paths.
  - Existing fake ITransactionParticipant implementations may gain a no-op extra method in tests before the production interface changes; do not change production only to compile a failing test.
- [x] **Step 2: Run focused tests and capture the expected RED output.** Use `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release --filter <fixtures>`; distinguish expected missing behavior from compilation/setup errors.
- [x] **Step 3: Implement minimal coherent repair.**
  - Core performs all internal commits followed by all committed notifications with independent exception collection and finally cleanup; rollback similarly visits all in reverse order.
  - Both modules finish their transaction bookkeeping before game code, freeze their pending notification payloads, and release storage in finally paths.
  - Reentrant inventory notification storage never aliases a live callback span. Ordinary operations, transaction commits and both transfer sides use the same cleanup/error/ownership rules.
  - Record originating log rows before game reaction hooks; one failing delivery channel does not skip other independent channels. Do not replay failed batches.
  - Preserve/deep-copy the original TState snapshot instead of substituting the current struct state. Existing class reference identity semantics remain covered.
  - Keep allocation-free ordinary paths after warmup; avoid generic abstractions unless multiple real consumers need them.
- [x] **Step 4: Verify GREEN and contracts.** Re-run focused tests, then full Debug and Release server tests; `dotnet build Bun3.sln -c Release -warnaserror`. Retain all existing allocation tests and add a meaningful warmed callback/reentry path allocation assertion without log boxing or NUnit work inside the measured window.
- [x] **Step 5: Update English public contract docs and package versions.** Explain internal commit vs notification, committed state after notification failure, reentry/span lifetime and no replay. Update the earlier spec with a pointer to this amendment.
- [x] **Step 6: Self-review and report.** Record exact RED/GREEN commands, counts, evidence, changed files and any limitations in the task report. Leave all source changes uncommitted for independent review.

## Acceptance

All four reproduced defects are fixed and automatically covered; immediate application and prior guards remain; both modules are reusable after callback failures; allocation checks and full server suites pass; Release solution has zero warnings; independent spec/quality review has no unresolved important findings.

## Verified Result

- Focused RED: 18 failures / 55 passes; a subsequent builder-specific RED: 1 failure / 20 passes.
- Final focused Release: 78 passed, 0 failed/skipped.
- Full server Debug and Release: 423 passed each, 0 failed/skipped (400 existing + 23 new cases).
- Release solution build with `-warnaserror`: 0 warnings, 0 errors.
- Independent task review and final integration review: approved, no findings.
- Related repairs cover stable ActionLog sink spans, regen basis finalization and builder cleanup before callbacks; no new external dependency.
- The completed repair remains an uncommitted working-tree patch. Review logs are retained in the ignored plan workspace while there is no commit history for this patch.
