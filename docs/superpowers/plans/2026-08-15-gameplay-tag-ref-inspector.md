# GameplayTagRef Inspector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unity 자산에 canonical GameplayTag 경로를 안정적으로 저장하고 기존 병합 Picker로 선택하는 `GameplayTagRef` Inspector를 제공한다.

**Architecture:** 코어의 2바이트 `GameplayTag`는 유지하고, 새 `Bun3.Gameplay.Unity` runtime Adapter assembly가 직렬화 문자열 reference를 소유한다. Editor PropertyDrawer는 고정 Game Source로 현재 Workspace를 resolve하고 기존 live Picker를 열며, selection은 `SerializedProperty`를 통해 Undo 가능한 방식으로 적용한다.

**Tech Stack:** C# 9, Unity 2022.3, Unity IMGUI/SerializedProperty/PropertyDrawer, NUnit EditMode, existing GameplayTag Workspace/Picker.

## Global Constraints

- `GameplayTag`의 2바이트 layout과 binary Catalog 포맷을 변경하지 않는다.
- `Bun3.Gameplay` 코어 assembly는 `noEngineReferences: true`를 유지한다.
- Game Source는 `ProjectSettings/GameplayTags.json` 하나만 사용한다.
- Picker는 병합 Runtime projection, 이름 filter, 자동 expand, expand 복원, 양방향 scroll을 재사용한다.
- invalid Workspace에서 raw 직렬화 문자열을 보존하고 신규 선택만 막는다.
- 공개 멤버는 한국어 XML 문서를 작성하고 모든 C#은 `#nullable enable`, block namespace, C# 9를 사용한다.
- 새 production 동작은 먼저 실패하는 Unity EditMode 테스트를 확인한 뒤 구현한다.
- user-owned `unity/ProjectSettings/GameplayTagSettings.asset`과 `GameplayTags.json`은 stage하거나 변경하지 않는다.
- Gameplay NuGet/UPM version은 `0.11.0`으로 함께 올린다.

---

### Task 1: Unity runtime Adapter와 GameplayTagRef

**Files:**
- Create: `common/src/com.bun3.gameplay/Unity/Bun3.Gameplay.Unity.asmdef`
- Create: `common/src/com.bun3.gameplay/Unity/Tags/GameplayTagRef.cs`
- Create: matching Unity `.meta` files
- Modify: `common/src/com.bun3.gameplay/Runtime/AssemblyInfo.cs`
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/Bun3.Gameplay.Tests.asmdef`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagRefTests.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagRefTests.cs.meta`

**Interfaces:**
- Consumes: internal `TagName.TryFold`, public `TagCatalog.TryGet/GetRequired`, `GameplayTag.None`.
- Produces: `GameplayTagRef(string)`, `Path`, `IsEmpty`, `TryResolve(TagCatalog, out GameplayTag)`, `ResolveRequired(TagCatalog)`, equality operators.

- [x] **Step 1: Write failing runtime-reference tests**

Add tests whose literal expectations prove:

```csharp
Assert.That(new GameplayTagRef("Ability.Attack").Path, Is.EqualTo("ability.attack"));
Assert.That(default(GameplayTagRef).TryResolve(catalog, out var none), Is.True);
Assert.That(none, Is.EqualTo(GameplayTag.None));
Assert.That(new GameplayTagRef("missing.tag").TryResolve(catalog, out _), Is.False);
Assert.Throws<ArgumentException>(() => new GameplayTagRef("bad..tag"));
```

Also set the private serialized `_path` through a real `SerializedObject` host to prove malformed legacy raw text is preserved and `TryResolve` returns false instead of mutating it.

- [x] **Step 2: Run RED**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagRefTests'
```

Expected: `GameplayTagRef`/Unity Adapter가 없어서 `CS0246` 또는 asmdef reference failure.

- [x] **Step 3: Implement the Unity Adapter**

Create a Unity runtime assembly referencing `Bun3.Gameplay`, keep the type in namespace
`Bun3.Gameplay.Tags`, and give that assembly internal access to `TagName`. Exclude `Unity/**/*.cs`
from the netstandard NuGet core csproj. Use this interface and semantics:

```csharp
[Serializable]
public struct GameplayTagRef : IEquatable<GameplayTagRef>
{
    [SerializeField]
    private string? _path;

    public static readonly GameplayTagRef None = default;
    public string Path => _path ?? string.Empty;
    public bool IsEmpty => Path.Length == 0;

    public GameplayTagRef(string path)
    {
        if (!TagName.TryFold(path, out var canonical))
            throw new ArgumentException("태그 경로 문법이 올바르지 않습니다.", nameof(path));
        _path = canonical;
    }

    public bool TryResolve(TagCatalog catalog, out GameplayTag tag)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (IsEmpty) { tag = GameplayTag.None; return true; }
        if (!TagName.TryFold(Path, out _)) { tag = GameplayTag.None; return false; }
        return catalog.TryGet(Path, out tag);
    }

    public GameplayTag ResolveRequired(TagCatalog catalog) =>
        IsEmpty ? GameplayTag.None : catalog.GetRequired(Path);
}
```

Complete equality/hash/operators with ordinal path semantics and Korean XML docs. The asmdef must be runtime-capable,
reference only `Bun3.Gameplay`, and keep engine references enabled for `[SerializeField]`.

- [x] **Step 4: Run focused GREEN**

Run the exact filter from Step 2. Expected: all `GameplayTagRefTests` pass with C# diagnostics 0.

- [x] **Step 5: Commit Task 1**

Stage only Task 1 code/test/asmdef/meta files and commit with a gitmoji Korean subject and the required co-author trailer.

### Task 2: PropertyDrawer와 live Picker 연결

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRefDrawer.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRefDrawer.cs.meta`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRefInspectorWorkspace.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRefInspectorWorkspace.cs.meta`
- Modify: `common/src/com.bun3.gameplay/Editor/Bun3.Gameplay.Editor.asmdef`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagRefDrawerTests.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagRefDrawerTests.cs.meta`

**Interfaces:**
- Consumes: `GameplayTagRef`, `GameplayTagPickerWindow.ShowLive`, `GameplayTagBuildContextResolver.ResolveDevelopment`, `GameplayTagEditorWorkspace.Open`, `GameplayTagGameSourcePath.Get`.
- Produces: one-line PropertyDrawer, clear/select application through `SerializedProperty`, cached current Workspace validation.

- [x] **Step 1: Write failing drawer behavior tests**

Use real `ScriptableObject` hosts and `SerializedObject` instances. Prove these observable behaviors:

```csharp
GameplayTagRefDrawer.ApplyPath(targets, "_tag", "ability.attack");
Assert.That(first.Tag.Path, Is.EqualTo("ability.attack"));
Assert.That(second.Tag.Path, Is.EqualTo("ability.attack"));
Undo.PerformUndo();
Assert.That(first.Tag.IsEmpty, Is.True);
```

Add separate tests for clear, mixed values, invalid raw display content, and current invalid Workspace preserving
the raw value while disabling Picker selection.

- [x] **Step 2: Run RED**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagRefDrawerTests;Bun3.Gameplay.Unity.Tests.GameplayTagPickerTests'
```

Expected: drawer/workspace adapter types are missing.

- [x] **Step 3: Implement the drawer and Workspace Adapter**

Implement the PropertyDrawer with label, dropdown text and clear button. Capture target objects and property path
when opening `ShowLive`; apply callback values using fresh `SerializedObject` instances. The Workspace adapter must
reuse the fixed Game Source resolver, cache only briefly, expose the current invalid state, and never substitute a
last-good snapshot.

```csharp
[CustomPropertyDrawer(typeof(GameplayTagRef))]
internal sealed class GameplayTagRefDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var path = property.FindPropertyRelative("_path");
        // PrefixLabel -> warning-aware dropdown -> None clear button.
        // Mixed values use EditorGUI.showMixedValue and an empty initial Picker selection.
    }

    internal static void ApplyPath(
        IReadOnlyList<UnityEngine.Object> targets,
        string propertyPath,
        string canonicalPath)
    {
        // Re-open each target with SerializedObject, find `_path`, assign, and
        // ApplyModifiedProperties so Undo/prefab override use Unity's real path.
    }
}
```

The dropdown must call:

```csharp
GameplayTagPickerWindow.ShowLive(
    GameplayTagRefInspectorWorkspace.OpenCurrent,
    path.hasMultipleDifferentValues ? string.Empty : path.stringValue,
    selected => GameplayTagRefDrawer.ApplyPath(targets, property.propertyPath, selected));
```

`GameplayTagRefInspectorWorkspace.OpenCurrent` computes the fixed path from `Application.dataPath`, then calls
`GameplayTagEditorWorkspace.Open(GameplayTagBuildContextResolver.ResolveDevelopment(path), path)`. Cache that exact
current result for at most 0.75 seconds and invalidate on `EditorApplication.projectChanged`; do not retain an older
successful snapshot after a failed refresh.

- [x] **Step 4: Run focused GREEN**

Run the exact filter from Step 2. Expected: drawer and existing Picker tests all pass, C#/GUI diagnostics 0.

- [x] **Step 5: Commit Task 2**

Stage only Task 2 code/test/meta/asmdef files and commit with the required message/trailer.

### Task 3: Domain docs, package metadata, and full verification

**Files:**
- Modify: `CONTEXT.md`
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Modify: `common/src/com.bun3.gameplay/package.json`
- Modify if generated references require it: Unity generated project files are never committed.

**Interfaces:**
- Consumes: completed Unity Adapter and PropertyDrawer.
- Produces: documented `GameplayTagRef` language and Gameplay package version `0.11.0`.

- [x] **Step 1: Update domain language and package metadata**

Add `GameplayTagRef` as Unity 자산에 canonical path를 저장하고 Runtime Catalog에서 `GameplayTag`로 resolve하는
authoring reference. State that it is not a Runtime Catalog handle. Set NuGet and UPM versions to `0.11.0` and update
the UPM description so only the core assembly is described as UnityEngine-free.

- [x] **Step 2: Run generated warning-zero builds**

```powershell
dotnet build unity/Bun3.Gameplay.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
dotnet build unity/Bun3.Gameplay.Unity.Tests.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
```

Expected: each warning 0/error 0. If Unity generates a separate Adapter csproj, build it sequentially with the same gate.

- [x] **Step 3: Run full Unity EditMode**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode -AllEditMode
```

Expected: failed/skipped/inconclusive 0, C# diagnostics 0, GUI style diagnostics 0. Inspect
`unity/ProjectSettings/ProjectSettings.asset` and restore only the exact runner-removed existing define token by
`apply_patch` if present.

- [x] **Step 4: Pack and inspect UPM**

Create an isolated archive, then assert `package.json` version `0.11.0`, Unity Adapter C#/asmdef and every matching
`.meta` exactly once. Confirm NuGet package version `0.11.0` while Unity Adapter source is not compiled into the
netstandard core DLL.

- [x] **Step 5: Final scope check and commit**

```powershell
git diff --check
git status --short
git diff -- unity/ProjectSettings/ProjectSettings.asset
```

Do not stage project-owned `GameplayTagSettings.asset`, `GameplayTags.json`, local artifacts or TestResult. Commit only
CONTEXT/package metadata and any final scoped corrections with the required gitmoji Korean subject and co-author trailer.
