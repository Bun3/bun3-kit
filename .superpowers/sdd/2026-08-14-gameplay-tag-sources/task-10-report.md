# Task 10 Report — ✨ 병합 GameplayTag Picker 트리 추가

## Status

`DONE`

병합 Runtime Catalog를 직접 투영하는 선택 전용 GameplayTag Picker를 추가했다. 같은 canonical 태그를
여러 Source가 선언해도 runtime index당 행 하나만 만들며, Source ID 순서의 Source 이름/comment 상세를
tooltip에 보존한다. 이름 검색, 일치 조상 자동 확장, 검색 전 expand 복원, 양축 scroll, canonical 경로만
반환하는 선택 callback을 구현했다. invalid Workspace에서는 기존 raw 선택값과 persistent diagnostic을
표시하되 신규 선택을 차단한다.

Task 9의 Source tree와 Picker가 별도 renderer를 복제하지 않도록 `GameplayTagProjectionTreeView<TRow>`를
추출했다. hierarchy 구성, 행 ID lookup, 검색 expand snapshot/restore, selection reveal, scroll과 label rect를
공유하고 각 projection은 label/content와 선택 의미만 제공한다. label rect는 계속 Unity
`GetContentIndent` 뒤에서 시작해 disclosure alignment fix를 보존한다.

## Implemented

- `GameplayTagPickerModel`은 `GameplayTagWorkspaceSnapshot.Catalog`의 preorder/runtime index를 그대로 행
  identity와 parent identity로 사용한다. Source root나 Source별 중복 tree를 만들지 않는다.
- `GameplayTagPickerRow`은 canonical path, 마지막 segment, parent/runtime row ID, Source count, Source별
  상세와 direct-match 상태를 보존한다.
- provenance 상세는 compiler가 보장하는 Source ID ordinal 순서로 `SourceId (DisplayName): comment` 형태로
  만들며 implicit contribution도 명시한다.
- filter는 canonical 전체 경로만 `OrdinalIgnoreCase`로 검색하고 직접 일치 행과 조상만 반환한다.
- Picker tree는 full canonical path와 Source/comment 상세를 tooltip으로 제공하고 `useScrollView`의 수평·수직
  scroll을 함께 사용한다. 검색 종료 시 기존 expand ID를 복원하며, 양축 scroll position은 filter reload를
  통과해 변경되지 않는다.
- `GameplayTagPickerWindow.Show(snapshot, selectedPath, callback)`와 invalid 상태를 받을 수 있는 Workspace
  overload를 추가했다. current raw value는 catalog에 없거나 casing이 달라도 그대로 표시한다.
- Workspace가 build 불가이면 tree interaction과 programmatic selection을 모두 차단하고 diagnostic banner를
  유지한다. callback에는 Source ID가 아닌 canonical runtime path만 전달한다.
- 계획대로 PropertyDrawer/serialized wrapper는 추가하지 않았다.

## Strict RED → GREEN Evidence

### Primary RED

Production code 작성 전에 Picker test 6개를 추가하고 다음 command를 실행했다.

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagPickerTests'
```

예상한 compiler RED로 `GameplayTagPickerModel`, `GameplayTagPickerRow`, `GameplayTagPickerTreeView`,
`GameplayTagPickerWindow`이 없다는 `CS0246`/`CS0103`이 발생했다. 같은 log에는 test가 사용하는 legacy
TreeView API의 `CS0618` obsolete warning도 있었고, test file 범위에서 이후 suppression했다.

- RED log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814093356884.log`
- Result: Unity exit 1, Task 10 production contracts 부재로 compile 실패

### Primary GREEN

같은 picker-only command의 최종 결과는 **6/6 passed**, 0 failed/skipped, duration `0.0872065s`, C# warning/error
diagnostics none이다.

- XML: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814093810890.xml`
- Log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814093810890.log`

첫 GREEN attempt는 production compile 후 4/6이었고 두 실패 모두 unshown test EditorWindow에 `Close()`를
호출한 test seam 문제였다. 실제 UI 선택은 close하고 programmatic verification seam은 callback만 적용하도록
분리했으며 test cleanup은 `DestroyImmediate`를 사용해 6/6이 되었다.

### Required Picker + Tree focused gate

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagPickerTests;Bun3.Gameplay.Unity.Tests.GameplayTagTreeModelTests'
```

**10/10 passed**, 0 failed/skipped, duration `0.1017397s`, C# diagnostics none.

- XML: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814094032895.xml`
- Log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814094032895.log`

## Generated Warning-zero Builds

Fresh final commands:

```powershell
dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror
dotnet build unity/Bun3.Gameplay.Unity.Tests.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror
```

두 dependency graph 모두 **0 warnings, 0 errors**로 종료했다.

## Full EditMode Gate

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Fresh final result: **118/118 passed**, 0 failed/skipped, duration `29.9780627s`, C# diagnostics none.

- XML: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814094642416.xml`
- Log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814094642416.log`

첫 full run은 self-review 최적화에서 Source label helper가 기존 Path-derived segment 대신 `DisplayName`을
사용한 회귀를 찾아 **117/118**이었다. 기존 contract를 복원한 뒤 exact regression test가 **1/1**
(`...20260814094506377.xml`)이었고 위 final full run이 118/118을 확인했다.

## Files Changed

- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPickerModel.cs` + `.meta`
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPickerRow.cs` + `.meta`
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPickerWindow.cs` + `.meta`
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeModel.cs`
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeView.cs`
- `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagPickerTests.cs` + `.meta`
- `.superpowers/sdd/2026-08-14-gameplay-tag-sources/task-10-report.md`

## Self-review and Safety

- Merged row identity and hierarchy come from the immutable runtime Catalog; no Source ID enters selection output.
- Source/comment order, full-path-only filter, direct-match marking, expand restore, scroll axes, disclosure geometry,
  valid canonical-only selection, invalid raw value and disabled invalid selection all have behavior tests.
- `git diff --check` passes; final generated builds are warning-zero.
- No Unity process was terminated, paused, or otherwise interfered with.
- Every runner changed only the exact removal of `SENTIS_ANALYTICS_ENABLED` from the Standalone define. After each
  natural exit the one-line diff was inspected and only that token was restored with a scoped patch. Final
  ProjectSettings diff is empty.
- `artifacts/`, `unity/GameplayTags.json`, and sibling `.superpowers/brainstorm` contents were not staged, deleted,
  moved or modified.

## Concerns

None.

## Fix Round 1 — explicit empty-comment provenance 표시 수정

### Review finding and correction

- Important finding을 수용했다. 기존 formatter는 `Comment.Length == 0`을 implicit 판정으로 사용해 comment를
  생략한 explicit 선언까지 `implicit`으로 잘못 표시했다.
- `TagSourceContribution.IsExplicit`을 authoritative provenance flag로 사용한다. comment가 있으면 기존
  Source detail/comment text를 유지하고, 빈 comment의 explicit declaration은 `explicit (no comment)`, 실제
  implicit contribution은 `implicit`으로 표시한다.
- duplicate leaf의 explicit empty-comment contribution과 같은 Source들의 실제 implicit parent를 한 test에서
  함께 검증해 두 상태가 다시 합쳐지지 않게 했다.

### Strict RED → GREEN

Picker-only command:

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagPickerTests'
```

- RED: **6/7 passed, 1 failed**. 새
  `Empty_comment_explicit_contribution_is_not_reported_as_implicit` test가 기대한
  `game (Game): explicit (no comment)` 대신 기존 `game (Game): implicit`을 관찰했다.
  - XML: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814095629354.xml`
  - Log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814095629354.log`
- GREEN: **7/7 passed**, 0 failed/skipped, duration `0.0878205s`, C# diagnostics none.
  - XML: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814095800648.xml`
  - Log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814095800648.log`

### Final verification

- Picker + Tree focused: **11/11 passed**, 0 failed/skipped, duration `0.1029116s`, C# diagnostics none.
  - XML: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814095928083.xml`
  - Log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814095928083.log`
- Generated `Bun3.Gameplay.Editor.csproj`: **0 warnings, 0 errors** with warning-as-error.
- Generated `Bun3.Gameplay.Unity.Tests.csproj`: **0 warnings, 0 errors** with warning-as-error.
- Full EditMode: **119/119 passed**, 0 failed/skipped, duration `30.9574318s`, C# diagnostics none.
  - XML: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814100101935.xml`
  - Log: `C:\Users\dudck\AppData\Local\Temp\bun3-gameplay-editmode-20260814100101935.log`

### Safety

- No Unity process was terminated or interfered with.
- Each runner changed only the exact removal of `SENTIS_ANALYTICS_ENABLED`; the one-line diff was inspected after
  natural exit and only that token was restored. Final ProjectSettings diff is empty.
- PropertyDrawer, Task 11 live invalidation and filter allocation behavior were not changed.
- `artifacts/`, `unity/GameplayTags.json`, and sibling `.superpowers/brainstorm` remain untouched and unstaged.
