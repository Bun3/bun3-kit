# GameplayTag Editor Unreal Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GameplayTag 관리 창을 Unreal 방식의 단일 경로·계층 트리 워크플로로 바꾸고, 런타임 redirect를 프로젝트 참조 검색과 확인 기반 cleanup으로 관리한다.

**Architecture:** `GameplayTagCatalogEditSession`이 원자적인 작성 mutation을 소유하고, 재사용 가능한 `GameplayTagTreeModel`/`GameplayTagTreeView`가 관리 창과 향후 Inspector picker의 공통 계층 표현을 맡는다. 참조 검색은 프로젝트 파일 열거와 text scanner를 창에서 분리하며, Window는 팝업·진행 UI·dirty lifecycle만 조정한다.

**Tech Stack:** C# 9, netstandard2.1, Unity Editor IMGUI/TreeView 2022.3+, Newtonsoft.Json 13.0.2, NUnit, PowerShell 검증

## Global Constraints

- 기준 명세는 `docs/superpowers/specs/2026-08-13-gameplay-tag-editor-unreal-workflow-design.md`와 `docs/superpowers/specs/2026-08-12-gameplay-tag-catalog-design.md`다.
- 공용 런타임은 `netstandard2.1`, C# 9와 nullable enable을 유지하고 Unity 타입을 참조하지 않는다.
- `Bun3.Gameplay.Editor`는 Unity Editor 전용이며 런타임 lookup, JSON schema와 fingerprint 형식을 바꾸지 않는다.
- 태그 문법은 `^[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*$`, 전체 경로 255 ASCII 문자, 깊이 16, 활성 노드 최대 65,535개를 유지한다.
- 모든 작성 mutation은 복제→mutation→직렬화→`TagCatalog` 검증→교체 순서로 원자적으로 동작한다.
- Redirect는 exact mapping이며 wildcard, prefix redirect와 chain을 추가하지 않는다.
- Text reference search는 읽기 전용이고 외부 세이브·서버 설정·배포 빌드를 안전하다고 판정하지 않는다.
- 이번 범위에는 GameplayTag PropertyDrawer, Inspector picker, structured reference migration이 포함되지 않는다.
- `Bun3.Gameplay.csproj`과 UPM `package.json`은 함께 `0.7.0`, `com.bun3.unity.window`는 `0.2.1`로 올린다.
- build warning은 0이어야 하며 새 public API를 추가하지 않는다.
- 사용자 소유 미추적 `unity/GameplayTags.json`은 stage, 수정, 삭제하지 않는다.
- Unity가 실행 중이면 사용자 프로세스를 종료하지 않는다. 프로젝트 lock으로 검증할 수 없으면 증거와 함께 보고한다.
- commit은 gitmoji 제목과 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` trailer를 사용한다.

## File Structure

- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogEditSession.cs`: segment rename, comment 승격, subtree 삭제와 redirect 제거의 원자적 작성 계약.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs`: 세션 mutation, 선택 경로와 dirty 상태 조정.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeModel.cs`: 전체 활성 경로, explicit metadata, 검색과 조상 문맥을 제공하는 공용 모델. 기존 `GameplayTagCatalogViewModel.cs`를 대체한다.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeView.cs`: 선택, 확장 상태, 필터 임시 확장과 context action intent.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagEditDialog.cs`: rename/comment modal 입력 UI와 부모·마지막 세그먼트 분리.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`: 세로 관리 UI, context action, redirect 표시·검색·정리 orchestration과 기존 dirty lifecycle.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceSearch.cs`: reference file/match/result 값과 exact token text scanner.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagProjectReferenceFiles.cs`: Assets, ProjectSettings와 local/embedded package의 text file 열거.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceResultsWindow.cs`: text match 결과와 asset/file navigation.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectCleanupDialog.cs`: 참조 0 redirect 후보의 사용자 선택과 외부 데이터 경고.
- `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectMaintenance.cs`: 제거 가능 후보와 개별 제거 결정을 계산하는 순수 정책.
- `common/src/com.bun3.gameplay/Tests/Editor`: 세션, 모델, TreeView, Window, scanner와 cleanup 정책 EditMode 테스트.
- `unity/Packages/com.bun3.unity.window/Editor/OverlaySettingsValidator.cs`: Overlay 검증 메뉴 경로.
- `unity/Packages/com.bun3.unity.window/Tests/Editor/OverlaySettingsValidatorTests.cs`: Overlay 메뉴 attribute 회귀 테스트.
- `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`, `common/src/com.bun3.gameplay/package.json`: Gameplay 0.7.0 metadata.
- `unity/Packages/com.bun3.unity.window/package.json`, `CHANGELOG.md`: Window 0.2.1 metadata와 변경 기록.

---

### Task 1: 이름 변경을 Unreal의 마지막 세그먼트 rename으로 제한한다

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogEditSession.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogEditSessionTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`

**Interfaces:**
- Consumes: 기존 `EnsureActive`, `GetActiveSubtreePaths`, `RenameTagRows`, `RewritePrefix`, `Apply`.
- Produces: `GameplayTagCatalogEditSession.RenameSubtree(string path, string newSegment) : string`, `GameplayTagCatalogWindowController.RenameSubtree(string path, string newSegment) : void`.
- Removes: 두 클래스의 `RelocateSubtree(string oldPath, string newPath)`.

- [ ] **Step 1: 부모 고정, implicit subtree, case-only와 invalid segment 테스트를 먼저 쓴다**

`GameplayTagCatalogEditSessionTests`의 relocate 테스트를 다음 계약으로 교체한다.

```csharp
[Test]
public void Rename_subtree_changes_only_the_last_segment_and_redirects_every_active_old_path()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[" +
        "{\"name\":\"State.Movement.Run.Fast\"}]," +
        "\"redirects\":[{\"from\":\"Legacy.Run\",\"to\":\"State.Movement.Run\"}]}");

    var renamedPath = session.RenameSubtree("STATE.MOVEMENT.RUN", "Sprint");

    Assert.That(renamedPath, Is.EqualTo("State.Movement.Sprint"));
    Assert.That(session.Serialize(), Does.Contain("State.Movement.Sprint.Fast"));
    Assert.That(GetRedirectTarget(session, "State.Movement.Run"),
        Is.EqualTo("State.Movement.Sprint"));
    Assert.That(GetRedirectTarget(session, "State.Movement.Run.Fast"),
        Is.EqualTo("State.Movement.Sprint.Fast"));
    Assert.That(GetRedirectTarget(session, "Legacy.Run"),
        Is.EqualTo("State.Movement.Sprint"));
}

[Test]
public void Renaming_an_implicit_parent_moves_its_explicit_descendants()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Movement.Run\"}]}");

    Assert.That(session.RenameSubtree("State.Movement", "Motion"),
        Is.EqualTo("State.Motion"));
    Assert.That(session.Serialize(), Does.Contain("State.Motion.Run"));
    Assert.That(GetRedirectTarget(session, "State.Movement"), Is.EqualTo("State.Motion"));
    Assert.That(GetRedirectTarget(session, "State.Movement.Run"), Is.EqualTo("State.Motion.Run"));
}

[TestCase("Other.Parent")]
[TestCase("Bad_Name")]
[TestCase("")]
public void Rename_rejects_a_non_segment_and_preserves_the_document(string newSegment)
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");
    var before = session.Serialize();

    Assert.Throws<ArgumentException>(() => session.RenameSubtree("State.Dead", newSegment));
    Assert.That(session.Serialize(), Is.EqualTo(before));
}
```

case-only와 Controller 선택은 다음 테스트로 고정한다.

```csharp
[Test]
public void Case_only_rename_changes_display_case_without_a_redirect()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");

    Assert.That(session.RenameSubtree("State.Dead", "dead"), Is.EqualTo("State.dead"));
    Assert.That(session.Serialize(), Does.Contain("\"name\": \"State.dead\""));
    Assert.That(session.Redirects, Is.Empty);
}

[Test]
public void Controller_selects_the_full_path_returned_by_segment_rename()
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    var controller = new GameplayTagCatalogWindowController();
    controller.New(path);
    controller.Add("State.Dead");

    controller.RenameSubtree("State.Dead", "Deceased");

    Assert.That(controller.SelectedPath, Is.EqualTo("State.Deceased"));
    Assert.That(controller.IsDirty, Is.True);
}
```

- [ ] **Step 2: Unity EditMode RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogEditSessionTests'
```

Expected: `RenameSubtree`가 없고 기존 `RelocateSubtree` 계약이 남아 compile FAIL한다.

- [ ] **Step 3: 현재 표시 경로에서 부모를 보존해 새 전체 경로를 계산한다**

세션 API는 caller casing이 아니라 카탈로그 표시 경로에서 prefix를 계산하고 성공한 새 전체 경로를 반환한다.

```csharp
internal string RenameSubtree(string path, string newSegment)
{
    var renamedPath = string.Empty;
    Apply((tags, redirects) =>
    {
        if (newSegment is null || newSegment.IndexOf('.') >= 0)
            throw new ArgumentException("The new name must be one gameplay tag segment.", nameof(newSegment));
        _ = RequireCanonical(newSegment, nameof(newSegment));

        var oldCanonical = RequireCanonical(path, nameof(path));
        var catalog = EnsureActive(path, oldCanonical, out var tag);
        var oldDisplayPath = catalog.GetDisplayName(tag);
        var separator = oldDisplayPath.LastIndexOf('.');
        renamedPath = separator < 0
            ? newSegment
            : oldDisplayPath.Substring(0, separator + 1) + newSegment;
        var newCanonical = RequireCanonical(renamedPath, nameof(newSegment));
        var activePaths = GetActiveSubtreePaths(catalog, oldCanonical);

        RenameTagRows(tags, oldCanonical, oldDisplayPath, renamedPath);
        if (oldCanonical == newCanonical) return;

        for (var i = 0; i < redirects.Count; i++)
        {
            var redirect = redirects[i];
            redirects[i] = new EditableRedirectRow(
                redirect.From,
                RewritePrefix(redirect.To, oldCanonical, renamedPath));
        }

        for (var i = 0; i < activePaths.Count; i++)
        {
            var oldActivePath = activePaths[i];
            redirects.Add(new EditableRedirectRow(
                oldActivePath,
                RewritePrefix(oldActivePath, oldCanonical, renamedPath)));
        }
    });
    return renamedPath;
}
```

Controller는 반환된 경로를 선택하고 성공 뒤 dirty로 만든다.

```csharp
internal void RenameSubtree(string path, string newSegment)
{
    SelectedPath = RequireSession().RenameSubtree(path, newSegment);
    IsDirty = true;
}
```

기존 호출부와 테스트의 `RelocateSubtree` 이름을 모두 제거한다.

- [ ] **Step 4: 세션과 Window/controller fixture를 GREEN으로 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogEditSessionTests|Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

Expected: 두 fixture가 모두 PASS하고 C# warning/error가 없다.

- [ ] **Step 5: rename 계약을 commit한다**

```powershell
git add -- `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogEditSession.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogEditSessionTests.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs
git commit -m "♻️ GameplayTag 이름 변경을 세그먼트 단위로 제한" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Redirect 제거를 원자적 작성 mutation으로 추가한다

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogEditSession.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogEditSessionTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`

**Interfaces:**
- Consumes: `EditableRedirectRow`, session `Apply`와 Controller dirty rollback.
- Produces: `GameplayTagCatalogEditSession.RemoveRedirects(IReadOnlyCollection<string> sources) : int`, Controller의 동일 signature.

- [ ] **Step 1: case-insensitive bulk 제거, stale source 원자성과 empty no-op 테스트를 쓴다**

```csharp
[Test]
public void Remove_redirects_matches_sources_case_insensitively_in_one_transaction()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
        "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}," +
        "{\"from\":\"State.Gone\",\"to\":\"State.Dead\"}]}");

    var removed = session.RemoveRedirects(new[] { "state.killed", "STATE.GONE" });

    Assert.That(removed, Is.EqualTo(2));
    Assert.That(session.Redirects, Is.Empty);
}

[Test]
public void Removing_an_unknown_redirect_preserves_the_document()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
        "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}]}");
    var before = session.Serialize();

    Assert.Throws<InvalidOperationException>(
        () => session.RemoveRedirects(new[] { "State.Killed", "Missing.Old" }));
    Assert.That(session.Serialize(), Is.EqualTo(before));
}
```

Window/controller 계약은 다음 테스트로 고정한다.

```csharp
[Test]
public void Controller_removes_redirects_marks_dirty_and_persists_the_result()
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    File.WriteAllText(path,
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
        "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}]}");
    var controller = new GameplayTagCatalogWindowController();
    controller.Open(path);

    Assert.That(controller.RemoveRedirects(Array.Empty<string>()), Is.Zero);
    Assert.That(controller.IsDirty, Is.False);
    Assert.That(controller.RemoveRedirects(new[] { "State.Killed" }), Is.EqualTo(1));
    Assert.That(controller.IsDirty, Is.True);
    controller.Save();

    Assert.That(controller.IsDirty, Is.False);
    Assert.That(File.ReadAllText(path), Does.Not.Contain("State.Killed"));
}
```

- [ ] **Step 2: 대상 fixture RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogEditSessionTests|Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

Expected: `RemoveRedirects` 부재로 compile FAIL한다.

- [ ] **Step 3: 요청 source를 canonical set으로 검증하고 clone 안에서만 제거한다**

Controller에는 `using System.Collections.Generic;`을 추가한다.

```csharp
internal int RemoveRedirects(IReadOnlyCollection<string> sources)
{
    if (sources is null) throw new ArgumentNullException(nameof(sources));
    if (sources.Count == 0) return 0;

    var requested = new HashSet<string>(StringComparer.Ordinal);
    foreach (var source in sources)
    {
        requested.Add(RequireCanonical(source, nameof(sources)));
    }

    var removed = 0;
    Apply((_, redirects) =>
    {
        for (var i = redirects.Count - 1; i >= 0; i--)
        {
            if (!requested.Contains(Fold(redirects[i].From))) continue;
            redirects.RemoveAt(i);
            removed++;
        }

        if (removed != requested.Count)
            throw new InvalidOperationException("A redirect source is no longer present.");
    });
    return removed;
}
```

Controller는 제거 수가 양수일 때만 dirty로 바꾸고 선택은 유지한다.

```csharp
internal int RemoveRedirects(IReadOnlyCollection<string> sources)
{
    var removed = RequireSession().RemoveRedirects(sources);
    if (removed > 0) IsDirty = true;
    return removed;
}
```

- [ ] **Step 4: 대상 fixture GREEN을 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogEditSessionTests|Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

Expected: redirect 제거, stale rollback와 dirty 저장 테스트가 PASS한다.

- [ ] **Step 5: redirect mutation을 commit한다**

```powershell
git add -- `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogEditSession.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogEditSessionTests.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs
git commit -m "✨ GameplayTag redirect 제거 트랜잭션 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: 관리 창과 picker가 공유할 트리 모델을 분리한다

**Files:**
- Replace: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogViewModel.cs` → `GameplayTagTreeModel.cs` (기존 `.meta` GUID 보존)
- Replace: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogViewModelTests.cs` → `GameplayTagTreeModelTests.cs` (기존 `.meta` GUID 보존)
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`

**Interfaces:**
- Consumes: `GameplayTagCatalogEditSession.Tags`, `Serialize`, `TagCatalog` hierarchy.
- Produces: `GameplayTagTreeModel`, `GameplayTagTreeRowModel.IsExplicit`, `Rows`, `Filter(string)`.

- [ ] **Step 1: explicit metadata와 이름 전용 검색 테스트를 쓴다**

```csharp
[Test]
public void Rows_distinguish_explicit_tags_from_implicit_parents()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\",\"comment\":\"사망\"}]}");
    var model = new GameplayTagTreeModel(session);

    Assert.That(model.Rows.Single(row => row.Path == "State").IsExplicit, Is.False);
    var dead = model.Rows.Single(row => row.Path == "State.Dead");
    Assert.That(dead.IsExplicit, Is.True);
    Assert.That(dead.Comment, Is.EqualTo("사망"));
}

[Test]
public void Search_matches_full_path_only_and_keeps_ancestor_context()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[" +
        "{\"name\":\"State.Dead.Ghost\",\"comment\":\"spectral marker\"}," +
        "{\"name\":\"Ability.GhostWalk\"}]}");
    var model = new GameplayTagTreeModel(session);

    Assert.That(model.Filter("gHoSt").Select(row => row.Path),
        Is.EqualTo(new[] { "Ability", "Ability.GhostWalk", "State", "State.Dead", "State.Dead.Ghost" }));
    Assert.That(model.Filter("spectral"), Is.Empty);
}
```

- [ ] **Step 2: 모델 fixture RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagTreeModelTests'
```

Expected: 새 class/file과 `IsExplicit`, `Rows`가 없어 compile FAIL한다.

- [ ] **Step 3: 모델 이름을 바꾸고 작성 metadata를 행에 결합한다**

행 생성자는 다음 계약을 가진다.

```csharp
internal GameplayTagTreeRowModel(
    ushort index,
    ushort parentIndex,
    string path,
    string comment,
    bool isExplicit,
    bool directMatch)
```

모델 생성 시 explicit 행을 대소문자 무시 dictionary로 만들고 모든 활성 카탈로그 노드를 전위 순회 순서로 기록한다.

```csharp
internal sealed class GameplayTagTreeModel
{
    private readonly GameplayTagTreeRowModel[] _rows;

    internal GameplayTagTreeModel(GameplayTagCatalogEditSession session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        var metadata = new Dictionary<string, EditableTagRow>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < session.Tags.Count; i++) metadata.Add(session.Tags[i].Name, session.Tags[i]);

        using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(session.Serialize()));
        var catalog = TagCatalog.Load(stream);
        _rows = new GameplayTagTreeRowModel[catalog.Count];
        for (var index = 1; index <= catalog.Count; index++)
        {
            var tag = catalog.GetRequiredByIndex(checked((ushort)index));
            var path = catalog.GetDisplayName(tag);
            var isExplicit = metadata.TryGetValue(path, out var authored);
            _rows[index - 1] = new GameplayTagTreeRowModel(
                checked((ushort)index), catalog.GetParent(tag).Index, path,
                isExplicit ? authored.Comment : string.Empty, isExplicit, false);
        }

        ActiveCount = catalog.Count;
        FingerprintPrefix = FormatFingerprintPrefix(catalog.Fingerprint);
    }

    internal IReadOnlyList<GameplayTagTreeRowModel> Rows => _rows;
}
```

`Filter`는 `row.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase)`만 direct match로 사용하고 comment 조건을 제거한다. Window와 테스트의 타입 참조를 새 이름으로 바꾼다.

- [ ] **Step 4: 모델과 Window compile fixture를 GREEN으로 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagTreeModelTests|Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

Expected: 새 모델 테스트와 기존 Window 테스트가 PASS한다.

- [ ] **Step 5: 공용 트리 모델을 commit한다**

```powershell
git add -- `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogViewModel.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogViewModel.cs.meta `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeModel.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeModel.cs.meta `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogViewModelTests.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogViewModelTests.cs.meta `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagTreeModelTests.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagTreeModelTests.cs.meta `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs
git commit -m "♻️ GameplayTag 공용 트리 모델 분리" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: 트리에 context action과 검색 중 임시 확장을 추가한다

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeView.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`

**Interfaces:**
- Consumes: `GameplayTagTreeRowModel`, Unity `TreeViewState`.
- Produces: `GameplayTagTreeAction`, 다섯 action event, `SetRows(rows, bool isFiltering)`, `RequestAction(action, id)`.

- [ ] **Step 1: normal expansion 복원, scroll 보존과 모든 행 action dispatch 테스트를 쓴다**

```csharp
[Test]
public void Search_expansion_is_temporary_and_normal_expansion_and_scroll_are_restored()
{
    var state = new TreeViewState { scrollPos = new UnityEngine.Vector2(23f, 47f) };
    var tree = new GameplayTagTreeView(state);
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead.Ghost\"},{\"name\":\"Ability.Jump\"}]}");
    var model = new GameplayTagTreeModel(session);
    var abilityId = model.Rows.Single(row => row.Path == "Ability").Index;
    var stateId = model.Rows.Single(row => row.Path == "State").Index;
    var deadId = model.Rows.Single(row => row.Path == "State.Dead").Index;
    tree.SetRows(model.Rows, isFiltering: false);
    tree.SetExpanded(abilityId, true);
    tree.SetExpanded(stateId, false);

    tree.SetRows(model.Filter("Ghost"), isFiltering: true);
    Assert.That(tree.IsExpanded(stateId), Is.True);
    Assert.That(tree.IsExpanded(deadId), Is.True);

    tree.SetRows(model.Rows, isFiltering: false);
    Assert.That(tree.IsExpanded(abilityId), Is.True);
    Assert.That(tree.IsExpanded(stateId), Is.False);
    Assert.That(state.scrollPos, Is.EqualTo(new UnityEngine.Vector2(23f, 47f)));
}

[TestCase(GameplayTagTreeAction.Rename)]
[TestCase(GameplayTagTreeAction.EditComment)]
[TestCase(GameplayTagTreeAction.AddSubTag)]
[TestCase(GameplayTagTreeAction.Copy)]
[TestCase(GameplayTagTreeAction.Delete)]
public void Every_tree_row_dispatches_the_requested_context_action(GameplayTagTreeAction action)
{
    foreach (var isExplicit in new[] { false, true })
    {
        var tree = new GameplayTagTreeView(new TreeViewState());
        var row = new GameplayTagTreeRowModel(1, 0, "State", "", isExplicit, false);
        tree.SetRows(new[] { row }, isFiltering: false);
        string? received = null;
        Subscribe(tree, action, path => received = path);

        tree.RequestAction(action, row.Index);

        Assert.That(received, Is.EqualTo("State"));
    }
}
```

`Subscribe` test helper는 enum별 internal event에 handler를 연결한다.

```csharp
private static void Subscribe(
    GameplayTagTreeView tree,
    GameplayTagTreeAction action,
    Action<string> handler)
{
    switch (action)
    {
        case GameplayTagTreeAction.Rename: tree.RenameRequested += handler; break;
        case GameplayTagTreeAction.EditComment: tree.CommentEditRequested += handler; break;
        case GameplayTagTreeAction.AddSubTag: tree.SubTagRequested += handler; break;
        case GameplayTagTreeAction.Copy: tree.CopyRequested += handler; break;
        case GameplayTagTreeAction.Delete: tree.DeleteRequested += handler; break;
        default: throw new ArgumentOutOfRangeException(nameof(action));
    }
}
```

- [ ] **Step 2: TreeView fixture RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

Expected: action enum/event와 `SetRows` overload가 없어 compile FAIL한다.

- [ ] **Step 3: TreeView가 normal expanded IDs를 검색 전후에 보존하게 한다**

```csharp
internal enum GameplayTagTreeAction
{
    Rename,
    EditComment,
    AddSubTag,
    Copy,
    Delete
}
```

TreeView는 전달받은 `TreeViewState`와 검색 전 expanded ID snapshot을 보유한다.

```csharp
internal void SetRows(IReadOnlyList<GameplayTagTreeRowModel> rows, bool isFiltering)
{
    if (rows is null) throw new ArgumentNullException(nameof(rows));
    if (isFiltering && !_isFiltering)
        _expandedBeforeFilter = new List<int>(_state.expandedIDs);
    if (!isFiltering && _isFiltering && _expandedBeforeFilter is not null)
        _state.expandedIDs = new List<int>(_expandedBeforeFilter);

    _rows = rows;
    _isFiltering = isFiltering;
    Reload();
    if (!isFiltering) return;
    for (var index = 0; index < _rows.Count; index++) SetExpanded(_rows[index].Index, true);
}
```

기존처럼 normal `SetRows` 때 모든 행을 강제 expand하는 loop는 제거한다.
생성자에서 `useScrollView = true`를 명시하고 `TreeViewState.scrollPos` 하나로 가로·세로 위치를
보존한다. `TreeViewItem.displayName`에는 실제로 그리는 마지막 segment를 넣어 IMGUI TreeView가
계층 indent와 label 폭으로 horizontal content width를 계산하게 한다.

- [ ] **Step 4: GenericMenu와 testable dispatch를 구현한다**

다음 event를 추가한다.

```csharp
internal event Action<string>? RenameRequested;
internal event Action<string>? CommentEditRequested;
internal event Action<string>? SubTagRequested;
internal event Action<string>? CopyRequested;
internal event Action<string>? DeleteRequested;
```

`ContextClickedItem`은 Rename, Edit Comment, Add Sub-Tag, Copy Tag, Delete Tag 순서로 menu를 만들고 각 callback은 하나의 dispatch method를 사용한다.

```csharp
internal void RequestAction(GameplayTagTreeAction action, int id)
{
    if (!TryGetPath(id, out var path)) throw new ArgumentOutOfRangeException(nameof(id));
    switch (action)
    {
        case GameplayTagTreeAction.Rename: RenameRequested?.Invoke(path); break;
        case GameplayTagTreeAction.EditComment: CommentEditRequested?.Invoke(path); break;
        case GameplayTagTreeAction.AddSubTag: SubTagRequested?.Invoke(path); break;
        case GameplayTagTreeAction.Copy: CopyRequested?.Invoke(path); break;
        case GameplayTagTreeAction.Delete: DeleteRequested?.Invoke(path); break;
        default: throw new ArgumentOutOfRangeException(nameof(action));
    }
}
```

- [ ] **Step 5: TreeView fixture GREEN을 확인하고 commit한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
git add -- `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeView.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs
git commit -m "✨ GameplayTag 트리 context action 지원" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

Expected: expansion/action/tooltip/selection을 포함한 Window fixture가 PASS한다.

---

### Task 5: 관리 창을 단일 추가 폼과 context popup 워크플로로 바꾼다

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagEditDialog.cs`와 Unity-generated `.meta`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`

**Interfaces:**
- Consumes: Task 1 `RenameSubtree`, Task 4 TreeView intent, 기존 dirty lifecycle.
- Produces: `GameplayTagTextEditRequest`, `GameplayTagTextEditResult`, rename/comment modal, `PrepareSubTag`, `CopyTag`, 세로 `DrawAddTag`/`DrawTagTree`/`DrawRedirects`.

- [ ] **Step 1: 메뉴 경로, rename prompt, sub-tag 준비와 copy 테스트를 쓴다**

```csharp
[Test]
public void Tag_editor_uses_the_gameplay_menu_path()
{
    var method = typeof(GameplayTagCatalogWindow).GetMethod(
        nameof(GameplayTagCatalogWindow.OpenWindow), BindingFlags.Public | BindingFlags.Static)!;
    var menu = method.GetCustomAttribute<MenuItem>()!;
    Assert.That(menu.menuItem, Is.EqualTo("Gameplay/Tag Editor"));
}

[TestCase("State", "", "State")]
[TestCase("State.Movement.Run", "State.Movement", "Run")]
public void Rename_dialog_request_separates_the_readonly_parent_and_editable_segment(
    string path, string expectedParent, string expectedSegment)
{
    var request = GameplayTagEditDialog.CreateRenameRequest(path);
    Assert.That(request.ParentPath, Is.EqualTo(expectedParent));
    Assert.That(request.InitialValue, Is.EqualTo(expectedSegment));
}

[Test]
public void Add_sub_tag_only_prefills_the_add_form_and_copy_uses_the_full_path()
{
    var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
    var previousClipboard = EditorGUIUtility.systemCopyBuffer;
    try
    {
        window.PrepareSubTag("State.Movement");
        Assert.That(GetPrivateString(window, "_newTagName"), Is.EqualTo("State.Movement."));
        Assert.That(GetController(window).Session, Is.Null);

        window.CopyTag("State.Movement");
        Assert.That(EditorGUIUtility.systemCopyBuffer, Is.EqualTo("State.Movement"));
    }
    finally
    {
        EditorGUIUtility.systemCopyBuffer = previousClipboard;
        CloseWithoutSaving(window);
    }
}
```

암시 부모 comment promotion과 private field 확인 helper도 같은 fixture에 추가한다.

```csharp
[TestCase("")]
[TestCase("상태 루트")]
public void Accepted_comment_edit_promotes_an_implicit_parent(string comment)
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
    try
    {
        var controller = GetController(window);
        controller.New(path);
        controller.Add("State.Dead");

        window.ApplyComment("State", GameplayTagTextEditResult.Accept(comment));

        Assert.That(controller.Session!.Tags.Any(row => row.Name == "State"), Is.True);
        Assert.That(controller.Session.Tags.Single(row => row.Name == "State").Comment,
            Is.EqualTo(comment));
    }
    finally
    {
        CloseWithoutSaving(window);
    }
}

private static string GetPrivateString(GameplayTagCatalogWindow window, string fieldName)
{
    var field = typeof(GameplayTagCatalogWindow).GetField(
        fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The expected Window field is missing.");
    return (string)(field.GetValue(window)
        ?? throw new InvalidOperationException("The expected Window value is missing."));
}
```

- [ ] **Step 2: Window fixture RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

Expected: 새 메뉴 경로, dialog와 handler가 없어 FAIL한다.

- [ ] **Step 3: 재사용 가능한 modal text dialog를 구현한다**

```csharp
internal readonly struct GameplayTagTextEditRequest
{
    internal GameplayTagTextEditRequest(string parentPath, string initialValue)
    {
        ParentPath = parentPath;
        InitialValue = initialValue;
    }

    internal string ParentPath { get; }
    internal string InitialValue { get; }
}

internal readonly struct GameplayTagTextEditResult
{
    internal GameplayTagTextEditResult(bool accepted, string value)
    {
        Accepted = accepted;
        Value = value;
    }

    internal bool Accepted { get; }
    internal string Value { get; }
    internal static GameplayTagTextEditResult Cancelled => new GameplayTagTextEditResult(false, string.Empty);
    internal static GameplayTagTextEditResult Accept(string value) => new GameplayTagTextEditResult(true, value);
}
```

`GameplayTagEditDialog.CreateRenameRequest(string path) : GameplayTagTextEditRequest`,
`ShowRename(string path) : GameplayTagTextEditResult`와
`ShowComment(string path, string comment) : GameplayTagTextEditResult`를 제공한다. Rename mode는
`Parent Path`를 disabled label로 그리고 마지막 segment만 `Tag Name` TextField에 둔다. Comment mode는
전체 path label과 multiline TextArea를 사용한다. `ShowModalUtility()`가 닫힌 뒤 Accepted/Value를
반환하며 Escape와 Cancel은 `Cancelled`다.

- [ ] **Step 4: 창을 세로 레이아웃으로 바꾸고 TreeView event를 연결한다**

기존 `_newRootPath`, `_newRootComment`, `_newChildSegment`, `_comment`, `_movePath`와 우측 Details column을 제거한다. 다음 상태만 둔다.

```csharp
private const string NewTagNameControl = "GameplayTag.NewTagName";
private string _newTagName = string.Empty;
private string _newTagComment = string.Empty;
private bool _focusNewTagName;
private bool _showRedirects = true;
private Vector2 _redirectScroll;
```

`OnGUI`는 toolbar, 별도 search row, add form, 남은 높이의 tree, redirect foldout, status 순서로 그린다.

```csharp
private void DrawAddTag()
{
    EditorGUILayout.LabelField("Add New Gameplay Tag", EditorStyles.boldLabel);
    GUI.SetNextControlName(NewTagNameControl);
    _newTagName = EditorGUILayout.TextField("Tag Name", _newTagName);
    _newTagComment = EditorGUILayout.TextField("Comment", _newTagComment);
    using (new EditorGUI.DisabledScope(_controller.Session is null || _newTagName.Length == 0))
    {
        if (GUILayout.Button("Add"))
        {
            var added = _newTagName;
            if (Execute(() => _controller.Add(added, _newTagComment)))
            {
                _newTagName = string.Empty;
                _newTagComment = string.Empty;
            }
        }
    }

    if (_focusNewTagName && Event.current.type == EventType.Repaint)
    {
        EditorGUI.FocusTextInControl(NewTagNameControl);
        _focusNewTagName = false;
    }
}
```

`EnsureTreeViewState`에서 다섯 event를 한 번만 구독한다. Handler 계약은 다음과 같다.

```csharp
internal void PrepareSubTag(string path)
{
    _newTagName = path + ".";
    _focusNewTagName = true;
    Repaint();
}

internal void CopyTag(string path) => EditorGUIUtility.systemCopyBuffer = path;

internal void ApplyRename(string path, GameplayTagTextEditResult result)
{
    if (result.Accepted) Execute(() => _controller.RenameSubtree(path, result.Value));
}

internal void ApplyComment(string path, GameplayTagTextEditResult result)
{
    if (result.Accepted) Execute(() => _controller.SetComment(path, result.Value));
}
```

Delete event는 기존 subtree 확인을 재사용한다. `ReloadTree`는 `_treeView.SetRows(_model.Filter(_search), _search.Length > 0)`를 호출한다. Redirect foldout은 이 단계에서 read-only `From → To` 행과 count만 그린다. Tree는 `GameplayTagTreeView`의 내장 양방향 scroll view를 사용하고 Redirect 목록은 `EditorGUILayout.BeginScrollView(_redirectScroll, true, true)`로 가로·세로 scrollbar를 항상 허용한다.

- [ ] **Step 5: Window fixture와 Gameplay 전체 EditMode를 GREEN으로 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: focused fixture와 Gameplay EditMode 전체가 PASS하고 기존 dirty/selection 계약이 유지된다.

- [ ] **Step 6: 관리 창 UX를 commit한다**

```powershell
git add -- `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagEditDialog.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagEditDialog.cs.meta `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs
git commit -m "✨ GameplayTag 에디터 Unreal 워크플로 적용" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: 프로젝트 소유 text reference를 한 번의 파일 순회로 검색한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceSearch.cs`와 Unity-generated `.meta`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagProjectReferenceFiles.cs`와 Unity-generated `.meta`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagReferenceSearchTests.cs`와 Unity-generated `.meta`

**Interfaces:**
- Consumes: redirect old paths와 Unity project/package paths.
- Produces: `GameplayTagReferenceFile`, `GameplayTagReferenceMatch`, `GameplayTagReferenceSearchResult`, `GameplayTagReferenceProgress`, `GameplayTagTextReferenceScanner(Func<string, TextReader>)`, `Search(IReadOnlyList<GameplayTagReferenceFile>, IReadOnlyList<string>, string, Func<GameplayTagReferenceProgress, bool>?)`, `GameplayTagProjectReferenceFiles.Enumerate()`, `EnumerateOwnedTextFiles(string, IReadOnlyList<string>)`.

- [ ] **Step 1: exact boundary, case-insensitive 다중 source, catalog 제외와 불완전 scan 테스트를 쓴다**

새 fixture는 실제 프로젝트 파일을 건드리지 않고 다음 temp helper를 사용한다.

```csharp
private string _temporaryDirectory = null!;

[SetUp]
public void SetUp()
{
    _temporaryDirectory = Path.Combine(
        Path.GetTempPath(), "bun3-tag-reference-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_temporaryDirectory);
}

[TearDown]
public void TearDown()
{
    if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, true);
}

private string WriteText(string relativePath, string contents)
{
    var path = Path.Combine(_temporaryDirectory, relativePath);
    var directory = Path.GetDirectoryName(path)!;
    Directory.CreateDirectory(directory);
    File.WriteAllText(path, contents, new UTF8Encoding(false, true));
    return path;
}
```

```csharp
[Test]
public void Scanner_finds_exact_old_tag_tokens_case_insensitively_in_one_file_pass()
{
    var path = WriteText("References.cs",
        "var a = \"STATE.KILLED\"; var b = \"Ability.Old\";\n" +
        "var c = \"State.Killed.Child\";");
    var opens = 0;
    var scanner = new GameplayTagTextReferenceScanner(file =>
    {
        opens++;
        return File.OpenText(file);
    });

    var result = scanner.Search(
        new[] { new GameplayTagReferenceFile(path, "Assets/References.cs") },
        new[] { "State.Killed", "Ability.Old" },
        excludedCatalogPath: string.Empty,
        isCancelled: null);

    Assert.That(result.IsComplete, Is.True);
    Assert.That(result.Matches.Select(match => match.RedirectSource),
        Is.EquivalentTo(new[] { "State.Killed", "Ability.Old" }));
    Assert.That(result.Matches.Any(match => match.Preview.Contains("State.Killed.Child")), Is.False);
    Assert.That(opens, Is.EqualTo(1));
}

[Test]
public void Scanner_excludes_the_catalog_and_blocks_cleanup_after_read_error()
{
    var catalog = WriteText("GameplayTags.json", "State.Killed");
    var locked = WriteText("Locked.asset", "State.Killed");
    using var lockStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    var scanner = new GameplayTagTextReferenceScanner(File.OpenText);

    var result = scanner.Search(
        new[]
        {
            new GameplayTagReferenceFile(catalog, "Assets/GameplayTags.json"),
            new GameplayTagReferenceFile(locked, "Assets/Locked.asset")
        },
        new[] { "State.Killed" },
        catalog,
        isCancelled: null);

    Assert.That(result.IsComplete, Is.False);
    Assert.That(result.Errors, Is.Not.Empty);
    Assert.That(result.Matches, Is.Empty);
}

[Test]
public void Scanner_skips_binary_content_even_when_the_extension_is_text_capable()
{
    var path = Path.Combine(_temporaryDirectory, "Binary.asset");
    File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3, 4 });
    var scanner = new GameplayTagTextReferenceScanner(File.OpenText);

    var result = scanner.Search(
        new[] { new GameplayTagReferenceFile(path, "Assets/Binary.asset") },
        new[] { "State.Killed" },
        string.Empty,
        isCancelled: null);

    Assert.That(result.IsComplete, Is.True);
    Assert.That(result.Matches, Is.Empty);
}
```

file enumerator와 cancellation 계약은 다음 테스트로 고정한다.

```csharp
[Test]
public void Enumerator_includes_owned_text_roots_and_excludes_cache_meta_and_binary_files()
{
    var assets = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "Assets")).FullName;
    var settings = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "ProjectSettings")).FullName;
    var library = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "Library")).FullName;
    var localPackage = Directory.CreateDirectory(Path.Combine(_temporaryDirectory, "LocalPackage")).FullName;
    File.WriteAllText(Path.Combine(assets, "Scene.unity"), "State.Killed");
    File.WriteAllText(Path.Combine(settings, "Tags.json"), "State.Killed");
    File.WriteAllText(Path.Combine(localPackage, "TagCode.cs"), "State.Killed");
    File.WriteAllText(Path.Combine(assets, "Scene.unity.meta"), "State.Killed");
    File.WriteAllBytes(Path.Combine(assets, "Texture.png"), new byte[] { 0, 1, 2 });
    File.WriteAllText(Path.Combine(library, "Generated.cs"), "State.Killed");

    var files = GameplayTagProjectReferenceFiles.EnumerateOwnedTextFiles(
        _temporaryDirectory,
        new[] { localPackage, localPackage });

    Assert.That(files.Select(file => file.AbsolutePath), Is.EquivalentTo(new[]
    {
        Path.Combine(assets, "Scene.unity"),
        Path.Combine(settings, "Tags.json"),
        Path.Combine(localPackage, "TagCode.cs")
    }));
}

[Test]
public void Cancellation_marks_the_scan_incomplete_without_opening_more_files()
{
    var first = WriteText("First.cs", "State.Killed");
    var second = WriteText("Second.cs", "State.Killed");
    var opens = 0;
    var scanner = new GameplayTagTextReferenceScanner(path =>
    {
        opens++;
        return File.OpenText(path);
    });

    var result = scanner.Search(
        new[]
        {
            new GameplayTagReferenceFile(first, "Assets/First.cs"),
            new GameplayTagReferenceFile(second, "Assets/Second.cs")
        },
        new[] { "State.Killed" },
        string.Empty,
        progress => progress.Fraction >= 0.5f);

    Assert.That(result.IsComplete, Is.False);
    Assert.That(result.IsCancelled, Is.True);
    Assert.That(opens, Is.EqualTo(1));
}
```

- [ ] **Step 2: reference search fixture RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagReferenceSearchTests'
```

Expected: 새 scanner와 value type이 없어 compile FAIL한다.

- [ ] **Step 3: 검색 결과와 outer-file/inner-token 순회의 scanner를 구현한다**

값 계약은 다음과 같다.

```csharp
internal readonly struct GameplayTagReferenceFile
{
    internal GameplayTagReferenceFile(string absolutePath, string displayPath)
    {
        AbsolutePath = absolutePath;
        DisplayPath = displayPath;
    }
    internal string AbsolutePath { get; }
    internal string DisplayPath { get; }
}

internal readonly struct GameplayTagReferenceMatch
{
    internal GameplayTagReferenceMatch(
        string redirectSource, string absolutePath, string displayPath, int lineNumber, string preview)
    {
        RedirectSource = redirectSource;
        AbsolutePath = absolutePath;
        DisplayPath = displayPath;
        LineNumber = lineNumber;
        Preview = preview;
    }
    internal string RedirectSource { get; }
    internal string AbsolutePath { get; }
    internal string DisplayPath { get; }
    internal int LineNumber { get; }
    internal string Preview { get; }
}
```

`GameplayTagReferenceProgress`는 `DisplayPath`와 `Fraction`, `GameplayTagReferenceSearchResult`는 `IsComplete`, `IsCancelled`, read-only `Matches`, `Errors`를 가진다. 결과 type은 test와 maintenance가 동일한 불변 결과를 만들도록 다음 factory를 제공한다.

```csharp
internal static GameplayTagReferenceSearchResult Complete(
    IReadOnlyList<GameplayTagReferenceMatch> matches) =>
    new GameplayTagReferenceSearchResult(true, false, matches, Array.Empty<string>());

internal static GameplayTagReferenceSearchResult Incomplete(
    bool cancelled,
    IReadOnlyList<string> errors) =>
    new GameplayTagReferenceSearchResult(
        false, cancelled, Array.Empty<GameplayTagReferenceMatch>(), errors);
```

Scanner는 sources를 `Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)`에 한 번 넣고, 파일을 outer loop로 한 번 연다. 각 파일의 첫 read block 또는 line에 NUL 문자가 있으면 binary로 보고 정상적으로 건너뛴다. 각 line에서 `[A-Za-z0-9.]`의 최대 연속 token을 뽑아 exact dictionary lookup하며 `State.Old.Child`를 `State.Old`로 부분 일치시키지 않는다. catalog absolute path는 OS path comparison으로 건너뛴다. progress callback은 각 파일을 열기 전에 호출하고 true면 남은 파일을 열지 않는다. reader 예외는 error에 추가하고 결과를 incomplete로 만든다.

- [ ] **Step 4: Unity 소유 text file 열거를 구현한다**

허용 확장자는 다음 set으로 고정한다.

```csharp
private static readonly HashSet<string> TextExtensions = new HashSet<string>(
    new[]
    {
        ".anim", ".asmdef", ".asmref", ".asset", ".compute", ".controller",
        ".cs", ".json", ".overrideController", ".playable", ".prefab", ".shader",
        ".txt", ".unity", ".uss", ".uxml", ".yaml", ".yml"
    },
    StringComparer.OrdinalIgnoreCase);
```

`Enumerate()`는 `Application.dataPath`의 project root에서 `Assets`, `ProjectSettings`를 추가하고 `PackageInfo.GetAllRegisteredPackages()` 중 `PackageSource.Embedded`와 `PackageSource.Local`의 `resolvedPath`만 추가한다. pure overload `EnumerateOwnedTextFiles(string projectRoot, IReadOnlyList<string> localPackagePaths)`가 실제 recursive enumeration을 담당해 fixture가 temp directories로 검증할 수 있게 한다. reparse directory는 재귀하지 않고 absolute path 중복을 제거한다.

- [ ] **Step 5: scanner fixture GREEN과 전체 Gameplay EditMode를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagReferenceSearchTests'
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: scanner tests와 Gameplay EditMode 전체가 PASS하며 C# diagnostics가 없다.

- [ ] **Step 6: reference scanner를 commit한다**

```powershell
git add -- `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceSearch.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceSearch.cs.meta `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagProjectReferenceFiles.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagProjectReferenceFiles.cs.meta `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagReferenceSearchTests.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagReferenceSearchTests.cs.meta
git commit -m "✨ GameplayTag 프로젝트 참조 검색 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Redirect Find References와 확인 기반 cleanup UI를 연결한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceResultsWindow.cs`와 Unity-generated `.meta`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectCleanupDialog.cs`와 Unity-generated `.meta`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectMaintenance.cs`와 Unity-generated `.meta`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagRedirectMaintenanceTests.cs`와 Unity-generated `.meta`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`

**Interfaces:**
- Consumes: Task 2 `RemoveRedirects`, Task 6 search result/file enumeration.
- Produces: `ReferencedRedirectDecision`, `GameplayTagRedirectMaintenance.GetUnreferencedSources`, results window, cleanup selection dialog와 Window commands.

- [ ] **Step 1: 완전한 scan만 cleanup하고 referenced 항목을 bulk 후보에서 제외하는 테스트를 쓴다**

```csharp
[Test]
public void Bulk_cleanup_returns_only_sources_without_project_matches()
{
    var redirects = new[]
    {
        new EditableRedirectRow("State.Killed", "State.Dead"),
        new EditableRedirectRow("Ability.Old", "Ability.New")
    };
    var result = GameplayTagReferenceSearchResult.Complete(new[]
    {
        new GameplayTagReferenceMatch(
            "State.Killed", "C:/Project/Assets/A.prefab", "Assets/A.prefab", 7, "State.Killed")
    });

    Assert.That(GameplayTagRedirectMaintenance.GetUnreferencedSources(redirects, result),
        Is.EqualTo(new[] { "Ability.Old" }));
}

[Test]
public void Incomplete_scan_cannot_produce_cleanup_candidates()
{
    var redirects = new[] { new EditableRedirectRow("State.Killed", "State.Dead") };
    var incomplete = GameplayTagReferenceSearchResult.Incomplete(
        cancelled: true, errors: Array.Empty<string>());

    Assert.Throws<InvalidOperationException>(
        () => GameplayTagRedirectMaintenance.GetUnreferencedSources(redirects, incomplete));
}

[TestCase(0, ReferencedRedirectDecision.OpenReferences)]
[TestCase(1, ReferencedRedirectDecision.Cancel)]
[TestCase(2, ReferencedRedirectDecision.RemoveAnyway)]
public void Referenced_redirect_dialog_result_maps_to_an_explicit_decision(
    int result, ReferencedRedirectDecision expected)
{
    Assert.That(GameplayTagRedirectMaintenance.MapReferencedDialogResult(result), Is.EqualTo(expected));
}
```

Window orchestration은 선택된 source만 제거하고 incomplete scan에서는 selector를 호출하지 않는 것으로 검증한다.

```csharp
[Test]
public void Bulk_cleanup_removes_only_selected_sources_and_marks_the_window_dirty()
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    File.WriteAllText(path,
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]," +
        "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}," +
        "{\"from\":\"State.Gone\",\"to\":\"State.Dead\"}]}");
    var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
    try
    {
        var controller = GetController(window);
        controller.Open(path);
        var result = GameplayTagReferenceSearchResult.Complete(
            Array.Empty<GameplayTagReferenceMatch>());

        var applied = window.TryApplyBulkCleanup(
            result,
            candidates => new[] { candidates.Single(source => source == "State.Gone") });

        Assert.That(applied, Is.True);
        Assert.That(controller.IsDirty, Is.True);
        Assert.That(controller.Session!.Serialize(), Does.Contain("State.Killed"));
        Assert.That(controller.Session.Serialize(), Does.Not.Contain("State.Gone"));
    }
    finally
    {
        CloseWithoutSaving(window);
    }
}

[Test]
public void Incomplete_bulk_scan_never_opens_the_cleanup_selector()
{
    var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
    var selectorCalls = 0;
    try
    {
        var applied = window.TryApplyBulkCleanup(
            GameplayTagReferenceSearchResult.Incomplete(true, Array.Empty<string>()),
            candidates =>
            {
                selectorCalls++;
                return candidates;
            });

        Assert.That(applied, Is.False);
        Assert.That(selectorCalls, Is.Zero);
    }
    finally
    {
        CloseWithoutSaving(window);
    }
}
```

- [ ] **Step 2: cleanup fixture RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagRedirectMaintenanceTests|Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

Expected: maintenance policy와 UI orchestration이 없어 compile FAIL한다.

- [ ] **Step 3: 순수 redirect cleanup 정책과 결과 창을 구현한다**

```csharp
internal enum ReferencedRedirectDecision
{
    OpenReferences,
    Cancel,
    RemoveAnyway
}
```

`GetUnreferencedSources`는 incomplete 결과를 거부하고, `Matches.RedirectSource`를 대소문자 무시 set으로 만든 뒤 redirect 순서를 유지해 match 0 source만 반환한다. `MapReferencedDialogResult`는 `DisplayDialogComplex`의 0/1/2만 위 enum으로 변환하고 그 밖의 값은 `ArgumentOutOfRangeException`이다.

`GameplayTagReferenceResultsWindow.Show(result)`는 `Text Matches` 제목, old path, display path, line과 preview를 scroll list로 보여 준다. 행 클릭 시 `AssetDatabase.LoadMainAssetAtPath(displayPath)`가 있으면 ping/open하고, 없으면 `InternalEditorUtility.OpenFileAtLineExternal(absolutePath, line)`를 호출한다. incomplete/cancel/error banner도 같은 창에 표시한다.

- [ ] **Step 4: 선택 가능한 cleanup modal과 외부 데이터 경고를 구현한다**

`GameplayTagRedirectCleanupDialog.ShowModal(IReadOnlyList<string> candidates)`는 후보별 checkbox를 기본 선택 상태로 보여 주고 다음 경고를 항상 표시한다.

```text
No project references were found. Save data, server configuration,
external files, and already deployed builds were not scanned.
```

`Remove Selected`는 체크된 source 배열을 반환하고 Cancel/Escape는 빈 배열을 반환한다. 후보가 0이면 dialog를 열지 않는다.

- [ ] **Step 5: Window에서 Find/Remove 명령과 진행 UI를 연결한다**

Redirect foldout의 각 행에 `Find References`, `Remove Redirect` 버튼을, header에 `Find All References`, `Remove Obsolete Redirects`를 추가한다. Search wrapper는 다음 구조를 지킨다.

```csharp
private GameplayTagReferenceSearchResult SearchRedirectReferences(IReadOnlyList<string> sources)
{
    try
    {
        var files = GameplayTagProjectReferenceFiles.Enumerate();
        return new GameplayTagTextReferenceScanner(File.OpenText).Search(
            files,
            sources,
            _controller.FilePath,
            progress => EditorUtility.DisplayCancelableProgressBar(
                "Find GameplayTag References", progress.DisplayPath, progress.Fraction));
    }
    finally
    {
        EditorUtility.ClearProgressBar();
    }
}
```

행의 `Find References`는 해당 `From` 하나로 검색하고 항상 결과 창을 연다. `Find All References`는
현재 session의 모든 `Redirects[i].From`을 한 번의 scan에 전달하고 결과가 0개여도 완료 상태를 결과
창에 표시한다.

개별 제거 규칙은 다음과 같다.

1. 최신 scan이 incomplete면 결과 창만 열고 종료한다.
2. match가 있으면 `Open References`, `Cancel`, `Remove Anyway` dialog를 연다.
3. `Open References`는 결과 창만 열고, `Cancel`은 종료한다.
4. `Remove Anyway` 또는 match 0은 외부 데이터 경고 confirmation을 연다.
5. 최종 승인 뒤 `_controller.RemoveRedirects(new[] { source })`를 `Execute`로 실행한다.

일괄 제거는 complete scan→`GetUnreferencedSources`→cleanup dialog→선택 source가 있을 때만 `Execute` 순서로 수행한다. 다음 internal seam을 실제 버튼과 fixture가 함께 사용한다.

```csharp
internal bool TryApplyBulkCleanup(
    GameplayTagReferenceSearchResult result,
    Func<IReadOnlyList<string>, IReadOnlyList<string>> selectSources)
{
    if (selectSources is null) throw new ArgumentNullException(nameof(selectSources));
    if (!result.IsComplete) return false;
    var session = _controller.Session
        ?? throw new InvalidOperationException("No gameplay tag catalog is open.");
    var candidates = GameplayTagRedirectMaintenance.GetUnreferencedSources(
        session.Redirects, result);
    var selected = selectSources(candidates);
    return selected.Count > 0 && Execute(() => _controller.RemoveRedirects(selected));
}
```

검색은 어떤 파일도 수정하지 않고 removal만 기존 dirty lifecycle에 진입한다.

- [ ] **Step 6: cleanup fixture와 Gameplay 전체 EditMode를 GREEN으로 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagRedirectMaintenanceTests|Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: 참조 분류, incomplete 차단, 개별 override, 선택 bulk removal와 dirty lifecycle이 PASS한다.

- [ ] **Step 7: redirect 관리 UI를 commit한다**

```powershell
git add -- `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceResultsWindow.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceResultsWindow.cs.meta `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectCleanupDialog.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectCleanupDialog.cs.meta `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectMaintenance.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectMaintenance.cs.meta `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagRedirectMaintenanceTests.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagRedirectMaintenanceTests.cs.meta `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs
git commit -m "✨ GameplayTag redirect 정리 도구 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Overlay 검증 메뉴를 Window 아래로 옮기고 패키지를 갱신한다

**Files:**
- Modify: `unity/Packages/com.bun3.unity.window/Editor/OverlaySettingsValidator.cs`
- Modify: `unity/Packages/com.bun3.unity.window/Tests/Editor/OverlaySettingsValidatorTests.cs`
- Modify: `unity/Packages/com.bun3.unity.window/package.json`
- Modify: `unity/Packages/com.bun3.unity.window/CHANGELOG.md`

**Interfaces:**
- Consumes: 기존 `OverlaySettingsValidator.Validate`.
- Produces: `Window/Validate Overlay Settings` menu item, UPM 0.2.1.

- [ ] **Step 1: 새 메뉴 경로 attribute 테스트를 쓴다**

```csharp
[Test]
public void Validate_command_uses_the_window_menu_path()
{
    var method = typeof(OverlaySettingsValidator).GetMethod(
        nameof(OverlaySettingsValidator.Validate),
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
    var menu = (UnityEditor.MenuItem)System.Attribute.GetCustomAttribute(
        method, typeof(UnityEditor.MenuItem))!;

    Assert.That(menu.menuItem, Is.EqualTo("Window/Validate Overlay Settings"));
}
```

- [ ] **Step 2: Overlay fixture RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode -AllEditMode `
  -TestFilter 'Bun3.Unity.Window.Editor.Tests.OverlaySettingsValidatorTests'
```

Expected: attribute 값이 `Bun3/Window/Validate Overlay Settings`라 FAIL한다.

- [ ] **Step 3: 메뉴와 patch metadata를 변경한다**

```csharp
[MenuItem("Window/Validate Overlay Settings")]
public static void Validate()
```

`package.json`의 version을 `0.2.1`로 바꾸고 CHANGELOG 맨 위에 다음 항목을 추가한다.

```markdown
## [0.2.1] - 2026-08-13

### Changed

- Moved the overlay settings validator menu to `Window/Validate Overlay Settings`.
```

- [ ] **Step 4: Overlay fixture와 UPM metadata를 GREEN으로 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  -Mode EditMode -AllEditMode `
  -TestFilter 'Bun3.Unity.Window.Editor.Tests.OverlaySettingsValidatorTests'
(Get-Content -Raw unity/Packages/com.bun3.unity.window/package.json | ConvertFrom-Json).version
```

Expected: fixture PASS, 출력 `0.2.1`.

- [ ] **Step 5: Window package 변경을 commit한다**

```powershell
git add -- `
  unity/Packages/com.bun3.unity.window/Editor/OverlaySettingsValidator.cs `
  unity/Packages/com.bun3.unity.window/Tests/Editor/OverlaySettingsValidatorTests.cs `
  unity/Packages/com.bun3.unity.window/package.json `
  unity/Packages/com.bun3.unity.window/CHANGELOG.md
git commit -m "🔧 Unity 에디터 메뉴 경로 정리" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: Gameplay 0.7.0 metadata와 전체 release gate를 검증한다

**Files:**
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Modify: `common/src/com.bun3.gameplay/package.json`

**Interfaces:**
- Consumes: Tasks 1–8의 완성된 editor/runtime behavior.
- Produces: warning-zero build, 전체 .NET/Unity test evidence, NuGet/UPM 0.7.0 readback.

- [ ] **Step 1: 두 Gameplay metadata를 0.7.0으로만 변경한다**

```xml
<Version>0.7.0</Version>
```

```json
"version": "0.7.0"
```

NuGet `Newtonsoft.Json` `[13.0.2]`, UPM Unity `2022.3`와 dependency `3.2.2`는 바꾸지 않는다.

- [ ] **Step 2: scoped diff와 Release warning-zero build를 검사한다**

```powershell
git diff --check
git status --short
dotnet build common/src/com.bun3.gameplay/Bun3.Gameplay.csproj `
  -c Release --no-restore --warnaserror
```

Expected: whitespace error 없음, runtime build warning 0/error 0. `unity/GameplayTags.json`은 여전히 미추적이며 stage되지 않는다.

- [ ] **Step 3: 전체 .NET test suite를 실행한다**

```powershell
dotnet test Bun3.sln -c Release --no-restore
```

Expected: FAIL 0. 기존 명시적 skip은 결과에 그대로 기록한다.

- [ ] **Step 4: 전체 Unity EditMode suite를 실행한다**

Unity 실행 전 `git diff -- unity/ProjectSettings/ProjectSettings.asset`을 기록한다.

```powershell
git diff -- unity/ProjectSettings/ProjectSettings.asset
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode -AllEditMode
git diff -- unity/ProjectSettings/ProjectSettings.asset
```

Expected: result XML의 testcasecount가 0보다 크고 failed 0, Unity log에 `warning CS`, `error CS`, `Compilation failed`가 없다. Unity가 tracked ProjectSettings를 바꿨다면 실행 전 diff와 실행 후 diff를 비교하고, 실행 전 clean이었던 자동 생성 line만 `apply_patch`로 원래 값에 복원한다. 사용자 변경이 섞였거나 원인이 불명확하면 수정하지 않고 중단해 보고한다.

- [ ] **Step 5: NuGet과 UPM 산출물 metadata를 readback한다**

```powershell
$artifactRoot = Join-Path $env:TEMP ('bun3-gameplay-0.7.0-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $artifactRoot | Out-Null
dotnet pack common/src/com.bun3.gameplay/Bun3.Gameplay.csproj `
  -c Release --no-restore -o $artifactRoot
$package = Get-ChildItem -LiteralPath $artifactRoot -Filter 'Bun3.Gameplay.0.7.0.nupkg' -File -ErrorAction Stop
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
  $entry = @($archive.Entries | Where-Object FullName -like '*.nuspec')
  if ($entry.Count -ne 1) { throw "Expected one nuspec, got $($entry.Count)." }
  $reader = [IO.StreamReader]::new($entry[0].Open())
  try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
  if ($nuspec.package.metadata.version -ne '0.7.0') { throw 'NuGet version mismatch.' }
  $dependency = @($nuspec.package.metadata.dependencies.group.dependency |
    Where-Object id -eq 'Newtonsoft.Json')
  if ($dependency.Count -ne 1 -or $dependency[0].version -ne '[13.0.2]') {
    throw 'NuGet dependency mismatch.'
  }
} finally { $archive.Dispose() }
$upm = Get-Content -Raw common/src/com.bun3.gameplay/package.json | ConvertFrom-Json
if ($upm.version -ne '0.7.0' -or $upm.unity -ne '2022.3' -or
    $upm.dependencies.'com.unity.nuget.newtonsoft-json' -ne '3.2.2') {
  throw 'UPM metadata mismatch.'
}
```

Expected: NuGet/UPM 모두 0.7.0이고 기존 dependency 범위와 Unity floor가 유지된다.

- [ ] **Step 6: metadata만 stage해 commit한다**

```powershell
git add -- `
  common/src/com.bun3.gameplay/Bun3.Gameplay.csproj `
  common/src/com.bun3.gameplay/package.json
git diff --cached --check
git diff --cached --name-only
git commit -m "🔖 Bun3.Gameplay 0.7.0 버전 갱신" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

Expected: staged/committed 파일은 metadata 두 개뿐이다.

- [ ] **Step 7: commit 후 최종 회귀와 repository 상태를 재확인한다**

```powershell
dotnet build common/src/com.bun3.gameplay/Bun3.Gameplay.csproj `
  -c Release --no-restore --warnaserror
dotnet test Bun3.sln -c Release --no-restore
git diff --check
git status --short
```

Expected: build warning 0/error 0, .NET FAIL 0, tracked diff와 staged diff 없음. 로컬 brainstorming 산출물 `.superpowers/`와 사용자 소유 `unity/GameplayTags.json`은 commit하지 않는다. publish와 push는 수행하지 않는다.
