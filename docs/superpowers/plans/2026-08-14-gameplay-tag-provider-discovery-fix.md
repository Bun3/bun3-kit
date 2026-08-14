# GameplayTag Provider Discovery Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unity의 전역 Provider 탐색에서 EditMode 테스트 더블을 제외하고, 실제 게임 Provider가 0개 또는 여러 개인 경우 후보를 식별할 수 있는 진단을 제공한다.

**Architecture:** `IGameplayTagBuildContextProvider` 공개 계약과 게임별 단일 Provider 정책은 유지한다. Editor 전용 discovery helper가 `TypeCache` 결과 중 NUnit을 참조하는 테스트 어셈블리를 제외하고, development resolver와 Published validator가 같은 후보 선택 및 진단 포맷을 사용한다. 테스트에서 명시적으로 주입한 타입 목록은 기존대로 필터링하지 않아 격리된 Provider 계약 테스트를 유지한다.

**Tech Stack:** Unity 6000.3 EditMode, C# 9, NUnit, Unity `TypeCache`

## Global Constraints

- 패키지 코드는 `netstandard2.1` 및 C# 9 블록 네임스페이스를 유지한다.
- 공개 `IGameplayTagBuildContextProvider` 인터페이스는 변경하지 않는다.
- 게임별 실제 Provider는 정확히 하나여야 하며, 0개 또는 여러 개면 build context를 만들지 않는다.
- Unity 테스트 어셈블리의 Provider 더블은 자동 탐색 후보가 아니다.
- 빌드 경고는 0이어야 한다.

---

### Task 1: Provider 자동 탐색 경계 수정

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagBuildContextProviderDiscovery.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagBuildContextProviderDiscovery.cs.meta`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagBuildContextResolver.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPublishedCatalogValidator.cs`
- Test: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagEditorWorkspaceTests.cs`
- Test: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagBuildPlayerProcessorTests.cs`

**Interfaces:**
- Consumes: `TypeCache.GetTypesDerivedFrom<IGameplayTagBuildContextProvider>()`, 기존 internal injected provider-list overloads
- Produces: 테스트 어셈블리를 제외한 global provider discovery와 정렬된 후보 타입 진단

- [x] **Step 1: 실제 global discovery 경로를 재현하는 실패 테스트 작성**

  실제 `TypeCache`를 사용하는 discovery가 현재 NUnit 테스트 어셈블리의 Provider 더블을 하나도 반환하지 않아야 한다. 이 검증은 소비 게임에 정상 Provider가 있어도 통과해야 한다. 명시적으로 두 Provider를 주입한 resolver 진단은 두 후보의 전체 타입 이름을 ordinal 순서로 포함해야 한다.

- [x] **Step 2: 집중 EditMode 테스트를 실행해 RED 확인**

  Run:

  ```powershell
  & common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
    -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests;Bun3.Gameplay.Unity.Tests.GameplayTagBuildPlayerProcessorTests'
  ```

  Expected: global resolver/validator가 테스트 더블을 6개 발견해 새 테스트가 실패한다.

- [x] **Step 3: 공통 discovery helper와 진단 포맷 최소 구현**

  `GameplayTagBuildContextProviderDiscovery.Discover()`는 TypeCache 결과를 열거하고, 해당 타입의 어셈블리가 `nunit.framework`를 직접 참조하면 제외한다. `SelectCandidates(...)`는 기존 abstract/generic/interface/parameterless-constructor 규칙을 한곳에서 적용한다. `FormatCandidateCount(...)`는 0개면 기존 `0.` 형식을 유지하고, 2개 이상이면 ordinal 정렬된 전체 타입 이름을 추가한다.

- [x] **Step 4: development와 Published 경로를 공통 helper로 이관**

  Public/global entry point만 테스트 어셈블리를 제외한 `Discover()`를 사용한다. Internal injected-list entry point는 전달받은 테스트 타입을 그대로 `SelectCandidates(...)`에 넘겨 기존 테스트 seam을 보존한다.

- [x] **Step 5: 집중 테스트와 generated project 경고 0 빌드 확인**

  Run:

  ```powershell
  & common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
    -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests;Bun3.Gameplay.Unity.Tests.GameplayTagBuildPlayerProcessorTests'
  dotnet build unity/Bun3.Gameplay.Editor.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror
  dotnet build unity/Bun3.Gameplay.Unity.Tests.csproj --no-restore -p:NoWarn=MSB3277 -warnaserror
  ```

  Expected: focused tests 전부 통과, 두 build 모두 warning 0/error 0.

- [x] **Step 6: 전체 Unity EditMode 회귀 검증**

  Run:

  ```powershell
  & common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode -AllEditMode
  ```

  Expected: failure 0, C# warning/error 0.

- [x] **Step 7: 범위와 사용자 파일 보존 확인 후 커밋**

  `unity/ProjectSettings/GameplayTags.json`, `unity/GameplayTags.json`, `.superpowers/`, `artifacts/`, `unity/TestResult/`는 staging하지 않는다. 변경한 Source/Test/meta/plan만 검토하고 커밋한다.
