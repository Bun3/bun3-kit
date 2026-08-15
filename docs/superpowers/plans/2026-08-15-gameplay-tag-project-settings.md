# GameplayTag Project Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 코드 Provider가 없는 일반 Unity 프로젝트도 Project Settings의 Catalog ID만으로 GameplayTag Editor, Picker, local Play와 development Catalog를 사용할 수 있게 한다.

**Architecture:** `GameplayTagProjectSettings`가 `ProjectSettings/GameplayTagSettings.asset`의 Catalog ID를 소유하고, development resolver는 코드 Provider가 없을 때 이 값을 fallback으로 사용한다. 코드 Provider는 외부 Source와 Published build의 고급 확장점으로 유지하며 settings ID와의 일치를 강제한다. Project Settings 페이지와 Tag Editor 인라인 설정은 같은 정규화·저장 서비스를 사용하고, controller refresh가 dirty in-memory session을 보존한다.

**Tech Stack:** Unity 6000.3, C# 9, Unity Editor `ScriptableSingleton<T>`/`SettingsProvider`, IMGUI, NUnit EditMode

## Global Constraints

- Game Source는 계속 `ProjectSettings/GameplayTags.json` 하나이며 settings asset에 tag/comment/redirect를 저장하지 않는다.
- Catalog ID는 invariant lowercase ASCII 영숫자와 `-`만 사용하고, UI 입력은 저장 전에 canonicalize한다.
- settings fallback은 development 전용이며 Published Player build는 concrete 코드 Provider가 정확히 하나 없으면 실패한다.
- 기존 단일 코드 Provider 프로젝트는 settings 파일 없이 계속 동작해야 한다.
- settings와 단일 코드 Provider가 함께 있으면 Catalog ID가 ordinal exact match여야 한다.
- Provider 2개 이상은 기존 `B3TAG3001` 후보 진단으로 fail closed한다.
- C# 9 블록 namespace, `#nullable enable`, public 멤버 한국어 XML 문서와 warning 0 규칙을 유지한다.
- `.superpowers/`, `artifacts/`, `unity/GameplayTags.json`, `unity/ProjectSettings/GameplayTags.json`, `unity/TestResult/`는 staging하지 않는다.

---

### Task 1: Catalog ID와 Project Settings 저장 경계

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogId.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogId.cs.meta`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagProjectSettings.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagProjectSettings.cs.meta`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagProjectSettingsProvider.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagProjectSettingsProvider.cs.meta`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagProjectSettingsTests.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagProjectSettingsTests.cs.meta`

**Interfaces:**
- Produces: `GameplayTagCatalogId.Normalize(string) : string`
- Produces: `GameplayTagCatalogId.Require(string, string) : string`
- Produces: `GameplayTagProjectSettings.ReadConfiguredCatalogId() : string?`
- Produces: `GameplayTagProjectSettings.SaveCatalogId(string) : string`
- Produces: `GameplayTagProjectSettings.GetSuggestedCatalogId(string) : string`
- Produces: `GameplayTagProjectSettingsProvider.CreateProvider() : SettingsProvider`
- Produces: `GameplayTagProjectSettingsProvider.GetProviderStatus(IReadOnlyList<Type>, string?) : GameplayTagProjectSettingsProviderStatus`

- [ ] **Step 1: 정규화와 설정 저장 계약의 실패 테스트 작성**

`GameplayTagProjectSettingsTests.cs`에 다음 테스트를 먼저 작성한다. 실제 프로젝트 settings 파일을 건드리지 않도록 persistence에는 `Action<string>`을 주입한다.

```csharp
/// <summary>제품 이름과 사용자 입력이 안정적인 소문자 Catalog ID로 정규화되는지 검증합니다.</summary>
[TestCase("Jurassic Paradise", "jurassic-paradise")]
[TestCase("Bun3.Game.Core", "bun3-game-core")]
[TestCase("  GAME__SERVER  ", "game-server")]
[TestCase("한글 게임", "")]
public void Catalog_id_normalization_is_deterministic(string input, string expected)
{
    Assert.That(GameplayTagCatalogId.Normalize(input), Is.EqualTo(expected));
}

/// <summary>유효하지 않은 ID는 persistence를 호출하지 않는지 검증합니다.</summary>
[Test]
public void Empty_normalized_id_is_rejected_before_persistence()
{
    var saveCount = 0;
    Assert.Throws<ArgumentException>(() =>
        GameplayTagProjectSettings.ApplyCatalogId("---", _ => saveCount++));
    Assert.That(saveCount, Is.Zero);
}

/// <summary>설정 Provider가 Unity의 표준 Project Settings 경로로 등록되는지 검증합니다.</summary>
[Test]
public void Settings_provider_uses_the_project_gameplay_tags_path()
{
    var provider = GameplayTagProjectSettingsProvider.CreateProvider();
    Assert.That(provider.settingsPath, Is.EqualTo("Project/Gameplay Tags"));
    Assert.That(provider.scope, Is.EqualTo(SettingsScope.Project));
}
```

`ApplyCatalogId(" My Game ", persist)`가 `my-game`을 정확히 한 번 전달하고 반환하는 테스트도 추가한다.
`ReadConfiguredCatalogId()` 호출 전후 실제 `ProjectSettings/GameplayTagSettings.asset`의 존재 여부 또는 기존
bytes가 같아 읽기만으로 설정을 생성·수정하지 않는 테스트를 추가한다. 이 테스트는 파일을 쓰지 않는다.

Provider 상태 formatter에는 후보 0, 단일 matching ID, 단일 mismatching ID, 복수 후보를 주입한다. 단일
Provider의 전체 타입 이름이 표시되고 mismatch/복수는 error 상태이며 복수 이름은 ordinal 정렬인지 검증한다.

- [ ] **Step 2: focused Unity test를 실행해 RED 확인**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagProjectSettingsTests'
```

Expected: 새 settings 타입 부재로 compiler RED. 테스트 작성 오류나 unrelated failure는 먼저 제거한다.

- [ ] **Step 3: Catalog ID canonicalizer 최소 구현**

```csharp
internal static string Normalize(string value)
{
    if (value is null) throw new ArgumentNullException(nameof(value));
    var result = new StringBuilder(value.Length);
    var pendingSeparator = false;
    foreach (var input in value)
    {
        var character = char.ToLowerInvariant(input);
        if ((character >= 'a' && character <= 'z')
            || (character >= '0' && character <= '9'))
        {
            if (pendingSeparator && result.Length > 0) result.Append('-');
            result.Append(character);
            pendingSeparator = false;
        }
        else
        {
            pendingSeparator = true;
        }
    }

    return result.ToString();
}

internal static string Require(string value, string parameterName)
{
    var result = Normalize(value);
    if (result.Length == 0)
        throw new ArgumentException("Catalog ID는 하나 이상의 ASCII 영숫자를 포함해야 합니다.", parameterName);
    return result;
}
```

- [ ] **Step 4: ScriptableSingleton 저장 모델과 test seam 구현**

`GameplayTagProjectSettings.cs`는 읽기만으로 disk save를 호출하지 않는다.

```csharp
[FilePath("ProjectSettings/GameplayTagSettings.asset", FilePathAttribute.Location.ProjectFolder)]
internal sealed class GameplayTagProjectSettings : ScriptableSingleton<GameplayTagProjectSettings>
{
    [SerializeField] private string _catalogId = string.Empty;

    internal static string? ReadConfiguredCatalogId()
    {
        var result = GameplayTagCatalogId.Normalize(instance._catalogId ?? string.Empty);
        return result.Length == 0 ? null : result;
    }

    internal static string GetSuggestedCatalogId(string productName) =>
        GameplayTagCatalogId.Normalize(productName ?? throw new ArgumentNullException(nameof(productName)));

    internal static string ApplyCatalogId(string value, Action<string> persist)
    {
        if (persist is null) throw new ArgumentNullException(nameof(persist));
        var result = GameplayTagCatalogId.Require(value, nameof(value));
        persist(result);
        return result;
    }

    internal static string SaveCatalogId(string value) =>
        ApplyCatalogId(value, result =>
        {
            instance._catalogId = result;
            instance.Save(true);
        });
}
```

- [ ] **Step 5: Project Settings 페이지 최소 구현**

`GameplayTagProjectSettingsProvider.CreateProvider()`는 `[SettingsProvider]` factory이고 path를 `Project/Gameplay Tags`로 고정한다. 저장값이 없으면 `GetSuggestedCatalogId(PlayerSettings.productName)`을 필드에 채우되 Apply 전에는 저장하지 않는다. Apply는 `SaveCatalogId`를 호출하고 실패는 `GameplayTagDiagnosticsPanel.ShowWarning`으로 한 번 표시한다.

`GetProviderStatus(...)`는 기존 `GameplayTagBuildContextProviderDiscovery.SelectCandidates(...)` 결과를 사용한다.
0개는 development fallback 안내, 1개는 전체 타입 이름과 ID 일치 여부, 2개 이상은 ordinal 정렬 후보와 error
상태를 반환한다. SettingsProvider GUI는 이 결과를 HelpBox로 그려 코드 Provider 없음/활성/mismatch/복수를
모두 보여 준다. 별도의 resolver 규칙을 만들지 않고 Task 2의 selection matrix와 같은 count/ID 규칙을
사용한다. 페이지는 Catalog ID가 안정적인 제품 ID이며 Published build에는 코드 Provider가 필요하다는
HelpBox도 표시한다.

```csharp
internal readonly struct GameplayTagProjectSettingsProviderStatus
{
    internal GameplayTagProjectSettingsProviderStatus(string message, MessageType messageType)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        MessageType = messageType;
    }

    internal string Message { get; }
    internal MessageType MessageType { get; }
}
```

- [ ] **Step 6: focused GREEN과 generated Editor build 확인**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagProjectSettingsTests'
dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
```

Expected: 새 fixture 전부 PASS, Editor build warning 0/error 0.

- [ ] **Step 7: Task 1 범위 커밋**

새 code/test/meta 8개만 stage하고 `git diff --cached --check` 후 다음 제목으로 커밋한다.

```text
✨ GameplayTag Project Settings 추가
Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 2: Development fallback와 Published 경계

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagBuildContextResolution.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagBuildContextResolver.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPublishedCatalogValidator.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagEditorWorkspace.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagEditorWorkspaceTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagBuildPlayerProcessorTests.cs`

**Interfaces:**
- Adds: `GameplayTagBuildContextResolution.RequiresCatalogConfiguration : bool` (internal)
- Adds: `GameplayTagEditorWorkspace.RequiresCatalogConfiguration : bool` (internal)
- Adds: `ResolveDevelopment(string, IReadOnlyList<Type>, IReadOnlyList<string>, string?)`
- Adds: `ResolveAndValidate(IReadOnlyList<Type>, string?)`

- [ ] **Step 1: resolver selection matrix의 실패 테스트 작성**

`GameplayTagEditorWorkspaceTests`에서 기존 missing-provider 기대를 `B3TAG3004`로 바꾸고 다음 핵심 테스트를 추가한다.

```csharp
/// <summary>코드 Provider가 없어도 Project Settings ID로 완전한 개발 context를 만드는지 검증합니다.</summary>
[Test]
public void Project_settings_catalog_id_is_the_development_fallback_without_a_provider()
{
    var path = WriteGameSource("game.json", "ability.jump");
    var resolution = GameplayTagBuildContextResolver.ResolveDevelopment(
        path, Array.Empty<Type>(), Array.Empty<string>(), "jurassic-paradise");

    Assert.That(resolution.HasCompleteContext, Is.True);
    Assert.That(resolution.RequiresCatalogConfiguration, Is.False);
    Assert.That(resolution.Context!.Identity.CatalogId, Is.EqualTo("jurassic-paradise"));
    Assert.That(resolution.Context.Sources, Has.Count.EqualTo(1));
}
```

추가 matrix는 Provider 0/settings null, Provider 1/settings null, Provider 1/settings same, Provider 1/settings different, Provider 2/settings present를 모두 포함한다.

- [ ] **Step 2: Published fail-closed 실패 테스트 작성**

```csharp
/// <summary>Project Settings만으로 Published build Provider 요구를 우회하지 못하는지 검증합니다.</summary>
[Test]
public void Project_settings_is_development_only_and_does_not_replace_a_published_provider()
{
    var error = Assert.Throws<BuildFailedException>(() =>
        GameplayTagPublishedCatalogValidator.ResolveAndValidate(
            Array.Empty<Type>(), "jurassic-paradise"));
    Assert.That(error!.Message, Does.Contain("Published"));
    Assert.That(error.Message, Does.Contain("development"));
}
```

단일 Provider/settings ID mismatch도 artifact open 전에 실패하는지 검증한다.

- [ ] **Step 3: exact focused filter로 RED 확인**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests;Bun3.Gameplay.Unity.Tests.GameplayTagBuildPlayerProcessorTests'
```

Expected: 새 overload/property 부재로 compiler RED.

- [ ] **Step 4: structured configuration state 구현**

`GameplayTagBuildContextResolution`의 두 internal constructor에 `bool requiresCatalogConfiguration`을 명시적으로 추가하고 모든 call site를 갱신한다. `GameplayTagEditorWorkspace`는 다음으로 전달한다.

```csharp
internal bool RequiresCatalogConfiguration => _resolution.RequiresCatalogConfiguration;
```

- [ ] **Step 5: development selection matrix 최소 구현**

public entry point는 settings를 읽어 4-argument seam으로 넘긴다.

```csharp
public static GameplayTagBuildContextResolution ResolveDevelopment(string gameSourcePath) =>
    ResolveDevelopment(
        gameSourcePath,
        GameplayTagBuildContextProviderDiscovery.Discover(),
        DiscoverInstalledPackageMetadataPaths(),
        GameplayTagProjectSettings.ReadConfiguredCatalogId());
```

candidates 2개 이상은 `B3TAG3001`; 0개/settings null은 `B3TAG3004: GameplayTag Catalog settings are not configured.`와 `RequiresCatalogConfiguration=true`; 0개/settings present는 settings ID와 empty external paths; 1개는 기존 Provider를 사용하되 settings가 있으면 ID exact match를 강제한다. 불일치는 `B3TAG3002`이고 source open 전에 반환한다.

- [ ] **Step 6: Published selection과 ID 일치 검증 구현**

global Published entry point도 settings ID를 읽는다. candidate 0은 settings가 있어도 `Project Settings configures development only; exactly one gameplay tag build context provider is required for a Published build.` 의미로 실패한다. candidate 1/settings present는 provider ID와 먼저 비교하고 그 다음 context ID를 검증한다.

- [ ] **Step 7: focused GREEN과 generated warning-zero build**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagProjectSettingsTests;Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests;Bun3.Gameplay.Unity.Tests.GameplayTagBuildPlayerProcessorTests'
dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
dotnet build unity/Bun3.Gameplay.Unity.Tests.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
```

Expected: focused fixtures PASS, 두 build warning 0/error 0.

- [ ] **Step 8: Task 2 범위 커밋**

열거한 production/test 6개만 stage하고 `git diff --cached --check` 후 다음 제목으로 커밋한다.

```text
✨ GameplayTag 설정 기반 개발 context 지원
Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 3: Tag Editor 인라인 Configure Catalog 흐름

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`

**Interfaces:**
- Adds: `GameplayTagCatalogWindowController.ConfigureCatalog(string) : void`
- Adds: `GameplayTagCatalogWindowController.RequiresCatalogConfiguration : bool`
- Adds constructor seam: `Func<string, string> saveCatalogId`
- Adds window seam: configure warning handler `Action<string, string>`

- [ ] **Step 1: controller configure/rollback 실패 테스트 작성**

```csharp
/// <summary>Catalog 설정 후 dirty Game Source와 선택을 보존한 채 Workspace를 다시 여는지 검증합니다.</summary>
[Test]
public void Configure_catalog_preserves_dirty_session_and_selection_while_refreshing_workspace()
{
    var path = Path.Combine(_temporaryDirectory, "GameplayTags.json");
    GameplayTagCatalogFileAdapter.CreateGameSource(path);
    string? configuredId = null;
    var controller = CreateControllerWithSettings(path, () => configuredId,
        value => configuredId = GameplayTagCatalogId.Require(value, nameof(value)));
    controller.Add("State.Dead");

    controller.ConfigureCatalog("Jurassic Paradise");

    Assert.That(configuredId, Is.EqualTo("jurassic-paradise"));
    Assert.That(controller.RequiresCatalogConfiguration, Is.False);
    Assert.That(controller.IsDirty, Is.True);
    Assert.That(controller.SelectedPath, Is.EqualTo("state.dead"));
    Assert.That(controller.Session!.Serialize(), Does.Contain("state.dead"));
}
```

save delegate가 throw할 때 Workspace, session serialization, dirty, selection이 모두 이전 값인지 `TryExecute`로 검증한다.

- [ ] **Step 2: window policy와 warning-once 실패 테스트 작성**

Provider/settings missing controller에서 configure box policy가 true이고 설정 완료 후 false인지 internal seam으로 검증한다. invalid ID에서 injected warning이 정확히 한 번 호출되고 controller 상태가 바뀌지 않아야 한다. Project Settings open action은 exact path `Project/Gameplay Tags`를 전달해야 한다.

- [ ] **Step 3: focused RED 확인**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

Expected: 새 controller/window API 부재로 compiler RED.

- [ ] **Step 4: controller 설정 저장과 dirty-safe refresh 구현**

기존 constructors는 production `GameplayTagProjectSettings.SaveCatalogId`로 위임한다. 최종 internal constructor만 `Func<string,string>`을 받는다.

```csharp
internal bool RequiresCatalogConfiguration => _workspace.RequiresCatalogConfiguration;

internal void ConfigureCatalog(string catalogId)
{
    _ = _saveCatalogId(catalogId);
    InstallRefreshedWorkspace(CreateRefreshedWorkspace());
}
```

`ReplaceWorkspace()`를 사용하지 않아 dirty/selection을 초기화하지 않으며 Window는 반드시 `controller.TryExecute`를 통해 호출한다.

- [ ] **Step 5: Tag Editor 인라인 UI 최소 구현**

Window의 Catalog ID 필드는 `OnEnable`에서 저장값이 없으면 `GetSuggestedCatalogId(PlayerSettings.productName)`으로 채운다. `OnGUI` 순서는 diagnostics → configure box → toolbar다.

```text
Configure GameplayTag Catalog
Catalog ID [jurassic-paradise]
[Save Settings] [Open Project Settings]
```

Save 성공 시 tree/unsaved/repaint를 갱신한다. 실패 시 `GameplayTagDiagnosticsPanel.ShowWarning("Configure GameplayTag Catalog", error.Message)`를 정확히 한 번 호출한다. Project Settings button은 `SettingsService.OpenProjectSettings("Project/Gameplay Tags")`를 호출한다. box는 `RequiresCatalogConfiguration`일 때만 표시한다.

- [ ] **Step 6: focused GREEN과 generated build**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests;Bun3.Gameplay.Unity.Tests.GameplayTagProjectSettingsTests;Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests'
dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
dotnet build unity/Bun3.Gameplay.Unity.Tests.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
```

Expected: focused fixtures PASS, C# diagnostics 0, 두 build warning 0/error 0.

- [ ] **Step 7: Task 3 범위 커밋**

Window/controller/test 3개만 stage하고 `git diff --cached --check` 후 다음 제목으로 커밋한다.

```text
✨ Tag Editor Catalog 설정 흐름 추가
Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
```

---

### Task 4: 전체 검증과 문서 정합성

**Files:**
- Modify if required: `CONTEXT.md`
- Modify only for completion bookkeeping: `docs/superpowers/plans/2026-08-15-gameplay-tag-project-settings.md`

**Interfaces:**
- Verifies: settings fallback, optional custom Provider, Published fail-closed, Tag Editor inline configuration

- [ ] **Step 1: domain language 확인**

`CONTEXT.md`에 필요한 경우에만 “GameplayTag Project Settings는 Unity Editor가 소유하는 Catalog ID 설정이며 Tag Source가 아니다”를 추가한다. 새 용어가 불필요하면 변경하지 않는다.

- [ ] **Step 2: 전체 generated warning-zero build**

```powershell
dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
dotnet build unity/Bun3.Gameplay.Unity.Tests.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror -v:minimal
```

Expected: 각각 warning 0/error 0.

- [ ] **Step 3: 전체 Unity EditMode**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode -AllEditMode
```

Expected: failed 0/skipped 0, log의 C# 및 GUI style diagnostics 0. 실행 후 `git diff -- unity/ProjectSettings/ProjectSettings.asset`을 확인하고 runner가 제거한 exact existing define token만 `apply_patch`로 복원한다.

- [ ] **Step 4: 실제 no-provider setup 경로 확인**

코드 Provider 0, Game Source 존재 상태에서 settings를 저장한 뒤 `B3TAG3004` 제거, tree 유지, local development artifact 생성, Published preflight의 code Provider 요구를 확인한다. 생성된 사용자 `ProjectSettings/GameplayTagSettings.asset`은 자동 staging하지 않는다.

- [ ] **Step 5: 최종 scope와 hygiene 확인**

```powershell
git diff --check
git status --short
git diff -- unity/ProjectSettings/ProjectSettings.asset
```

tracked 변경은 계획의 code/test/meta/CONTEXT뿐이어야 하며 기존 user-owned untracked 경로는 유지한다. CONTEXT가 변경된 경우에만 문서 커밋을 별도로 만들고, 변경이 없으면 빈 마무리 커밋을 만들지 않는다.
