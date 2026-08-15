# GameplayTag Editor Save Shortcut Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 포커스된 GameplayTag 에디터의 `Ctrl+S`/`Cmd+S`를 카탈로그 JSON 저장으로 전환한다.

**Architecture:** `GameplayTagCatalogWindow.OnGUI`가 그리기 전에 현재 IMGUI 키 이벤트를 검사한다. 정확한 저장 단축키이면 이벤트를 항상 소비하고, 열린 dirty 카탈로그가 있을 때만 기존 `SaveChanges()`를 호출해 저장·오류·dirty 생명주기를 재사용한다.

**Tech Stack:** C# 9, Unity Editor IMGUI, `EditorWindow`, `UnityEngine.Event`

## Global Constraints

- `GameplayTagCatalogWindow`가 키보드 포커스를 가진 동안에만 동작한다.
- Shift·Alt 없는 `Ctrl+S` 또는 `Cmd+S`만 처리한다.
- 일치하는 이벤트는 카탈로그 유무와 관계없이 소비한다.
- 카탈로그가 열려 있고 dirty일 때만 JSON을 쓴다.
- 신규 자동화 테스트는 사용자 결정에 따라 추가하지 않는다.
- 사용자 소유 `.superpowers/`와 `unity/GameplayTags.json`은 수정하거나 커밋하지 않는다.

---

### Task 1: 포커스 저장 단축키 처리

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`

**Interfaces:**
- Consumes: `GameplayTagCatalogWindow.SaveChanges()`, `_controller.Session`, `_controller.IsDirty`, `Event.current`
- Produces: `private void HandleSaveShortcut(Event currentEvent)`

- [ ] **Step 1: OnGUI 진입 시 저장 단축키를 먼저 처리한다**

`OnGUI`의 첫 줄에서 현재 이벤트를 전달한다.

```csharp
private void OnGUI()
{
    HandleSaveShortcut(Event.current);
    EnsureTreeViewState();
    // 기존 draw 순서 유지
}
```

- [ ] **Step 2: 정확한 단축키를 소비하고 기존 저장 경로를 호출한다**

다른 조합은 건드리지 않고, 정확한 저장 단축키는 먼저 소비한다. 열린 dirty 세션만 `SaveChanges()`로 저장한다.

```csharp
private void HandleSaveShortcut(Event currentEvent)
{
    if (currentEvent.type != EventType.KeyDown
        || currentEvent.keyCode != KeyCode.S
        || (!currentEvent.control && !currentEvent.command)
        || currentEvent.shift
        || currentEvent.alt)
    {
        return;
    }

    currentEvent.Use();
    if (_controller.Session is not null && _controller.IsDirty)
    {
        SaveChanges();
    }
}
```

- [ ] **Step 3: Unity 에디터 어셈블리를 컴파일한다**

Run:

```powershell
dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -v:minimal
```

Expected: 오류 0. 생성된 Unity csproj의 기존 MSB3277 어셈블리 충돌 경고 외 신규 C# 진단 없음.

- [ ] **Step 4: 열린 Unity에서 포커스 동작을 수동 검증한다**

1. `Gameplay/Tag Editor`를 열고 카탈로그를 로드한다.
2. 태그를 수정해 dirty 상태로 만든 뒤 창에 포커스를 두고 `Ctrl+S`를 누른다.
3. JSON이 갱신되고 dirty 표시가 사라지는지 확인한다.
4. 카탈로그가 없는 상태에서도 `Ctrl+S`가 Unity 씬 저장으로 전파되지 않는지 확인한다.
5. Tag Editor 밖에 포커스를 두면 Unity의 기존 `Ctrl+S`가 유지되는지 확인한다.

- [ ] **Step 5: 의도한 파일만 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs
git commit -m "✨ GameplayTag 에디터 저장 단축키 지원" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
