# GameplayTag Picker Row Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 병합 GameplayTag Picker에서 현재 태그 이름 왼쪽에 체크 아이콘을 표시하고 Source 개수는 행 오른쪽 끝에 정렬한다.

**Architecture:** `GameplayTagPickerRow` projection은 변경하지 않고 `GameplayTagPickerTreeView`가 현재 canonical 경로와 Picker 전용 행 geometry를 소유한다. Picker renderer는 disclosure 이후 영역을 이름/체크 영역과 우측 Source 영역으로 나누며, Unity 내장 `TestPassed` 이미지가 없으면 `✓` 글리프로 대체한다.

**Tech Stack:** C# 9, Unity 2022.3+, Unity IMGUI `TreeView`, `GUIContent`, NUnit EditMode.

## Global Constraints

- Source 기반 Tag Editor TreeView와 `GameplayTagPickerRow` projection 데이터는 변경하지 않는다.
- 기존 선택 하이라이트, foldout, filter 자동 확장, 양축 scroll 및 tooltip 계약을 유지한다.
- 체크 이미지는 Unity 내장 `TestPassed`를 우선 사용하고 이미지가 없으면 `✓`를 표시한다.
- Source 표기는 `1 source` 또는 `N sources`를 유지하되 행 오른쪽 끝에 정렬한다.
- 좁은 행에서는 Source 영역을 유지하고 이름 영역만 먼저 줄인다.
- Gameplay NuGet/UPM version을 `0.11.0`에서 `0.11.1`로 함께 올린다.
- 모든 C# 파일은 `#nullable enable`, C# 9 block namespace를 유지한다.
- 사용자 소유 `unity/Assets/Scenes/SampleScene.unity`, `NewMonoBehaviourScript.cs(.meta)`, `unity/ProjectSettings/GameplayTagSettings.asset`, `GameplayTags.json`은 수정하거나 stage하지 않는다.
- Unity 프로세스를 종료하지 않는다. 테스트 러너가 `SENTIS_ANALYTICS_ENABLED`를 제거하면 exact diff를 확인한 뒤 그 한 줄만 `apply_patch`로 복원한다.

---

### Task 1: Picker 선택 아이콘과 우측 Source 레이아웃

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPickerWindow.cs:11-63`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPickerWindow.cs:241-307`
- Test: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagPickerTests.cs:19-177`

**Interfaces:**
- Consumes: `GameplayTagProjectionTreeView<GameplayTagPickerRow>.TryGetRow`, `CalculateLabelRect`, `SynchronizeSelection`.
- Produces: `GameplayTagPickerRowGeometry.Calculate(Rect, float, float)`, `GameplayTagPickerRowRects`, `GameplayTagPickerTreeView.SetCurrentPath(string)`, `IsCurrent(GameplayTagPickerRow)`, `CreateNameContent(GameplayTagPickerRow, bool, Texture?)`, `CreateSourceContent(GameplayTagPickerRow)`.

- [ ] **Step 1: Write failing selected-content and geometry tests**

Replace tests that expect `"jump  1 source"` in one content string with separate name and Source content assertions. Add tests equivalent to:

```csharp
var selectedIcon = new Texture2D(1, 1);
try
{
    var selected = GameplayTagPickerTreeView.CreateNameContent(
        row, isCurrent: true, selectedIcon);
    var ordinary = GameplayTagPickerTreeView.CreateNameContent(
        row, isCurrent: false, selectedIcon);
    var fallback = GameplayTagPickerTreeView.CreateNameContent(
        row, isCurrent: true, checkImage: null);

    Assert.That(selected.text, Is.EqualTo("jump"));
    Assert.That(selected.image, Is.SameAs(selectedIcon));
    Assert.That(ordinary.image, Is.Null);
    Assert.That(fallback.text, Does.StartWith("✓ "));
    Assert.That(GameplayTagPickerTreeView.CreateSourceContent(row).text,
        Is.EqualTo("1 source"));
}
finally
{
    UnityEngine.Object.DestroyImmediate(selectedIcon);
}
```

Add a pure geometry assertion:

```csharp
var rects = GameplayTagPickerRowGeometry.Calculate(
    new Rect(40f, 8f, 240f, 18f),
    sourceWidth: 56f,
    spacing: 8f);

Assert.That(rects.SourceRect.xMax, Is.EqualTo(280f));
Assert.That(rects.SourceRect.width, Is.EqualTo(56f));
Assert.That(rects.NameRect.xMax + 8f, Is.EqualTo(rects.SourceRect.xMin));
Assert.That(rects.NameRect.Overlaps(rects.SourceRect), Is.False);
```

Add a current-path state test that sets rows, synchronizes with `"ABILITY.JUMP"`, filters/reloads rows, and proves only the `ability.jump` row remains current by ordinal-ignore-case comparison. Then set the current path to `string.Empty`, `"Legacy..Broken"`, and a valid-but-missing path in turn and prove no projected row is current.

- [ ] **Step 2: Run focused RED**

Run:

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagPickerTests'
```

Expected: compile failure for the new geometry/content/current-path APIs. If Unity is already open, capture the generated Unity test csproj compiler RED instead of terminating the user process.

- [ ] **Step 3: Implement pure Picker row geometry**

In `GameplayTagPickerWindow.cs`, add the following Picker-only value types before `GameplayTagPickerTreeView`:

```csharp
internal readonly struct GameplayTagPickerRowRects
{
    internal GameplayTagPickerRowRects(Rect nameRect, Rect sourceRect)
    {
        NameRect = nameRect;
        SourceRect = sourceRect;
    }

    internal Rect NameRect { get; }
    internal Rect SourceRect { get; }
}

internal static class GameplayTagPickerRowGeometry
{
    internal static GameplayTagPickerRowRects Calculate(
        Rect labelRect,
        float sourceWidth,
        float spacing)
    {
        var safeSourceWidth = Math.Min(Math.Max(0f, sourceWidth), labelRect.width);
        var sourceRect = new Rect(
            labelRect.xMax - safeSourceWidth,
            labelRect.y,
            safeSourceWidth,
            labelRect.height);
        var nameWidth = Math.Max(0f, sourceRect.xMin - spacing - labelRect.xMin);
        var nameRect = new Rect(labelRect.x, labelRect.y, nameWidth, labelRect.height);
        return new GameplayTagPickerRowRects(nameRect, sourceRect);
    }
}
```

The calculation must clamp negative widths and keep `SourceRect.xMax == labelRect.xMax`.

- [ ] **Step 4: Implement selected content and split RowGUI**

Add `_currentCanonicalPath` to `GameplayTagPickerTreeView`. `SetCurrentPath` validates non-null and stores it. `IsCurrent` compares row path with `StringComparison.OrdinalIgnoreCase`. `SynchronizeSelection` must call `SetCurrentPath` before its existing selection behavior.

Split content creation:

```csharp
internal static GUIContent CreateNameContent(
    GameplayTagPickerRow row,
    bool isCurrent,
    Texture? checkImage)
{
    var tooltip = row.CanonicalPath + "\n" + row.SourceDetails;
    if (!isCurrent) return new GUIContent(row.DisplaySegment, tooltip);
    return checkImage is null
        ? new GUIContent("✓ " + row.DisplaySegment, tooltip)
        : new GUIContent(row.DisplaySegment, checkImage, tooltip);
}

internal static GUIContent CreateSourceContent(GameplayTagPickerRow row)
{
    var suffix = row.SourceCount == 1 ? " source" : " sources";
    return new GUIContent(
        row.SourceCount + suffix,
        row.CanonicalPath + "\n" + row.SourceDetails);
}
```

Override Picker `RowGUI` only. For known rows:

1. Get `labelRect = CalculateLabelRect(args.item, args.rowRect)`.
2. Measure Source content with `EditorStyles.miniLabel.CalcSize` while inside `OnGUI`.
3. Calculate name/source rects with 8px spacing.
4. Draw the name using `CreateNameContent(row, IsCurrent(row), EditorGUIUtility.IconContent("TestPassed").image)`.
5. Draw Source content in the exact-width right rect with `EditorStyles.miniLabel`.

Unknown rows delegate to `base.RowGUI(args)`. Keep `CreateRowContent` implemented for the abstract base but return the same name content.

- [ ] **Step 5: Keep current-path state synchronized**

In `GameplayTagPickerWindow.ApplySelection`, after assigning `_currentRawValue`, call:

```csharp
_treeView?.SetCurrentPath(canonicalPath);
```

Do this before invoking `_onSelected`. Existing `ApplyWorkspace` already calls `SynchronizeSelection(_currentRawValue)` after rows reload, so initial/live refresh and filter row replacement retain the current path.

- [ ] **Step 6: Run focused GREEN**

Run the exact focused command from Step 2. Expected: every `GameplayTagPickerTests` test passes, C# diagnostics 0, GUI style/OnGUI diagnostics 0.

- [ ] **Step 7: Run generated warning-zero builds**

Run sequentially:

```powershell
dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
dotnet build unity/Bun3.Gameplay.Unity.Tests.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
```

Expected for each: warnings 0, errors 0.

- [ ] **Step 8: Commit Task 1**

Stage only the Picker production/test files. Verify `git diff --cached --check`, then commit:

```powershell
git commit -m "✨ GameplayTag Picker 선택 표시와 행 정렬 개선" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

### Task 2: Package version and full verification

**Files:**
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj:10`
- Modify: `common/src/com.bun3.gameplay/package.json:4`

**Interfaces:**
- Consumes: completed Picker UI change.
- Produces: Gameplay NuGet/UPM version `0.11.1`.

- [ ] **Step 1: Bump Gameplay package versions**

Set the two exact values:

```xml
<Version>0.11.1</Version>
```

```json
"version": "0.11.1"
```

Do not change dependency constraints or other package versions.

- [ ] **Step 2: Run full .NET and Unity regression**

Run:

```powershell
dotnet test Bun3.sln -c Release --no-restore -v:minimal
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode -AllEditMode
```

Expected: .NET failures 0; Unity failed/skipped/inconclusive 0; C#/GUI diagnostics 0. Let Unity exit naturally and restore only the exact runner-removed compiler define token after inspecting the diff.

- [ ] **Step 3: Pack and read back metadata**

Pack Gameplay NuGet into an isolated temporary directory and create/read the UPM archive. Assert:

- NuGet nuspec version is exactly `0.11.1`.
- UPM `package.json` version is exactly `0.11.1`.
- Newtonsoft NuGet remains exactly `[13.0.2]`.
- UPM Unity remains `2022.3` and Newtonsoft UPM remains `3.2.2`.
- Picker production/test source changes are present exactly once in UPM output.

- [ ] **Step 4: Commit Task 2 and completed checklist**

Mark every plan checkbox complete. Stage only the two metadata files and this plan, verify staged scope and `git diff --cached --check`, then commit:

```powershell
git commit -m "🔖 GameplayTag Picker 0.11.1 버전 갱신" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Final scope verification**

Run:

```powershell
git diff --check
git status --short
git diff -- unity/ProjectSettings/ProjectSettings.asset
git log -3 --format=full
```

Expected: tracked/staged product changes are clean; `ProjectSettings.asset` diff is empty; only the pre-existing user-owned Scene/script/settings/source files remain unstaged; both feature commits have the exact required co-author trailer.
