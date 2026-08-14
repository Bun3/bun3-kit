# Task 9 Report — ✨ GameplayTag Source 트리 편집기 적용

## Status

`DONE`

Source를 가상 최상위 root로 표시하는 Tag Editor projection, Source별 중복 canonical 행과 comment/provenance,
implicit parent fold group, 순차 Editor row ID, `(SourceId, CanonicalPath)` 선택 key, 권한별 context action,
정확한 태그 참조 검색으로 차단되는 exact delete, Source별 redirect 그룹/read-only/shadow 표시를 구현했다.
Task 10이 같은 renderer geometry를 재사용할 수 있도록 `GameplayTagTreeView`의 양축 scroll,
`GetContentIndent` 기반 label rect와 row label content seam을 유지했다.

## Implemented

- `GameplayTagWorkspaceSnapshot`의 Runtime Catalog와 Provenance를 Source ID ordinal 순서로 투영한다.
  Source root는 빈 tag path와 runtime index 0을 가지며 GameplayTag selection/action을 발생시키지 않는다.
- 같은 canonical 태그가 여러 Source에 있으면 Source마다 별도 행으로 표시한다. runtime index는 같아도
  `int` 순차 Editor row ID가 달라 TreeView identity가 충돌하지 않는다.
- 각 Source의 implicit/explicit 상태, Source별 comment, read-only 상태를 행에 보존한다.
- 검색은 canonical 전체 path만 대소문자 무관하게 찾고 matching Source root와 조상을 포함한다.
  TreeView는 filter 진입 전 expand ID를 보존하고 match 문맥을 펼치며 filter 해제 시 기존 상태를 복원한다.
- writable explicit 행은 Rename/Edit Comment/Add Sub-Tag/Copy/Find References/Delete를, writable implicit 행은
  Delete를 제외한 동일 action을, read-only 행은 Copy/Find References만, Source root는 아무 action도 제공하지
  않는다. 모든 action target은 `(SourceId, CanonicalPath)`다.
- Delete는 exact canonical token 검색이 완전하고 match가 없을 때만 확인 후 `DeleteExact`를 호출한다.
  live match 또는 incomplete scan은 mutation 없이 reference result view로 보낸다.
- redirect는 owning Source ID 및 old path ordinal 순서로 그룹화한다. read-only Source의 개별/일괄 제거를
  비활성화하고 Find References는 유지한다. active old name과 겹치는 shadowed redirect에는 warning icon과
  active-name lookup priority tooltip을 표시한다.
- legacy 단일-Source/casing TreeModel test 4개를 정확히 제거하고 Source projection test 4개로 교체했다.

## Strict RED → GREEN Evidence

### Primary RED

Tests were replaced/added before production changes, then the required three-class filter was run:

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagTreeModelTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests;Bun3.Gameplay.Unity.Tests.GameplayTagRedirectMaintenanceTests'
```

Expected compiler RED: missing Source snapshot tree constructor/row metadata, `GameplayTagTreeSelectionKey`, action
policy/Find References, `GameplayTagRedirectRowModel`/`CreateRows`, and reference-gated delete seam. Test-authoring
namespace errors were corrected and the command was rerun before production implementation.

- RED log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814062746349.log`
- Result: Unity exit 1 due only to absent Task 9 production contracts.

### Primary GREEN

Final exact three-class filter: **63/63 passed**, 0 failed, 0 skipped.

- XML: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814064015643.xml`
- Log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814064015643.log`
- XML duration: `1.0605409s`
- C# warning/error diagnostics: none.

The first GREEN attempt was **62/63** because the shadow tooltip did not contain the exact tested lookup-priority
phrase (`...20260814063118647.xml`). Updating the production explanation produced 63/63.

### Warning icon mini-cycle

Self-review found the brief required both warning tooltip and icon for shadowed redirects. An image assertion was added
first:

- RED: **0/1 passed**, expected `GUIContent.image` null failure,
  `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814063659929.xml`.
- GREEN: **1/1 passed** after adding the Unity warning icon,
  `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814063842852.xml`.

## Generated Warning-zero Builds

Fresh final commands:

```powershell
dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror
dotnet build unity/Bun3.Gameplay.Unity.Tests.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror
```

Both dependency graphs completed with **0 warnings, 0 errors**. An earlier attempt ran the two generated builds in
parallel and hit a shared `unity/obj` file lock (`CS2012`); the required commands were then run sequentially and passed,
including the fresh final run above.

## Full EditMode Gate

Command:

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Final result: **111/111 passed**, 0 failed, 0 skipped.

- XML: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814064205300.xml`
- Log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814064205300.log`
- XML duration: `29.9789085s`
- C# warning/error diagnostics: none.

## Files Changed

- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeModel.cs`
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeView.cs`
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceSearch.cs`
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectMaintenance.cs`
- `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagTreeModelTests.cs`
- `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`
- `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagRedirectMaintenanceTests.cs`
- `.superpowers/sdd/2026-08-14-gameplay-tag-sources/task-9-report.md`

## Self-review and Safety

- `git diff --check` passes.
- Source row parent links are derived per Source; duplicate runtime indices never serve as Editor identity.
- Context action policy mutations (wrong read-only branch, implicit Delete, root actions) are covered by literal exact masks.
- Reference-blocked delete preserves the serialized Source and does not invoke confirmation or mutation.
- Redirect cleanup receives only writable Source rows; read-only rows remain searchable but never removable.
- No Unity process was terminated, paused, or otherwise interfered with.
- Every Unity run removed only `SENTIS_ANALYTICS_ENABLED` from the Standalone define. After each natural exit the exact
  one-line diff was inspected and only that token was restored with a scoped patch. Final ProjectSettings diff is empty.
- `artifacts/`, `unity/GameplayTags.json`, and sibling `.superpowers` contents were not staged, deleted, moved, or modified.

## Concerns

None.
