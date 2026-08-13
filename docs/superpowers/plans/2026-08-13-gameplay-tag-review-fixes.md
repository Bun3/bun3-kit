# GameplayTag Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** GameplayTag 리뷰에서 확인된 에디터 dirty 유실, allocation 측정 오탐, 중복 fingerprint 계산, 컨테이너 exact 상태 추출 부재를 수정하고 `Bun3.Gameplay` 0.6.0으로 검증한다.

**Architecture:** Unity Window가 팝업과 Unity lifecycle을 소유하고 controller는 파일·세션 상태만 소유한다. 런타임에서는 `TagCatalog`의 build data와 최종 immutable catalog 생성을 분리하고, 컨테이너는 호출자 제공 `Span<T>`에 exact 상태를 복사하는 작은 interface만 공개한다.

**Tech Stack:** C# 9, netstandard2.1, .NET 10 NUnit 4.1, Unity 2022.3+ Editor/Test Framework, Newtonsoft.Json 13.0.2, PowerShell 릴리스 검증

## Global Constraints

- 기준 명세는 `docs/superpowers/specs/2026-08-13-gameplay-tag-review-fixes-design.md`와 `docs/superpowers/specs/2026-08-12-gameplay-tag-catalog-design.md`다.
- 공용 런타임은 `netstandard2.1`, C# 9, nullable enable을 유지하고 Unity 타입을 참조하지 않는다.
- Unity package 최소 버전은 `2022.3`이며 Editor 코드는 `Bun3.Gameplay.Editor`에만 둔다.
- 새 public type과 member는 한국어 XML 문서를 갖고 build warning은 0이어야 한다.
- 조회와 복사 hot path는 LINQ, iterator, boxing, heap allocation을 사용하지 않는다.
- JSON schema, deterministic index, fingerprint bytes, tag query/mutation 의미를 바꾸지 않는다.
- NuGet `Bun3.Gameplay.csproj`과 UPM `package.json` 버전은 함께 `0.6.0`으로 올린다.
- 사용자 소유 미추적 `unity/GameplayTags.json`은 stage, 수정, 삭제하지 않는다.
- commit은 gitmoji 제목과 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` trailer를 사용한다.

## File Structure

- `Editor/Tags/GameplayTagCatalogWindow.cs`: dirty decision, 교체 guard, Unity unsaved-change/reload lifecycle.
- `Editor/Tags/GameplayTagCatalogWindowController.cs`: 파일·편집 세션과 명시적 discard 상태 전환.
- `Tests/Editor/GameplayTagCatalogWindowTests.cs`: 임시 JSON을 쓰는 Window/controller lifecycle 테스트.
- `Runtime/Tags/TagCatalog.Build.cs`, `TagCatalog.cs`: build data와 최종 fingerprint 생성.
- `Runtime/Tags/TagContainer.cs`: `CopyExactTags`.
- `Runtime/Tags/TagCountEntry.cs`: exact tag/count readonly value type.
- `Runtime/Tags/TagCountContainer.cs`: `CopyExactEntries`.
- `common/tests/Bun3.Gameplay.Tests`: .NET 의미·allocation 회귀 테스트.
- `Tests/Editor/GameplayUnitySmokeTests.cs`: Unity public interface compile/runtime smoke.
- `Bun3.Gameplay.csproj`, `package.json`: 0.6.0 metadata.

---

### Task 1: 태그 조회 allocation 측정을 assertion/JIT에서 격리한다

**Files:**
- Modify: `common/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs`

**Interfaces:**
- Consumes: `TagContainer.Has`, `TagCountContainer.Has`, `TagCountContainer.Count`.
- Produces: test-only `MeasureTagQueries(TagContainer, TagCountContainer, GameplayTag, int, out int) : long`.

- [ ] **Step 1: 현재 실패를 RED로 재현한다**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj `
  --configuration Release --no-restore `
  --filter FullyQualifiedName~AllocationSmokeTests.Tag_queries_do_not_allocate
```

Expected: `Expected: 0 But was: 24`로 FAIL한다.

- [ ] **Step 2: 측정 helper와 assertion 순서를 수정한다**

`using System.Runtime.CompilerServices;`를 추가하고 테스트를 다음으로 교체한다.

```csharp
[Test]
public void Tag_queries_do_not_allocate()
{
    var catalog = TagCatalogTestData.Load();
    var ghost = catalog.GetRequired("State.Dead.Ghost");
    var dead = catalog.GetRequired("State.Dead");
    var set = catalog.CreateContainer(8);
    var counts = catalog.CreateCountContainer(8);
    set.Add(ghost);
    counts.Add(ghost, 2);

    _ = MeasureTagQueries(set, counts, dead, 1, out _);
    var allocated = MeasureTagQueries(set, counts, dead, 100_000, out var hits);

    Assert.That(allocated, Is.Zero);
    Assert.That(hits, Is.EqualTo(400_000));
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static long MeasureTagQueries(
    TagContainer tags,
    TagCountContainer counts,
    GameplayTag query,
    int iterations,
    out int hits)
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    hits = 0;
    for (var i = 0; i < iterations; i++)
    {
        if (tags.Has(query)) hits++;
        if (counts.Has(query)) hits++;
        hits += counts.Count(query);
    }

    return GC.GetAllocatedBytesForCurrentThread() - before;
}
```

- [ ] **Step 3: 대상과 전체 .NET suite를 GREEN으로 확인한다**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~AllocationSmokeTests.Tag_queries_do_not_allocate
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore
```

Expected: 대상 1/1 PASS, 전체 FAIL 0.

- [ ] **Step 4: commit한다**

```powershell
git add -- common/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs
git commit -m "🐛 GameplayTag 조회 할당 측정 격리" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: dirty 세션 교체를 Save/Discard/Cancel 결정으로 보호한다

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`

**Interfaces:**
- Consumes: controller `IsDirty`, `Save`, `TryExecute`, `New`, `Open`, `Reload`.
- Produces: internal `UnsavedChangesDecision`, `MapUnsavedChangesDialogResult(int)`, `TryResolveUnsavedChanges(UnsavedChangesDecision, Func<bool>)`.

- [ ] **Step 1: 실제 controller/file 결과를 검증하는 실패 테스트를 쓴다**

```csharp
[TestCase(0, UnsavedChangesDecision.Save)]
[TestCase(1, UnsavedChangesDecision.Cancel)]
[TestCase(2, UnsavedChangesDecision.Discard)]
public void Unsaved_dialog_result_maps_to_the_matching_decision(
    int dialogResult, UnsavedChangesDecision expected)
{
    Assert.That(GameplayTagCatalogWindow.MapUnsavedChangesDialogResult(dialogResult),
        Is.EqualTo(expected));
}

[Test]
public void Save_decision_persists_the_real_dirty_session_before_replacement()
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    var controller = new GameplayTagCatalogWindowController();
    controller.New(path);
    controller.Add("State.Dead");

    var proceed = GameplayTagCatalogWindow.TryResolveUnsavedChanges(
        UnsavedChangesDecision.Save,
        () => controller.TryExecute(controller.Save, out _));

    Assert.That(proceed, Is.True);
    Assert.That(controller.IsDirty, Is.False);
    Assert.That(File.ReadAllText(path), Does.Contain("State.Dead"));
}

[TestCase(UnsavedChangesDecision.Discard, true)]
[TestCase(UnsavedChangesDecision.Cancel, false)]
public void Discard_proceeds_and_cancel_preserves_the_dirty_session(
    UnsavedChangesDecision decision, bool expectedProceed)
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    var controller = new GameplayTagCatalogWindowController();
    controller.New(path);
    controller.Add("State.Dead");

    var proceed = GameplayTagCatalogWindow.TryResolveUnsavedChanges(
        decision, () => controller.TryExecute(controller.Save, out _));

    Assert.That(proceed, Is.EqualTo(expectedProceed));
    Assert.That(controller.IsDirty, Is.True);
    Assert.That(File.ReadAllText(path), Does.Not.Contain("State.Dead"));
    Assert.That(controller.Session!.Serialize(), Does.Contain("State.Dead"));
}

[Test]
public void Failed_save_does_not_allow_catalog_replacement()
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    var controller = new GameplayTagCatalogWindowController();
    controller.New(path);
    controller.Add("State.Dead");
    Directory.Delete(_temporaryDirectory, true);

    var proceed = GameplayTagCatalogWindow.TryResolveUnsavedChanges(
        UnsavedChangesDecision.Save,
        () => controller.TryExecute(controller.Save, out _));

    Assert.That(proceed, Is.False);
    Assert.That(controller.IsDirty, Is.True);
    Assert.That(controller.Session!.Serialize(), Does.Contain("State.Dead"));
}
```

- [ ] **Step 2: Unity EditMode RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: 새 enum/helper가 없어 compile FAIL. 다른 Unity 인스턴스가 프로젝트를 점유하면 닫고 재실행한다.

- [ ] **Step 3: decision helper와 replacement guard를 구현한다**

Window class 앞에 enum을 추가한다.

```csharp
internal enum UnsavedChangesDecision
{
    Save,
    Discard,
    Cancel
}
```

Window에 다음 method를 추가한다.

```csharp
internal static UnsavedChangesDecision MapUnsavedChangesDialogResult(int result) => result switch
{
    0 => UnsavedChangesDecision.Save,
    1 => UnsavedChangesDecision.Cancel,
    2 => UnsavedChangesDecision.Discard,
    _ => throw new ArgumentOutOfRangeException(nameof(result))
};

internal static bool TryResolveUnsavedChanges(
    UnsavedChangesDecision decision, Func<bool> save)
{
    if (save is null) throw new ArgumentNullException(nameof(save));
    return decision switch
    {
        UnsavedChangesDecision.Save => save(),
        UnsavedChangesDecision.Discard => true,
        UnsavedChangesDecision.Cancel => false,
        _ => throw new ArgumentOutOfRangeException(nameof(decision))
    };
}

private bool TryPrepareForCatalogReplacement(string title)
{
    if (!_controller.IsDirty) return true;
    var result = EditorUtility.DisplayDialogComplex(
        title,
        "Save changes to the current gameplay tag catalog?",
        "Save", "Cancel", "Discard");
    return TryResolveUnsavedChanges(
        MapUnsavedChangesDialogResult(result),
        () => Execute(_controller.Save));
}
```

`Execute`를 bool 반환으로 바꾸고 성공/실패 모두 `SynchronizeUnsavedChanges()`를 호출한다.

```csharp
private bool Execute(Action action)
{
    if (_controller.TryExecute(action, out var error))
    {
        ReloadTree();
        SynchronizeUnsavedChanges();
        return true;
    }

    SynchronizeUnsavedChanges();
    GameplayTagValidationWindow.Show(
        _controller.FilePath.Length == 0 ? "GameplayTags.json" : _controller.FilePath,
        error!);
    return false;
}

private void SynchronizeUnsavedChanges() => hasUnsavedChanges = _controller.IsDirty;
```

`CreateNew`, `Open`, `Reload`은 파일 picker 취소 확인 뒤 guard를 거친다.

```csharp
private void CreateNew()
{
    var path = EditorUtility.SaveFilePanel(
        "Create Gameplay Tag Catalog", "", "GameplayTags", "json");
    if (path.Length == 0 ||
        !TryPrepareForCatalogReplacement("Create Gameplay Tag Catalog")) return;
    Execute(() => _controller.New(path));
}

private void Open()
{
    var path = EditorUtility.OpenFilePanel(
        "Open Gameplay Tag Catalog", "", "json");
    if (path.Length == 0 ||
        !TryPrepareForCatalogReplacement("Open Gameplay Tag Catalog")) return;
    Execute(() => _controller.Open(path));
}

private void Reload()
{
    if (!TryPrepareForCatalogReplacement("Reload Gameplay Tags")) return;
    Execute(() =>
    {
        if (!_controller.Reload(discardDirty: true))
            throw new InvalidOperationException("Gameplay tag reload was not allowed.");
    });
}
```

replacement가 실패하면 `TryExecute`가 원래 dirty session을 복구한다.

- [ ] **Step 4: Unity tests를 GREEN으로 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: 신규 decision 테스트와 기존 Editor 테스트 PASS.

- [ ] **Step 5: commit한다**

```powershell
git add -- common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs
git commit -m "🐛 GameplayTag 편집 세션 교체 보호" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: 창 닫기와 assembly reload에 Unity unsaved-change lifecycle을 연결한다

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`

**Interfaces:**
- Consumes: Task 2의 decision/helper와 bool-returning `Execute`.
- Produces: controller `DiscardChanges()`, Window `SaveChanges()`, `DiscardChanges()`, `HandleBeforeAssemblyReload(UnsavedChangesDecision)`.

- [ ] **Step 1: lifecycle 실패 테스트를 먼저 쓴다**

```csharp
[Test]
public void Synchronizing_a_dirty_controller_marks_the_window_unsaved()
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
    try
    {
        var controller = GetController(window);
        controller.New(path);
        controller.Add("State.Dead");
        SynchronizeUnsavedChanges(window);
        Assert.That(window.hasUnsavedChanges, Is.True);
        Assert.That(window.saveChangesMessage, Does.Contain("gameplay tag catalog"));
    }
    finally { CloseWithoutSaving(window); }
}

[TestCase(UnsavedChangesDecision.Save, true)]
[TestCase(UnsavedChangesDecision.Discard, false)]
public void Assembly_reload_resolves_the_real_dirty_session(
    UnsavedChangesDecision decision, bool expectedSaved)
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    var window = EditorWindow.GetWindow<GameplayTagCatalogWindow>();
    try
    {
        var controller = GetController(window);
        controller.New(path);
        controller.Add("State.Dead");
        window.HandleBeforeAssemblyReload(decision);

        Assert.That(File.ReadAllText(path).Contains("State.Dead"), Is.EqualTo(expectedSaved));
        Assert.That(controller.IsDirty, Is.False);
        Assert.That(window.hasUnsavedChanges, Is.False);
    }
    finally { CloseWithoutSaving(window); }
}
```

기존 Window test의 `window.Close()` cleanup을 다음 helper로 바꾼다.

```csharp
private static void CloseWithoutSaving(GameplayTagCatalogWindow window)
{
    if (window.hasUnsavedChanges) window.DiscardChanges();
    else window.Close();
}

private static void SynchronizeUnsavedChanges(GameplayTagCatalogWindow window)
{
    var method = typeof(GameplayTagCatalogWindow).GetMethod(
        "SynchronizeUnsavedChanges", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The unsaved state synchronizer is missing.");
    method.Invoke(window, null);
}
```

- [ ] **Step 2: Unity RED를 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: `HandleBeforeAssemblyReload`/controller `DiscardChanges` 부재로 FAIL.

- [ ] **Step 3: controller discard와 Window lifecycle을 구현한다**

Controller:

```csharp
internal void DiscardChanges() => IsDirty = false;
```

Window lifecycle:

```csharp
private void OnEnable()
{
    titleContent = new GUIContent("Gameplay Tags");
    minSize = new Vector2(640f, 420f);
    saveChangesMessage = "Save changes to the current gameplay tag catalog?";
    AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
    AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
    EnsureTreeViewState();
    SynchronizeUnsavedChanges();
}

private void OnDisable() =>
    AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;

public override void SaveChanges()
{
    if (!Execute(_controller.Save)) return;
    base.SaveChanges();
}

public override void DiscardChanges()
{
    _controller.DiscardChanges();
    SynchronizeUnsavedChanges();
    base.DiscardChanges();
}

private void BeforeAssemblyReload()
{
    if (!_controller.IsDirty) return;
    var save = EditorUtility.DisplayDialog(
        "Reload Scripts",
        "Save changes to the current gameplay tag catalog before scripts reload?",
        "Save", "Discard");
    HandleBeforeAssemblyReload(save
        ? UnsavedChangesDecision.Save
        : UnsavedChangesDecision.Discard);
}

internal void HandleBeforeAssemblyReload(UnsavedChangesDecision decision)
{
    if (!_controller.IsDirty) return;
    if (decision == UnsavedChangesDecision.Save)
    {
        _ = Execute(_controller.Save);
        return;
    }

    if (decision != UnsavedChangesDecision.Discard)
        throw new ArgumentException("Assembly reload cannot be cancelled.", nameof(decision));
    _controller.DiscardChanges();
    SynchronizeUnsavedChanges();
}
```

- [ ] **Step 4: Unity suite를 GREEN으로 확인한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: lifecycle/기존 Editor 테스트 PASS, test cleanup에서 popup 없음.

- [ ] **Step 5: commit한다**

```powershell
git add -- common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs `
  common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs
git commit -m "🐛 GameplayTag 편집기 미저장 변경 보호" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: `TagCatalog` fingerprint를 한 번만 계산한다

**Files:**
- Create: `common/tests/Bun3.Gameplay.Tests/TagCatalogAllocationTests.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Build.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs`

**Interfaces:**
- Consumes: private `Build`, `BuildRedirects`, `CreateCanonicalNames`, `ComputeFingerprint`.
- Produces: private readonly `BuildData`와 fingerprint가 이미 계산된 최종 constructor 경로.

- [ ] **Step 1: 단일 fingerprint allocation 예산 테스트를 쓴다**

`TagCatalogAllocationTests.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagCatalogAllocationTests
{
    [Test]
    public void Catalog_creation_stays_within_single_fingerprint_allocation_budget()
    {
        var utf8 = Encoding.UTF8.GetBytes(TagCatalogTestData.BuildFlatCatalog(4_096));
        _ = Load(utf8);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var catalog = Load(utf8);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        GC.KeepAlive(catalog);
        Assert.That(allocated, Is.LessThanOrEqualTo(8_200_000L));
    }

    private static TagCatalog Load(byte[] utf8)
    {
        using var stream = new MemoryStream(utf8, writable: false);
        return TagCatalog.Load(stream);
    }
}
```

4,096 flat tag의 현재 측정값은 8,318,920 bytes이고 폐기되는 첫 fingerprint pass는 약 200,288 bytes다. 8,200,000 상한은 현재 구현을 실패시키면서 단일 pass에 80KB 이상 여유를 준다.

- [ ] **Step 2: 올바른 RED를 확인한다**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~TagCatalogAllocationTests
```

Expected: 실제 allocation이 8,200,000을 넘어 FAIL.

- [ ] **Step 3: `Build`가 build data를 반환하게 한다**

`TagCatalog.Build.cs`에서 기존 build body와 preorder 호출은 유지하고 반환형/return을 다음처럼 바꾼다.

```csharp
private static BuildData Build(List<ExplicitTag> explicitTags)
```

```csharp
return new BuildData(byCanonicalName, displayNames, parents, subtreeEnds);
```

같은 partial class 안에 다음 type을 추가한다.

```csharp
private readonly struct BuildData
{
    internal BuildData(
        Dictionary<string, ushort> byCanonicalName,
        string[] displayNames,
        ushort[] parents,
        ushort[] subtreeEnds)
    {
        ByCanonicalName = byCanonicalName;
        DisplayNames = displayNames;
        Parents = parents;
        SubtreeEnds = subtreeEnds;
    }

    internal Dictionary<string, ushort> ByCanonicalName { get; }
    internal string[] DisplayNames { get; }
    internal ushort[] Parents { get; }
    internal ushort[] SubtreeEnds { get; }
}
```

- [ ] **Step 4: 임시 fingerprint constructor를 제거한다**

`TagCatalog.cs`의 4-argument private constructor를 삭제하고 `Create`를 다음으로 교체한다.

```csharp
private static TagCatalog Create(
    List<ExplicitTag> explicitTags,
    List<RedirectDefinition> definitions)
{
    var build = Build(explicitTags);
    var redirects = BuildRedirects(
        definitions, build.ByCanonicalName, out var fingerprintRedirects);
    var canonicalNames = CreateCanonicalNames(
        build.ByCanonicalName, build.DisplayNames.Length);
    var fingerprint = ComputeFingerprint(1, canonicalNames, fingerprintRedirects);
    return new TagCatalog(
        build.ByCanonicalName,
        redirects,
        build.DisplayNames,
        build.Parents,
        build.SubtreeEnds,
        fingerprint);
}
```

- [ ] **Step 5: allocation/fingerprint/전체 suite를 GREEN으로 확인한다**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore `
  --filter "FullyQualifiedName~TagCatalogAllocationTests|FullyQualifiedName~TagCatalogFingerprintTests"
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore
```

Expected: allocation 8,200,000 이하, fingerprint 값 변화 없음, 전체 FAIL 0.

- [ ] **Step 6: commit한다**

```powershell
git add -- common/tests/Bun3.Gameplay.Tests/TagCatalogAllocationTests.cs `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Build.cs `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs
git commit -m "⚡ GameplayTag 카탈로그 fingerprint 단일 계산" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: `TagContainer` exact tag를 caller span에 복사한다

**Files:**
- Modify: `common/tests/Bun3.Gameplay.Tests/TagContainerTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayUnitySmokeTests.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagContainer.cs`

**Interfaces:**
- Consumes: `ExactKindCount`와 내부 index 정렬 invariant.
- Produces: `public int CopyExactTags(Span<GameplayTag> destination)`.

- [ ] **Step 1: 정렬/빈 상태/원자적 short buffer 실패 테스트를 쓴다**

```csharp
[Test]
public void Copy_exact_tags_returns_catalog_order_and_empty_is_zero()
{
    var tags = _catalog.CreateContainer();
    Span<GameplayTag> empty = stackalloc GameplayTag[0];
    Assert.That(tags.CopyExactTags(empty), Is.Zero);

    tags.Add(_rooted);
    tags.Add(_ghost);
    Span<GameplayTag> destination = stackalloc GameplayTag[2];
    var copied = tags.CopyExactTags(destination);

    Assert.That(copied, Is.EqualTo(2));
    Assert.That(destination[0], Is.EqualTo(_ghost));
    Assert.That(destination[1], Is.EqualTo(_rooted));
}

[Test]
public void Copy_exact_tags_rejects_a_short_destination_before_writing()
{
    var tags = _catalog.CreateContainer();
    tags.Add(_ghost);
    tags.Add(_rooted);
    var destination = new[] { _state };

    Assert.Throws<ArgumentException>(() => tags.CopyExactTags(destination.AsSpan()));
    Assert.That(destination[0], Is.EqualTo(_state));
}
```

`GameplayUnitySmokeTests.Tag_catalog_round_trips_public_wire_indices_in_unity` 끝에 추가한다.

```csharp
var tags = catalog.CreateContainer(1);
tags.Add(catalog.GetRequired("State.Dead"));
Span<GameplayTag> copiedTags = stackalloc GameplayTag[1];
Assert.That(tags.CopyExactTags(copiedTags), Is.EqualTo(1));
Assert.That(copiedTags[0], Is.EqualTo(catalog.GetRequired("State.Dead")));
```

- [ ] **Step 2: method 부재로 RED인지 확인한다**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~TagContainerTests
```

Expected: `CopyExactTags` 부재 compile FAIL.

- [ ] **Step 3: public method를 최소 구현한다**

```csharp
/// <summary>명시적으로 저장된 태그를 카탈로그 인덱스 오름차순으로 복사합니다.</summary>
/// <param name="destination">명시 태그를 받을 버퍼입니다.</param>
/// <returns>복사한 태그 수입니다.</returns>
/// <exception cref="ArgumentException">버퍼 길이가 명시 태그 종류 수보다 작은 경우입니다.</exception>
public int CopyExactTags(Span<GameplayTag> destination)
{
    if (destination.Length < _count)
        throw new ArgumentException(
            "The destination is too small for the exact tags.", nameof(destination));
    for (var i = 0; i < _count; i++)
        destination[i] = new GameplayTag(_indices[i]);
    return _count;
}
```

- [ ] **Step 4: .NET/Unity를 GREEN으로 확인한다**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~TagContainerTests
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: exact copy 의미와 Unity smoke PASS.

- [ ] **Step 5: commit한다**

```powershell
git add -- common/tests/Bun3.Gameplay.Tests/TagContainerTests.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayUnitySmokeTests.cs `
  common/src/com.bun3.gameplay/Runtime/Tags/TagContainer.cs
git commit -m "✨ TagContainer exact 태그 복사 지원" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: `TagCountContainer` exact entry를 caller span에 복사한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCountEntry.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCountEntry.cs.meta`
- Modify: `common/tests/Bun3.Gameplay.Tests/TagCountContainerTests.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayUnitySmokeTests.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCountContainer.cs`

**Interfaces:**
- Consumes: Task 5 `CopyExactTags`, `ExactKindCount`, 내부 sorted entry 배열.
- Produces: public readonly `TagCountEntry`, `CopyExactEntries(Span<TagCountEntry>)`, 두 copy interface allocation 0.

- [ ] **Step 1: exact-only/정렬/값 동등성/short buffer 실패 테스트를 쓴다**

```csharp
[Test]
public void Copy_exact_entries_excludes_aggregate_only_parents_and_preserves_counts()
{
    _counts.Add(_rooted, 4);
    _counts.Add(_ghost, 2);
    Span<TagCountEntry> destination = stackalloc TagCountEntry[2];
    var copied = _counts.CopyExactEntries(destination);

    Assert.That(copied, Is.EqualTo(2));
    Assert.That(destination[0], Is.EqualTo(new TagCountEntry(_ghost, 2)));
    Assert.That(destination[1], Is.EqualTo(new TagCountEntry(_rooted, 4)));
    Assert.That(destination[0] == new TagCountEntry(_ghost, 2), Is.True);
    Assert.That(destination[0] != destination[1], Is.True);
}

[Test]
public void Copy_exact_entries_handles_empty_and_rejects_short_destination_atomically()
{
    Span<TagCountEntry> empty = stackalloc TagCountEntry[0];
    Assert.That(_counts.CopyExactEntries(empty), Is.Zero);
    _counts.Add(_ghost);
    _counts.Add(_rooted);
    var sentinel = new TagCountEntry(_state, 7);
    var destination = new[] { sentinel };

    Assert.Throws<ArgumentException>(() => _counts.CopyExactEntries(destination.AsSpan()));
    Assert.That(destination[0], Is.EqualTo(sentinel));
    Assert.That(default(TagCountEntry).Tag, Is.EqualTo(GameplayTag.None));
    Assert.That(default(TagCountEntry).Count, Is.Zero);
}
```

Unity smoke에 다음을 추가한다.

```csharp
var counts = catalog.CreateCountContainer(1);
counts.Add(catalog.GetRequired("State.Dead"), 3);
Span<TagCountEntry> copiedCounts = stackalloc TagCountEntry[1];
Assert.That(counts.CopyExactEntries(copiedCounts), Is.EqualTo(1));
Assert.That(copiedCounts[0].Tag, Is.EqualTo(catalog.GetRequired("State.Dead")));
Assert.That(copiedCounts[0].Count, Is.EqualTo(3));
```

- [ ] **Step 2: 새 type/method 부재로 RED인지 확인한다**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore `
  --filter FullyQualifiedName~TagCountContainerTests
```

Expected: `TagCountEntry`/`CopyExactEntries` 부재 compile FAIL.

- [ ] **Step 3: `TagCountEntry`와 Unity meta를 만든다**

`TagCountEntry.cs`:

```csharp
#nullable enable
using System;

namespace Bun3.Gameplay.Tags
{
    /// <summary>명시적으로 저장된 게임플레이 태그와 양수 count의 값 쌍입니다.</summary>
    public readonly struct TagCountEntry : IEquatable<TagCountEntry>
    {
        internal TagCountEntry(GameplayTag tag, int count)
        {
            Tag = tag;
            Count = count;
        }

        /// <summary>명시적으로 저장된 태그를 가져옵니다.</summary>
        public GameplayTag Tag { get; }

        /// <summary>태그에 직접 저장된 count를 가져옵니다.</summary>
        public int Count { get; }

        /// <summary>태그와 count가 모두 같은지 비교합니다.</summary>
        public bool Equals(TagCountEntry other) => Tag == other.Tag && Count == other.Count;

        /// <summary>지정한 객체가 같은 태그와 count를 가지는지 비교합니다.</summary>
        public override bool Equals(object? obj) => obj is TagCountEntry other && Equals(other);

        /// <summary>태그와 count를 결합한 hash code를 반환합니다.</summary>
        public override int GetHashCode() => unchecked((Tag.GetHashCode() * 397) ^ Count);

        /// <summary>두 entry의 태그와 count가 모두 같은지 비교합니다.</summary>
        public static bool operator ==(TagCountEntry left, TagCountEntry right) => left.Equals(right);

        /// <summary>두 entry의 태그 또는 count가 다른지 비교합니다.</summary>
        public static bool operator !=(TagCountEntry left, TagCountEntry right) => !left.Equals(right);
    }
}
```

`TagCountEntry.cs.meta`:

```yaml
fileFormatVersion: 2
guid: 8c6577b496f049d9987b71a0c04f3f35
```

- [ ] **Step 4: `CopyExactEntries`를 구현한다**

```csharp
/// <summary>명시적으로 저장된 태그와 count를 카탈로그 인덱스 오름차순으로 복사합니다.</summary>
/// <param name="destination">명시 태그와 count를 받을 버퍼입니다.</param>
/// <returns>복사한 entry 수입니다.</returns>
/// <exception cref="ArgumentException">버퍼 길이가 명시 태그 종류 수보다 작은 경우입니다.</exception>
public int CopyExactEntries(Span<TagCountEntry> destination)
{
    if (destination.Length < _exactKindCount)
        throw new ArgumentException(
            "The destination is too small for the exact entries.", nameof(destination));

    var copied = 0;
    for (var i = 0; i < _entryCount; i++)
    {
        if (_exactCounts[i] == 0) continue;
        destination[copied++] = new TagCountEntry(
            new GameplayTag(_indices[i]), _exactCounts[i]);
    }

    return copied;
}
```

- [ ] **Step 5: copy allocation 실패 테스트를 추가한다**

`AllocationSmokeTests`에 Task 1의 `MethodImpl` import를 사용해 추가한다.

```csharp
[Test]
public void Exact_state_copy_does_not_allocate()
{
    var catalog = TagCatalogTestData.Load();
    var ghost = catalog.GetRequired("State.Dead.Ghost");
    var rooted = catalog.GetRequired("State.Rooted");
    var tags = catalog.CreateContainer(2);
    var counts = catalog.CreateCountContainer(2);
    tags.Add(rooted);
    tags.Add(ghost);
    counts.Add(rooted, 4);
    counts.Add(ghost, 2);

    _ = MeasureExactCopies(tags, counts, 1, out _);
    var allocated = MeasureExactCopies(tags, counts, 100_000, out var copied);

    Assert.That(allocated, Is.Zero);
    Assert.That(copied, Is.EqualTo(400_000));
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static long MeasureExactCopies(
    TagContainer tags, TagCountContainer counts, int iterations, out int copied)
{
    Span<GameplayTag> tagBuffer = stackalloc GameplayTag[64];
    Span<TagCountEntry> countBuffer = stackalloc TagCountEntry[64];
    var before = GC.GetAllocatedBytesForCurrentThread();
    copied = 0;
    for (var i = 0; i < iterations; i++)
    {
        copied += tags.CopyExactTags(tagBuffer);
        copied += counts.CopyExactEntries(countBuffer);
    }

    return GC.GetAllocatedBytesForCurrentThread() - before;
}
```

- [ ] **Step 6: .NET/Unity를 GREEN으로 확인한다**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore `
  --filter "FullyQualifiedName~TagCountContainerTests|FullyQualifiedName~AllocationSmokeTests.Exact_state_copy_does_not_allocate"
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: exact entry 의미, 원자성, 동등성, allocation 0, Unity smoke PASS.

- [ ] **Step 7: commit한다**

```powershell
git add -- common/src/com.bun3.gameplay/Runtime/Tags/TagCountEntry.cs `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCountEntry.cs.meta `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCountContainer.cs `
  common/tests/Bun3.Gameplay.Tests/TagCountContainerTests.cs `
  common/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayUnitySmokeTests.cs
git commit -m "✨ TagCountContainer exact 상태 복사 지원" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: 0.6.0 metadata와 전체 릴리스 계약을 검증한다

**Files:**
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Modify: `common/src/com.bun3.gameplay/package.json`
- Verify: Tasks 1-6의 전체 변경.

**Interfaces:**
- Consumes: Tasks 1-6 테스트와 public interface.
- Produces: NuGet/UPM version `0.6.0`, warning-zero build, verified nupkg metadata.

- [ ] **Step 1: 두 metadata를 함께 0.6.0으로 올린다**

```xml
<Version>0.6.0</Version>
```

```json
"version": "0.6.0"
```

Newtonsoft.Json `[13.0.2]`, UPM dependency `3.2.2`, Unity `2022.3`은 유지한다.

- [ ] **Step 2: warning-zero build와 전체 .NET suite를 실행한다**

```powershell
dotnet build common/src/com.bun3.gameplay/Bun3.Gameplay.csproj -c Release --no-restore --warnaserror
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore
```

Expected: build warning/error 0, test FAIL 0.

- [ ] **Step 3: Unity EditMode 전체 suite를 실행한다**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

Expected: dirty lifecycle, conformance, public copy smoke 포함 FAIL 0.

- [ ] **Step 4: isolated output에 pack하고 metadata를 read back한다**

```powershell
$ErrorActionPreference = 'Stop'
$artifactRoot = (Resolve-Path -LiteralPath 'common/artifacts').Path
$packDir = Join-Path $artifactRoot ('gameplay-review-fixes-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $packDir -ErrorAction Stop | Out-Null
dotnet pack common/src/com.bun3.gameplay/Bun3.Gameplay.csproj `
  -c Release --no-restore --nologo -o $packDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack failed.' }

$packages = @(Get-ChildItem -LiteralPath $packDir -Filter 'Bun3.Gameplay.0.6.0.nupkg')
if ($packages.Count -ne 1) { throw "Expected one 0.6.0 nupkg, got $($packages.Count)." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $entry = $archive.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -Single
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $metadata = $nuspec.package.metadata
    $dependency = $metadata.dependencies.group.dependency |
      Where-Object { $_.id -eq 'Newtonsoft.Json' }
    if ($metadata.version -ne '0.6.0' -or $dependency.version -ne '[13.0.2]') {
        throw 'NuGet metadata mismatch.'
    }
} finally { $archive.Dispose() }

$upm = Get-Content -LiteralPath 'common/src/com.bun3.gameplay/package.json' -Raw |
  ConvertFrom-Json
if ($upm.version -ne '0.6.0' -or $upm.unity -ne '2022.3' -or
    $upm.dependencies.'com.unity.nuget.newtonsoft-json' -ne '3.2.2') {
    throw 'UPM metadata mismatch.'
}
```

Expected: nupkg 0.6.0, NuGet dependency `[13.0.2]`, UPM Unity `2022.3`/dependency `3.2.2`. 게시하지 않는다.

- [ ] **Step 5: diff와 workspace 소유권을 확인한다**

```powershell
git diff --check
git status --short
```

Expected: whitespace error 없음. `?? unity/GameplayTags.json`은 untracked로 남고 commit 대상에서 제외된다.

- [ ] **Step 6: metadata를 commit한다**

```powershell
git add -- common/src/com.bun3.gameplay/Bun3.Gameplay.csproj `
  common/src/com.bun3.gameplay/package.json
git commit -m "🔖 Bun3.Gameplay 0.6.0 버전 갱신" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 7: commit 이후 최종 증거를 다시 수집한다**

```powershell
git status --short
git log -8 --oneline
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release --no-restore
```

Expected: 사용자 소유 `unity/GameplayTags.json` 외 작업 변경 없음, Tasks 1-7 commit 확인, .NET FAIL 0.

## Plan Self-Review Result

- Spec coverage: dirty 교체/close/quit/domain reload/save 실패, allocation smoke, fingerprint 단일 계산, 두 exact copy interface, 값 동등성, allocation 0, 0.6.0과 .NET/Unity/pack 검증을 모두 task에 매핑했다.
- Type consistency: `UnsavedChangesDecision`, `TagCountEntry`, `CopyExactTags`, `CopyExactEntries`, `HandleBeforeAssemblyReload` 이름과 signature가 일치한다.
- Mutation check: decision branch 교환, save 실패 무시, short span 부분 쓰기, aggregate entry 노출, copy 순서 반전, 임시 fingerprint 재도입, iterator allocation을 테스트가 잡는다.
- Scope: JSON schema, tag 의미, 자동 복구, iterator와 aggregate 외부 노출은 포함하지 않는다.
