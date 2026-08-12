# GameplayTag 불변 카탈로그·컨테이너·Unity 에디터 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 사람이 편집하는 단일 JSON 태그 목록을 서버와 Unity가 같은 불변 `ushort` 카탈로그로 로드하고, 일정한 비용의 Unreal식 `TagContainer`/`TagCountContainer` 및 Unity 작성 UI를 제공한다.

**Architecture:** `Bun3.Gameplay`가 JSON 파싱·검증·결정적 preorder 인덱싱·redirect·fingerprint와 두 컨테이너를 소유하는 깊은 공용 모듈이 된다. 문자열과 JSON은 기동/데이터 로드 seam에서만 사용하고 simulation은 2바이트 `GameplayTag`, 조밀 카탈로그 배열, 컨테이너별 정렬 배열만 사용한다. Unity 전용 Editor 어셈블리는 공용 로더로 모든 변경을 재검증한 뒤 같은 JSON을 원자적으로 저장하며, NuGet 소스에는 포함되지 않는다.

**Tech Stack:** C# 9, netstandard2.1, Newtonsoft.Json 13.0.2 / Unity Newtonsoft JSON UPM 3.2.2, NUnit 4(.NET), Unity Test Framework 1.6, Unity 6000.3.14f1, IMGUI `TreeView`, SHA-256

## Global Constraints

- 결정 원본은 [`../specs/2026-08-12-gameplay-tag-catalog-design.md`](../specs/2026-08-12-gameplay-tag-catalog-design.md)다. 구현 중 의미를 바꿔야 하면 코드를 임의로 바꾸지 말고 스펙을 먼저 수정·승인받는다.
- 프레임워크는 schema/loader/container/editor만 제공하고 게임별 태그 JSON이나 도메인 태그는 이 저장소에 넣지 않는다.
- 공용 런타임은 `common/src/com.bun3.gameplay` 아래 `netstandard2.1`, C# 9 블록 namespace로 작성하고 Unity 타입을 참조하지 않는다.
- 모든 package/Editor C# 파일은 Unity 컴파일의 nullable 문맥을 고정하도록 첫 줄에 `#nullable enable`을 둔다. 모든 public 타입·멤버는 한국어 XML 문서를 갖고 빌드 경고·오류는 0이어야 한다.
- 태그 이름은 `^[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*$`, 전체 255 ASCII 문자 이하, 깊이 16 이하이며 ASCII 대소문자를 무시한다. 숫자 세그먼트를 허용하고 `01`과 `1`은 구분한다.
- 암시 부모 포함 활성 노드는 최대 65,535개다. 인덱스 0은 `None`, 나머지는 canonical sibling 정렬 후 preorder로 부여한 `ushort`다.
- 런타임 등록·재로드·hash-only identity를 추가하지 않는다. DB/세이브 정본은 경로 문자열이고 wire 인덱스는 같은 fingerprint 세션에서만 받는다.
- 동결 `TagCatalog`는 동시 읽기 안전하게 만들고 컨테이너는 단일 simulation owner가 mutation하는 전제로 locking을 넣지 않는다. Burst/NativeArray 어댑터는 실제 Job 전환 요구가 생기기 전까지 이 버전에 추가하지 않는다.
- `TagContainer`와 `TagCountContainer`의 exact 종류는 각각 최대 64개다. 단일 조회 비교 상한은 각각 7회와 11회이며 steady-state 조회 할당은 0이다.
- JSON 의존성은 NuGet `Newtonsoft.Json` exact `[13.0.2]`, UPM `com.unity.nuget.newtonsoft-json` exact `3.2.2`로 선언한다. Unity 호스트에 우연히 설치된 전이 의존성에 기대지 않는다.
- Unity UPM 3.2.2는 asmdef를 제공하지 않고 `Newtonsoft.Json.dll` precompiled plugin을 `isExplicitlyReferenced: 0`으로 제공한다. 따라서 `Bun3.Gameplay`와 Editor asmdef는 `overrideReferences: false`를 유지해 이 DLL을 자동 참조하며 존재하지 않는 `Unity.Newtonsoft.Json` asmdef reference나 `precompiledReferences`를 추가하지 않는다. 각 Unity compile GREEN이 실제 참조 경계를 증명한다.
- `Bun3.Gameplay.csproj`과 `package.json`은 첫 package 코드 변경부터 `0.5.0`으로 함께 올리되, 이 계획 전체 검증 전에는 pack 산출물을 게시하지 않는다. 같은 `0.5.0`을 두 번 게시하지 않는다.
- Unity 최소 버전은 `2022.3`을 유지한다. 검증 Editor는 `E:\Unitys\6000.3.14f1\Editor\Unity.exe`다.
- 새 Unity-visible 폴더/파일의 `.meta`는 Unity import로 생성한다. GUID를 손으로 작성하거나 복사하지 않는다. 생성된 루트 `.csproj`, `.sln`, `Library`, `Logs`, `Temp`, `UserSettings`는 커밋하지 않는다.
- 각 Unity 실행 직후 `git status --short`를 읽고 package `.meta`/의도한 lock 외 `ProjectSettings` 등 tracked 변경이 생기면 원인을 확인해 원래 의미로 복원한 뒤 다음 단계로 간다.
- 각 커밋은 gitmoji 제목과 정확한 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` 트레일러를 사용한다.
- 각 `git commit` 직전에는 해당 task 경로만 stage하고 `git diff --cached --check`와 `git diff --cached --stat`을 실행한다. 실패하거나 계획 밖 파일이 보이면 commit하지 않는다.

## File Structure

### 공용 런타임

- Modify `common/src/com.bun3.gameplay/Runtime/Tags/GameplayTag.cs`: 2바이트 값 타입과 raw 생성 차단.
- Create `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogException.cs`: JSON/semantic validation 위치가 담긴 공개 예외.
- Create `common/src/com.bun3.gameplay/Runtime/Tags/TagName.cs`: ASCII 문법 검증·case fold 전용 내부 모듈.
- Create `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs`: 동결 데이터와 공개 조회/컨테이너 factory.
- Create `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Json.cs`: strict JSON 읽기와 작성 DTO 해석.
- Create `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Build.cs`: 암시 부모, deterministic tree/preorder, 배열 구축.
- Create `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Fingerprint.cs`: canonical `BTAG` byte stream과 SHA-256.
- Create `common/src/com.bun3.gameplay/Runtime/AssemblyInfo.cs`: Unity shared conformance assembly에만 runtime internal golden 접근 허용.
- Create `common/src/com.bun3.gameplay/Runtime/Tags/TagSearch.cs`: 비교 횟수를 관측할 수 있는 정렬 `ushort` lower-bound.
- Create `common/src/com.bun3.gameplay/Runtime/Tags/TagContainer.cs`: 최대 64종의 일반 집합.
- Create `common/src/com.bun3.gameplay/Runtime/Tags/TagCountContainer.cs`: 최대 64종 exact와 최대 1,024개 aggregate entry.
- Delete `common/src/com.bun3.gameplay/Runtime/Tags/TagRegistry.cs` and `.meta`: 동적 등록 제거.
- Delete `common/src/com.bun3.gameplay/Runtime/Tags/TagSet.cs` and `.meta`: 역할이 불명확한 기존 카운트 맵 제거.

### .NET 및 공유 계약 테스트

- Create `common/tests/Bun3.Gameplay.Tests/GameplayTagTests.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/TagCatalogTestData.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/TagCatalogLoadingTests.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/TagCatalogValidationTests.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/TagCatalogRedirectTests.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/TagCatalogFingerprintTests.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/TagContainerTests.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/TagCountContainerTests.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/TagPerformanceContractTests.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/TagPerformanceBenchmarkTests.cs`.
- Create `common/tests/Bun3.Gameplay.Tests/Assert-TagPerformance.ps1`: 세 runtime의 XML/log를 같은 규칙으로 판정.
- Create `common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1`: import/EditMode/Mono/IL2CPP 실행과 XML·로그 readback을 한 경계로 고정.
- Create `common/tests/Bun3.Gameplay.Tests/LegacyTagApiRemovalTests.cs`.
- Delete `common/tests/Bun3.Gameplay.Tests/TagRegistryTests.cs`.
- Delete `common/tests/Bun3.Gameplay.Tests/TagSetTests.cs`.
- Modify `common/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs`.
- Modify `common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`: shared conformance test link.
- Create `common/src/com.bun3.gameplay/Tests/Editor/TagCatalogConformanceTests.cs`: .NET/Unity가 같은 소스를 실행하는 골든 계약.
- Create `common/src/com.bun3.gameplay/Tests/Runtime/TagPerformanceFixture.cs`: .NET/Mono/IL2CPP가 공유하는 workload와 legacy baseline.

### Unity 작성 어댑터

- Create `common/src/com.bun3.gameplay/Editor/Bun3.Gameplay.Editor.asmdef`.
- Create `common/src/com.bun3.gameplay/Editor/AssemblyInfo.cs`.
- Create `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogEditSession.cs`: 비 UI transactional authoring 모듈.
- Create `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogFileAdapter.cs`: UTF-8 파일 저장과 `AssetDatabase` import.
- Create `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogViewModel.cs`: 검색·트리·선택 상태.
- Create `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs`: 파일·session·dirty·selection을 묶는 테스트 가능한 창 workflow.
- Create `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeView.cs`: IMGUI TreeView 렌더링.
- Create `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagValidationWindow.cs`: line/path/cause 표시.
- Create `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`: 메뉴와 작성 workflow.
- Create Editor tests under `common/src/com.bun3.gameplay/Tests/Editor/` for session, file adapter, view model, window smoke.
- Create `common/src/com.bun3.gameplay/Tests/Runtime/Bun3.Gameplay.Runtime.Tests.asmdef` and `TagRuntimePerformanceTests.cs`: Mono/IL2CPP player 성능·할당 검증.
- Create two checked-in Test Framework settings JSON files under `Tests/Runtime/`: `TagTests.Mono.json`, `TagTests.IL2CPP.json`.

### 패키지 경계

- Modify `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`: version, exact JSON dependency, Editor exclusion.
- Modify `common/src/com.bun3.gameplay/package.json`: version과 UPM JSON dependency.
- Modify by Unity resolver `unity/Packages/packages-lock.json`: local gameplay dependency 아래 JSON dependency를 기록. `manifest.json`의 local path는 유지한다.
- Modify `common/src/com.bun3.gameplay/Tests/Editor/Bun3.Gameplay.Tests.asmdef`: Editor assembly reference.

---

### Task 1: `GameplayTag`를 2바이트 카탈로그 인덱스로 전환하고 0.5.0 경계를 연다

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/GameplayTag.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/GameplayTagTests.cs`
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Modify: `common/src/com.bun3.gameplay/package.json`
- Create: `common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1`
- Modify by Unity resolver: `unity/Packages/packages-lock.json`

**Interfaces:**
- Produces: `GameplayTag.None`, `bool IsValid`, `ushort Index`, equality/hash, internal `GameplayTag(ushort index)`.
- Produces package contract: NuGet `Newtonsoft.Json [13.0.2]`, UPM `com.unity.nuget.newtonsoft-json 3.2.2`, version `0.5.0`.
- Invariant: raw `ushort` public constructor는 없고 이후 `TagCatalog`만 값을 복원한다.
- Transitional: 기존 `TagRegistry`/`TagSet`이 Task 6까지 컴파일되도록 internal `GameplayTag(int)`와 internal `Handle` getter만 임시 유지하고 Task 6에서 함께 제거한다.

- [ ] **Step 1: 2바이트 표현과 공개 생성 차단 RED 테스트를 쓴다**

```csharp
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class GameplayTagTests
{
    [Test]
    public void Representation_is_two_byte_catalog_index()
    {
        var tag = new GameplayTag(42);

        Assert.That(Marshal.SizeOf<GameplayTag>(), Is.EqualTo(sizeof(ushort)));
        Assert.That(tag.Index, Is.EqualTo((ushort)42));
        Assert.That(tag.IsValid, Is.True);
        Assert.That(GameplayTag.None.Index, Is.Zero);
        Assert.That(GameplayTag.None.IsValid, Is.False);
    }

    [Test]
    public void Equality_and_hash_use_only_the_index()
    {
        var left = new GameplayTag(19);
        var right = new GameplayTag(19);
        var other = new GameplayTag(20);

        Assert.That(left, Is.EqualTo(right));
        Assert.That(left.GetHashCode(), Is.EqualTo(19));
        Assert.That(left == right, Is.True);
        Assert.That(left != other, Is.True);
    }

    [Test]
    public void Raw_index_constructor_is_not_public()
    {
        Assert.That(
            typeof(GameplayTag).GetConstructors()
                .Any(c => c.GetParameters().Length == 1
                    && c.GetParameters()[0].ParameterType == typeof(ushort)),
            Is.False);
    }
}
```

- [ ] **Step 2: 테스트를 실행해 현재 4바이트 `Handle` 계약 때문에 실패함을 확인한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~GameplayTagTests"
```

Expected: `GameplayTag(int)`/`Index` 컴파일 실패 또는 크기 4 assertion 실패. 기존 테스트 실패는 이 단계에서 실행하지 않는다.

- [ ] **Step 3: `GameplayTag`를 최소 2바이트 값 타입으로 교체한다**

```csharp
#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Bun3.Gameplay.Tags
{
    /// <summary>동결된 태그 카탈로그 안의 2바이트 태그 인덱스입니다.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        private readonly ushort _index;

        internal GameplayTag(ushort index) => _index = index;

        // Task 6에서 TagRegistry/TagSet과 함께 제거하는 컴파일 전용 migration shim.
        internal GameplayTag(int index) => _index = checked((ushort)index);
        internal int Handle => _index;

        /// <summary>태그가 없음을 나타내는 기본값입니다.</summary>
        public static readonly GameplayTag None = default;

        /// <summary>현재 카탈로그에서 사용하는 런타임 인덱스를 가져옵니다.</summary>
        public ushort Index => _index;

        /// <summary><see cref="None"/>이 아닌지 나타냅니다.</summary>
        public bool IsValid => _index != 0;

        /// <inheritdoc />
        public bool Equals(GameplayTag other) => _index == other._index;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is GameplayTag other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _index;

        /// <summary>두 태그의 런타임 인덱스가 같은지 비교합니다.</summary>
        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Equals(right);

        /// <summary>두 태그의 런타임 인덱스가 다른지 비교합니다.</summary>
        public static bool operator !=(GameplayTag left, GameplayTag right) => !left.Equals(right);
    }
}
```

- [ ] **Step 4: package metadata와 JSON 의존성을 고정한다**

`Bun3.Gameplay.csproj`에 다음을 반영한다.

```xml
<Version>0.5.0</Version>
<ItemGroup>
  <PackageReference Include="Newtonsoft.Json" Version="[13.0.2]" />
</ItemGroup>

<ItemGroup>
  <Compile Remove="Editor/**/*.cs" />
  <Compile Remove="Tests/**/*.cs" />
</ItemGroup>
```

`package.json`은 기존 필드를 유지하고 다음 값을 반영한다.

```json
"version": "0.5.0",
"dependencies": {
  "com.unity.nuget.newtonsoft-json": "3.2.2"
}
```

반복되는 Unity import/test 명령은 `Invoke-GameplayUnityTests.ps1`에 한 번 고정한다.

```powershell
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Import', 'EditMode', 'Player')]
  [string]$Mode,
  [string]$TestFilter,
  [switch]$AllEditMode,
  [ValidateSet('Mono', 'IL2CPP')]
  [string]$Backend = 'Mono'
)
$ErrorActionPreference = 'Stop'
$unityEditor = 'E:\Unitys\6000.3.14f1\Editor\Unity.exe'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$unityProject = (Resolve-Path (Join-Path $repoRoot 'unity')).Path
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
$logPath = Join-Path $env:TEMP "bun3-gameplay-$($Mode.ToLowerInvariant())-$stamp.log"

function Invoke-Unity([string[]]$Arguments) {
  & $unityEditor @Arguments
  $unityExit = $LASTEXITCODE
  & git -C $repoRoot status --short
  if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect repository state after Unity.' }
  if ($unityExit -ne 0) { throw "Unity $Mode failed with exit code $unityExit." }
  if (Select-String -LiteralPath $logPath -Pattern 'warning CS|error CS|Compilation failed' -Quiet) {
    throw "Unity $Mode emitted a C# diagnostic: $logPath"
  }
}

if ($Mode -eq 'Import') {
  Invoke-Unity @('-batchmode', '-quit', '-projectPath', $unityProject, '-logFile', $logPath)
  return
}

$resultPath = Join-Path $env:TEMP "bun3-gameplay-$($Mode.ToLowerInvariant())-$stamp.xml"
if ($Mode -eq 'EditMode') {
  $arguments = @(
    '-batchmode', '-projectPath', $unityProject,
    '-runTests', '-testPlatform', 'EditMode',
    '-testResults', $resultPath, '-logFile', $logPath)
  if (-not $AllEditMode) { $arguments += @('-assemblyNames', 'Bun3.Gameplay.Unity.Tests') }
  if ($TestFilter) { $arguments += @('-testFilter', $TestFilter) }
  Invoke-Unity $arguments
} else {
  $backendLower = $Backend.ToLowerInvariant()
  $settingsPath = Join-Path $repoRoot `
    "common/src/com.bun3.gameplay/Tests/Runtime/TagTests.$Backend.json"
  $buildDirectory = Join-Path $env:TEMP "bun3-tags-$backendLower-$stamp"
  $null = New-Item -ItemType Directory -Path $buildDirectory
  $buildPath = Join-Path $buildDirectory 'Bun3TagsTests.exe'
  Invoke-Unity @(
    '-batchmode', '-projectPath', $unityProject,
    '-runTests', '-testPlatform', 'StandaloneWindows64',
    '-assemblyNames', 'Bun3.Gameplay.Runtime.Tests',
    '-testSettingsFile', (Resolve-Path $settingsPath).Path,
    '-buildPlayerPath', $buildPath,
    '-testResults', $resultPath, '-logFile', $logPath)
}

if (-not (Test-Path -LiteralPath $resultPath)) { throw "Unity result XML missing: $resultPath" }
[xml]$results = Get-Content -Raw -Encoding UTF8 -LiteralPath $resultPath
if ([int]$results.'test-run'.testcasecount -eq 0 `
  -or $results.'test-run'.result -ne 'Passed' `
  -or [int]$results.'test-run'.failed -ne 0) {
  throw "Unity $Mode failed or discovered zero tests: $resultPath"
}
if (-not (Select-String -LiteralPath $logPath -Pattern 'Run completed' -Quiet)) {
  throw "Unity Test Runner did not report completion: $logPath"
}
if ($Mode -eq 'Player') {
  & (Join-Path $PSScriptRoot 'Assert-TagPerformance.ps1') `
    -ResultPath $resultPath -ExpectedBackend $Backend
}
[pscustomobject]@{ ResultPath = $resultPath; LogPath = $logPath }
```

- [ ] **Step 5: 대상 테스트와 restore를 GREEN으로 만든다**

Run:

```powershell
dotnet restore common/src/com.bun3.gameplay/Bun3.Gameplay.csproj --nologo
if ($LASTEXITCODE -ne 0) { throw 'Gameplay package restore failed.' }
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~GameplayTagTests"
if ($LASTEXITCODE -ne 0) { throw 'GameplayTag .NET tests failed.' }
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
```

Expected: `GameplayTagTests` 3건 PASS, Unity import/EditMode warning CS 0, packages-lock의 local gameplay dependency에 Newtonsoft 3.2.2가 기록된다. 기존 `TagRegistry`/`TagSet`은 아직 `int Handle`을 사용하므로 전체 .NET suite는 Task 6 전까지 실행하지 않는다.

- [ ] **Step 6: 첫 breaking-contract 커밋을 만든다**

```powershell
git add -- common/src/com.bun3.gameplay/Runtime/Tags/GameplayTag.cs `
  common/src/com.bun3.gameplay/Bun3.Gameplay.csproj `
  common/src/com.bun3.gameplay/package.json `
  common/tests/Bun3.Gameplay.Tests/GameplayTagTests.cs `
  common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 `
  unity/Packages/packages-lock.json
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 1 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "💥 GameplayTag 인덱스를 ushort로 전환" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 1 failed.' }
```

### Task 2: strict JSON을 불변 `TagCatalog`로 로드한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogException.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagName.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Json.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Build.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/AssemblyInfo.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagCatalogTestData.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagCatalogLoadingTests.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagCatalogValidationTests.cs`
- Generate by Unity: matching runtime `.meta` files.

**Interfaces:**
- Produces: `TagCatalog.Load(Stream utf8Json)`, `Count`, `TryGet`, `GetRequired`, `TryGetByIndex`, `GetRequiredByIndex`, `GetDisplayName`, `GetParent`, `IsAncestorOrSelf`.
- Produces: `TagCatalogException.JsonPath`, `LineNumber`, `LinePosition`.
- Consumes: Task 1 `GameplayTag(ushort)` and exact Newtonsoft dependency.
- Contract: input stream은 현재 위치부터 EOF까지 읽고 닫지 않는다. `TryGet`은 문법상 유효하지만 미등록이면 false, 문법 오류에는 `ArgumentException`; `GetRequired` 미등록에는 `KeyNotFoundException`.
- Test seam: runtime `AssemblyInfo.cs`는 `InternalsVisibleTo("Bun3.Gameplay.Unity.Tests")`만 선언해 shared subtree-end golden을 허용한다.

- [ ] **Step 1: 공통 JSON fixture helper와 기본 로드 RED 테스트를 쓴다**

```csharp
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tests;

internal static class TagCatalogTestData
{
    internal const string CanonicalJson = """
    {
      "schemaVersion": 1,
      "tags": [
        { "name": "State.Rooted", "comment": "이동 불가" },
        { "name": "ability.movement.Jump", "comment": "점프" },
        { "name": "State.Dead.Ghost", "comment": "유령" }
      ],
      "redirects": []
    }
    """;

    internal static TagCatalog Load(string json = CanonicalJson)
    {
        using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(json));
        return TagCatalog.Load(stream);
    }
}
```

```csharp
using System;
using System.IO;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagCatalogLoadingTests
{
    [Test]
    public void Load_builds_implicit_parents_and_deterministic_preorder()
    {
        var catalog = TagCatalogTestData.Load();

        Assert.That(catalog.Count, Is.EqualTo(7));
        Assert.That(catalog.GetRequired("Ability").Index, Is.EqualTo(1));
        Assert.That(catalog.GetRequired("ABILITY.MOVEMENT").Index, Is.EqualTo(2));
        Assert.That(catalog.GetRequired("ability.movement.jump").Index, Is.EqualTo(3));
        Assert.That(catalog.GetRequired("State").Index, Is.EqualTo(4));
        Assert.That(catalog.GetRequired("state.dead").Index, Is.EqualTo(5));
        Assert.That(catalog.GetRequired("STATE.DEAD.GHOST").Index, Is.EqualTo(6));
        Assert.That(catalog.GetRequired("state.rooted").Index, Is.EqualTo(7));
    }

    [Test]
    public void Parent_and_subtree_queries_use_catalog_arrays()
    {
        var catalog = TagCatalogTestData.Load();
        var state = catalog.GetRequired("State");
        var dead = catalog.GetRequired("State.Dead");
        var ghost = catalog.GetRequired("State.Dead.Ghost");

        Assert.That(catalog.GetParent(ghost), Is.EqualTo(dead));
        Assert.That(catalog.GetParent(state), Is.EqualTo(GameplayTag.None));
        Assert.That(catalog.IsAncestorOrSelf(state, ghost), Is.True);
        Assert.That(catalog.IsAncestorOrSelf(ghost, state), Is.False);
        Assert.That(catalog.IsAncestorOrSelf(GameplayTag.None, ghost), Is.False);
    }

    [Test]
    public void Wire_index_is_restored_only_through_catalog_range_check()
    {
        var catalog = TagCatalogTestData.Load();

        Assert.That(catalog.TryGetByIndex(0, out var none), Is.True);
        Assert.That(none, Is.EqualTo(GameplayTag.None));
        Assert.That(catalog.GetRequiredByIndex(0), Is.EqualTo(GameplayTag.None));
        Assert.That(catalog.TryGetByIndex(7, out var last), Is.True);
        Assert.That(last.Index, Is.EqualTo(7));
        Assert.That(catalog.TryGetByIndex(8, out _), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => catalog.GetRequiredByIndex(8));
    }

    [Test]
    public void Load_leaves_the_input_stream_open()
    {
        var prefix = System.Text.Encoding.UTF8.GetBytes("ignored-prefix");
        var json = System.Text.Encoding.UTF8.GetBytes(TagCatalogTestData.CanonicalJson);
        using var stream = new MemoryStream(prefix.Length + json.Length);
        stream.Write(prefix, 0, prefix.Length);
        stream.Write(json, 0, json.Length);
        stream.Position = prefix.Length;
        Assert.That(TagCatalog.Load(stream).Count, Is.EqualTo(7));
        Assert.That(stream.CanRead, Is.True);
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [Test]
    public void Unregistered_and_malformed_lookups_have_distinct_contracts()
    {
        var catalog = TagCatalogTestData.Load();
        Assert.That(catalog.TryGet("State.Missing", out var missing), Is.False);
        Assert.That(missing, Is.EqualTo(GameplayTag.None));
        var required = Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
            () => catalog.GetRequired("State.Missing"));
        Assert.That(required!.Message, Does.Contain("State.Missing"));
        Assert.Throws<ArgumentException>(() => catalog.TryGet("State_Bad", out _));
        Assert.Throws<ArgumentException>(() => catalog.GetRequired("State_Bad"));
    }

    [Test]
    public void Frozen_catalog_supports_concurrent_reads()
    {
        var catalog = TagCatalogTestData.Load();
        var failures = 0;
        System.Threading.Tasks.Parallel.For(0, 10_000, i =>
        {
            if (!catalog.TryGet((i & 1) == 0 ? "STATE.DEAD" : "ability.movement.jump", out var tag)
                || !tag.IsValid
                || catalog.GetDisplayName(tag).Length == 0)
                System.Threading.Interlocked.Increment(ref failures);
        });
        Assert.That(failures, Is.Zero);
    }
}
```

- [ ] **Step 2: 기본 로드 테스트가 `TagCatalog` 부재로 RED인지 확인한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~TagCatalogLoadingTests"
```

Expected: `TagCatalog`/`TagCatalogException`을 찾지 못해 컴파일 실패.

- [ ] **Step 3: 문법·schema·경계 validation RED 행렬을 추가한다**

`TagCatalogValidationTests.cs`에 다음 계약을 각각 독립 테스트로 둔다.

```csharp
[TestCase("", false)]
[TestCase("State", true)]
[TestCase("State.123", true)]
[TestCase("State.01", true)]
[TestCase("A0.B9", true)]
[TestCase(".State", false)]
[TestCase("State.", false)]
[TestCase("State..Dead", false)]
[TestCase("State_Dead", false)]
[TestCase("State-Dead", false)]
[TestCase("상태.Dead", false)]
[TestCase("State Dead", false)]
public void Name_grammar_is_ascii_alphanumeric_segments_only(string name, bool valid)
{
    var json = $$"""{ "schemaVersion": 1, "tags": [{ "name": "{{name}}" }] }""";
    if (valid)
        Assert.DoesNotThrow(() => TagCatalogTestData.Load(json));
    else
        Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
}

[Test]
public void Numeric_text_is_not_normalized()
{
    var catalog = TagCatalogTestData.Load(
        """{ "schemaVersion": 1, "tags": [{"name":"State.01"},{"name":"State.1"}] }""");
    Assert.That(catalog.GetRequired("State.01"), Is.Not.EqualTo(catalog.GetRequired("State.1")));
}

[Test]
public void Display_case_is_preserved_and_implicit_parent_case_is_order_independent()
{
    var first = TagCatalogTestData.Load(
        """{"schemaVersion":1,"tags":[{"name":"state.Dead.Ghost"},{"name":"State.Alive"}]}""");
    var reversed = TagCatalogTestData.Load(
        """{"schemaVersion":1,"tags":[{"name":"State.Alive"},{"name":"state.Dead.Ghost"}]}""");

    Assert.That(first.GetDisplayName(first.GetRequired("state.dead.ghost")),
        Is.EqualTo("state.Dead.Ghost"));
    Assert.That(first.GetDisplayName(first.GetRequired("state")), Is.EqualTo("State"));
    Assert.That(first.GetDisplayName(first.GetRequired("state.dead")), Is.EqualTo("state.Dead"));
    Assert.That(first.GetDisplayName(first.GetRequired("state")),
        Is.EqualTo(reversed.GetDisplayName(reversed.GetRequired("STATE"))));
}

[TestCase("{ \"schemaVersion\": 2, \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": 1 }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [], \"unknown\": true }")]
[TestCase("{ \"schemaVersion\": 1, \"schemaVersion\": 1, \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\",\"name\":\"B\"}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [], }")]
[TestCase("{ 'schemaVersion': 1, 'tags': [] }")]
[TestCase("{ schemaVersion: 1, tags: [] }")]
[TestCase("/*comment*/ { \"schemaVersion\": 1, \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [] } true")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [] } { \"schemaVersion\": 1, \"tags\": [] }")]
[TestCase("[]")]
[TestCase("null")]
[TestCase("{ \"schemaVersion\": \"1\", \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": 1.0, \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": null, \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": 01, \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": 0x1, \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": NaN, \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": Infinity, \"tags\": [] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": null }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": {} }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [null] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [[]] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":1}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\",\"comment\":1}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\",\"extra\":true}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [], \"redirects\": null }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [], \"redirects\": {} }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [], \"redirects\": [null] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"to\":\"A\"}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":\"Old\"}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":1,\"to\":\"A\"}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":\"Old\",\"to\":1}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":\"Old\",\"to\":\"A\",\"extra\":1}] }")]
[TestCase("{ \"schemaVersion\": 1, \"tags\": [{\"name\":\"A\"}], \"redirects\": [{\"from\":\"Old_Tag\",\"to\":\"A\"}] }")]
public void Schema_is_strict(string json)
{
    var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
    Assert.That(error!.LineNumber, Is.GreaterThanOrEqualTo(1));
}

[Test]
public void Semantic_name_error_preserves_json_path_and_source_location()
{
    const string json = "{\n  \"schemaVersion\": 1,\n  \"tags\": [\n" +
        "    { \"name\": \"State_Dead\" }\n  ]\n}";
    var error = Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
    Assert.That(error!.JsonPath, Is.EqualTo("tags[0].name"));
    Assert.That(error.LineNumber, Is.EqualTo(4));
    Assert.That(error.LinePosition, Is.GreaterThan(0));
}

[Test]
public void Case_only_duplicate_is_rejected_instead_of_merged()
{
    const string json =
        """{ "schemaVersion": 1, "tags": [{"name":"State.Dead"},{"name":"state.dead"}] }""";
    Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
}
```

경계 테스트는 helper로 정확히 255/256자와 16/17 세그먼트를 만든다. 노드 상한은 explicit leaf 32,767개가 각각 고유 implicit parent를 만들고 별도 root 하나를 더한 65,535 성공 입력과, leaf 32,768개가 65,536 active node를 만드는 실패 입력으로 검증해 암시 부모도 상한에 포함됨을 직접 증명한다. `BuildFlatCatalog`는 이후 64/65종 컨테이너 fixture에 사용한다.

```csharp
[Test]
public void Active_node_limit_includes_implicit_parents()
{
    var maximum = TagCatalogTestData.Load(
        TagCatalogTestData.BuildTwoLevelCatalog(32_767, includeExtraRoot: true));
    Assert.That(maximum.Count, Is.EqualTo(65_535));
    Assert.That(maximum.GetRequiredByIndex(65_535).Index, Is.EqualTo(65_535));
    Assert.Throws<TagCatalogException>(
        () => TagCatalogTestData.Load(
            TagCatalogTestData.BuildTwoLevelCatalog(32_768, includeExtraRoot: false)));
}
```

`TagCatalogTestData`에 경계 fixture helper를 실제로 추가한다.

```csharp
internal static string BuildFlatCatalog(int count)
{
    var json = new System.Text.StringBuilder(count * 20);
    json.Append("{\"schemaVersion\":1,\"tags\":[");
    for (var i = 0; i < count; i++)
    {
        if (i != 0) json.Append(',');
        json.Append("{\"name\":\"T").Append(i).Append("\"}");
    }
    return json.Append("]}").ToString();
}

internal static string BuildTwoLevelCatalog(int chainCount, bool includeExtraRoot)
{
    var json = new System.Text.StringBuilder(chainCount * 26);
    json.Append("{\"schemaVersion\":1,\"tags\":[");
    for (var i = 0; i < chainCount; i++)
    {
        if (i != 0) json.Append(',');
        json.Append("{\"name\":\"P").Append(i).Append(".Leaf\"}");
    }
    if (includeExtraRoot)
    {
        if (chainCount != 0) json.Append(',');
        json.Append("{\"name\":\"Extra\"}");
    }
    return json.Append("]}").ToString();
}

internal static string ChainLeaf(int chain, int depth)
{
    var path = new System.Text.StringBuilder().Append('B').Append(chain);
    for (var level = 1; level < depth; level++)
        path.Append(".L").Append(level);
    return path.ToString();
}

internal static string BuildChainCatalog(int exactKinds, int depth, bool includeMissRoot)
{
    var json = new System.Text.StringBuilder().Append("{\"schemaVersion\":1,\"tags\":[");
    for (var i = 0; i < exactKinds; i++)
    {
        if (i != 0) json.Append(',');
        json.Append("{\"name\":\"").Append(ChainLeaf(i, depth)).Append("\"}");
    }
    if (includeMissRoot)
    {
        if (exactKinds != 0) json.Append(',');
        json.Append("{\"name\":\"ZMiss\"}");
    }
    return json.Append("]}").ToString();
}

internal static string BuildPath(int length) => new string('A', length);

internal static string BuildDepth(int depth) =>
    string.Join(".", System.Linq.Enumerable.Repeat("A", depth));
```

그리고 `BuildPath(255)` 성공/`BuildPath(256)` 실패, `BuildDepth(16)` 성공/`BuildDepth(17)` 실패를 각각 assertion한다. 65,535 성공 catalog는 `Count == 65_535`와 `GetRequiredByIndex(65_535).Index == 65_535`도 확인한다.

- [ ] **Step 4: 이름 검증과 공개 예외를 구현한다**

`TagName`은 문화권 API와 regex 객체를 쓰지 않고 한 번의 문자 순회로 검증·fold한다.

```csharp
internal static class TagName
{
    internal const int MaximumLength = 255;
    internal const int MaximumDepth = 16;

    internal static string ValidateAndFold(
        string value,
        string jsonPath,
        int lineNumber,
        int linePosition)
    {
        if (value is null)
            throw new TagCatalogException(
                "태그 이름이 없습니다.", jsonPath, lineNumber, linePosition);
        if (value.Length == 0 || value.Length > MaximumLength)
            throw new TagCatalogException(
                "태그 경로 길이는 1..255여야 합니다.", jsonPath, lineNumber, linePosition);

        var chars = value.ToCharArray();
        var depth = 1;
        var segmentLength = 0;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c == '.')
            {
                if (segmentLength == 0 || ++depth > MaximumDepth)
                    throw new TagCatalogException(
                        "빈 세그먼트 또는 16단계 초과입니다.", jsonPath, lineNumber, linePosition);
                segmentLength = 0;
                continue;
            }

            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                throw new TagCatalogException(
                    "ASCII 영숫자 이외의 문자는 사용할 수 없습니다.",
                    jsonPath,
                    lineNumber,
                    linePosition);
            if (c >= 'A' && c <= 'Z')
                chars[i] = (char)(c + ('a' - 'A'));
            segmentLength++;
        }

        if (segmentLength == 0)
            throw new TagCatalogException(
                "태그는 점으로 끝날 수 없습니다.", jsonPath, lineNumber, linePosition);
        return new string(chars);
    }
}
```

`TagCatalogException`은 `FormatException`을 상속하고 생성자에서 `JsonPath`, `LineNumber`, `LinePosition`을 고정한다. loader는 각 `JValue`의 `Path`와 `IJsonLineInfo`를 위 네 인자에 전달한다. Newtonsoft의 `JsonReaderException`과 semantic validation 오류를 모두 이 타입으로 감싸며, public `TryGet`/`GetRequired`의 입력 문법 검사는 별도 `TagName.TryFold`를 사용해 `ArgumentException` 계약으로 변환한다. `TryFold`는 같은 ASCII 순회 규칙을 공유하되 예외를 만들지 않고 false를 반환하며, public lookup은 false일 때 요청 path를 `ArgumentException.ParamName == "path"`로 거부한다.

- [ ] **Step 5: strict parse와 deterministic build를 구현한다**

`TagCatalog.Load`의 공개 골격을 다음과 같이 고정한다.

```csharp
public static TagCatalog Load(Stream utf8Json)
{
    if (utf8Json is null) throw new ArgumentNullException(nameof(utf8Json));
    if (!utf8Json.CanRead) throw new ArgumentException("읽을 수 있는 스트림이 필요합니다.", nameof(utf8Json));
    return Loader.Load(utf8Json);
}
```

`Loader.Load`는 아래 순서를 그대로 수행한다.

1. `new StreamReader(utf8Json, new UTF8Encoding(false, true), false, 1024, true)`를 사용해 현재 위치부터 text를 한 번 `ReadToEnd`하고 input은 열어 둔다.
2. text를 작은 내부 `StrictJsonSyntax.Validate`에 먼저 통과시킨다. 이 scanner는 JSON string escape/line-column state를 추적하고 RFC JSON의 double-quoted string, `-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?` number, `true`/`false`/`null`, object/array 구분자만 허용해 comment, single quote, unquoted property, leading-zero/hex/NaN/Infinity number, trailing comma와 root 뒤 trailing token을 모두 `TagCatalogException`으로 거부한다. 독자 semantic parser가 되지 않도록 token value는 해석하지 않고 lexical/구분자 stack만 검증한다.
3. `new StringReader(text)` 위에 `JsonTextReader`를 만들고 `DateParseHandling.None`, `FloatParseHandling.Decimal`, `MaxDepth = 8`을 설정한다.
4. `JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = Error, LineInfoHandling = Load })`로 root와 중복 property를 읽은 뒤 `reader.Read()`가 false인지 확인해 EOF를 다시 강제한다.
5. root 허용 필드는 `schemaVersion`, `tags`, `redirects`; tag 허용 필드는 `name`, `comment`; redirect 허용 필드는 `from`, `to`뿐인지 집합 비교한다.
6. token type을 검사해 `schemaVersion == 1`, `tags` array, optional `redirects` array, 문자열 필드를 읽는다. `comment` 누락은 빈 문자열로 둔다.
7. 모든 explicit 경로를 fold하고 대소문자 무시 중복을 거부한다. 각 경로의 점 prefix를 추가해 implicit 부모를 만든다.
8. canonical path를 parent/leaf로 분리하고 각 sibling을 `StringComparer.Ordinal`로 정렬한 tree를 만든다.
9. root부터 preorder로 순회하며 `1..N`을 부여하고 `displayNames`, `canonicalNames`, `parents`, `subtreeEnds` 배열을 채운다. 명시 노드는 자기 casing, 암시 노드는 canonical 순으로 첫 explicit descendant의 세그먼트 casing을 쓴다.
10. active lookup은 `Dictionary<string, ushort>(StringComparer.Ordinal)`로 만들고 완성 뒤 외부에 노출하지 않는다.

`StrictJsonSyntax`는 `Validate`, `ParseValue`, `ParseObject`, `ParseArray`, `ParseString`, `ParseNumber`, `ConsumeLiteral`, `SkipWhitespace`로만 구성한다. object state는 첫 member 전 `}`만 빈 object로 허용하고, member 뒤에는 `,` 다음 반드시 double-quoted property 또는 `}`만 허용한다. array도 첫 value 전 `]`만 빈 array로 허용하고 comma 뒤 `]`를 거부한다. string은 unescaped U+0000..U+001F를 거부하고 `\" \\ \/ \b \f \n \r \t \uXXXX`만 escape로 허용한다. number는 위 grammar를 문자 단위로 소비하며 literal은 정확한 소문자 bytes만 받는다. root 하나 뒤 whitespace를 건너뛴 cursor가 text length와 다르면 실패한다. 모든 실패는 scanner가 유지한 1-based line/position을 `TagCatalogException`에 넣는다.

공개 조회는 다음 시그니처를 정확히 제공한다.

```csharp
public int Count { get; }
public bool TryGet(string path, out GameplayTag tag);
public GameplayTag GetRequired(string path);
public bool TryGetByIndex(ushort index, out GameplayTag tag);
public GameplayTag GetRequiredByIndex(ushort index);
public string GetDisplayName(GameplayTag tag);
public GameplayTag GetParent(GameplayTag tag);
public bool IsAncestorOrSelf(GameplayTag ancestor, GameplayTag tag);
internal ushort GetSubtreeEnd(GameplayTag tag);
```

`IsAncestorOrSelf`는 `ancestor.Index <= tag.Index && tag.Index <= _subtreeEnds[ancestor.Index]`만 수행한다. `None`이 들어오면 false다.

Unity에서 같은 source conformance test가 internal subtree-end 배열까지 직접 검증할 수 있도록 runtime `AssemblyInfo.cs`는 다음 한 friend assembly만 연다.

```csharp
#nullable enable
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Bun3.Gameplay.Unity.Tests")]
```

- [ ] **Step 6: catalog 로드/validation 테스트를 GREEN으로 만든다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~TagCatalogLoadingTests|FullyQualifiedName~TagCatalogValidationTests"
if ($LASTEXITCODE -ne 0) { throw 'TagCatalog load/validation tests failed.' }
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
```

Expected: 모든 새 테스트 PASS. 65,536개 입력은 `TagCatalogException`, 65,535개는 마지막 index 65,535를 가진다. Unity import가 새 runtime `.meta`를 만들고 EditMode warning CS 0이다.

- [ ] **Step 7: catalog loader를 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogException.cs* `
  common/src/com.bun3.gameplay/Runtime/Tags/TagName.cs* `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs* `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Json.cs* `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Build.cs* `
  common/src/com.bun3.gameplay/Runtime/AssemblyInfo.cs* `
  common/tests/Bun3.Gameplay.Tests/TagCatalogTestData.cs `
  common/tests/Bun3.Gameplay.Tests/TagCatalogLoadingTests.cs `
  common/tests/Bun3.Gameplay.Tests/TagCatalogValidationTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 2 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "✨ 불변 GameplayTag 카탈로그 로더 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 2 failed.' }
```

### Task 3: redirect와 canonical SHA-256 fingerprint를 고정한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Fingerprint.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Json.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagCatalogRedirectTests.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagCatalogFingerprintTests.cs`
- Generate by Unity: `TagCatalog.Fingerprint.cs.meta`.

**Interfaces:**
- Produces: `ReadOnlySpan<byte> TagCatalog.Fingerprint`, `bool MatchesFingerprint(ReadOnlySpan<byte> other)`.
- Extends: `TryGet`/`GetRequired` resolve active name first, then one direct redirect.
- Contract: redirect source는 active와 겹치지 않고 target은 active여야 하며 duplicate/chain/cycle을 거부한다.

- [ ] **Step 1: redirect validation과 lookup RED 테스트를 쓴다**

```csharp
[Test]
public void Redirect_resolves_old_path_case_insensitively()
{
    var catalog = TagCatalogTestData.Load(
        """
        {
          "schemaVersion": 1,
          "tags": [{"name":"State.Dead"}],
          "redirects": [{"from":"State.Killed","to":"state.dead"}]
        }
        """);

    Assert.That(catalog.GetRequired("STATE.KILLED"), Is.EqualTo(catalog.GetRequired("State.Dead")));
}

[Test]
public void Redirect_can_target_an_implicit_parent()
{
    var catalog = TagCatalogTestData.Load(
        """{"schemaVersion":1,"tags":[{"name":"A.B"}],"redirects":[{"from":"Old","to":"A"}]}""");
    Assert.That(catalog.GetRequired("Old"), Is.EqualTo(catalog.GetRequired("A")));
}

[TestCase("""{"schemaVersion":1,"tags":[{"name":"A"}],"redirects":[{"from":"A","to":"A"}]}""")]
[TestCase("""{"schemaVersion":1,"tags":[{"name":"A.B"}],"redirects":[{"from":"A","to":"A.B"}]}""")]
[TestCase("""{"schemaVersion":1,"tags":[{"name":"A"}],"redirects":[{"from":"Old","to":"Missing"}]}""")]
[TestCase("""{"schemaVersion":1,"tags":[{"name":"A"}],"redirects":[{"from":"Old","to":"A"},{"from":"old","to":"A"}]}""")]
[TestCase("""{"schemaVersion":1,"tags":[{"name":"A"}],"redirects":[{"from":"Old1","to":"Old2"},{"from":"Old2","to":"A"}]}""")]
public void Invalid_redirect_graph_is_rejected(string json)
{
    Assert.Throws<TagCatalogException>(() => TagCatalogTestData.Load(json));
}
```

- [ ] **Step 2: exact fingerprint golden과 semantic invariance RED 테스트를 쓴다**

다음 semantic catalog의 canonical byte stream은 161 bytes이고 SHA-256은
`feef3116e20f93b5383d8061ffc20ff189fc939a3291d6fa9f09ff3d16ff5f0e`다.

```csharp
[Test]
public void Fingerprint_matches_BTAG_big_endian_golden()
{
    var catalog = TagCatalogTestData.Load(
        """
        {
          "schemaVersion": 1,
          "tags": [
            {"name":"State.Rooted"},
            {"name":"Ability.Movement.Jump"},
            {"name":"State.Dead.Ghost"}
          ],
          "redirects": [{"from":"State.Killed","to":"State.Dead"}]
        }
        """);

    Assert.That(ToHex(catalog.Fingerprint),
        Is.EqualTo("feef3116e20f93b5383d8061ffc20ff189fc939a3291d6fa9f09ff3d16ff5f0e"));
}

[Test]
public void Formatting_order_comments_and_display_case_do_not_change_identity()
{
    var left = TagCatalogTestData.Load(
        """{"schemaVersion":1,"tags":[{"name":"State.Dead","comment":"왼쪽"},{"name":"Ability.Jump"}]}""");
    var right = TagCatalogTestData.Load(
        """
        { "tags": [{"name":"ability.jump"},{"comment":"오른쪽","name":"state.dead"}],
          "schemaVersion": 1, "redirects": [] }
        """);

    Assert.That(right.Fingerprint.ToArray(), Is.EqualTo(left.Fingerprint.ToArray()));
    Assert.That(right.GetRequired("STATE.DEAD").Index, Is.EqualTo(left.GetRequired("state.dead").Index));
}

[Test]
public void Semantic_path_parent_or_redirect_change_changes_fingerprint()
{
    var baseline = TagCatalogTestData.Load("""{"schemaVersion":1,"tags":[{"name":"State.Dead"}]}""");
    var renamed = TagCatalogTestData.Load("""{"schemaVersion":1,"tags":[{"name":"State.Gone"}]}""");
    var redirected = TagCatalogTestData.Load(
        """{"schemaVersion":1,"tags":[{"name":"State.Dead"}],"redirects":[{"from":"State.Killed","to":"State.Dead"}]}""");
    Assert.That(renamed.Fingerprint.ToArray(), Is.Not.EqualTo(baseline.Fingerprint.ToArray()));
    Assert.That(redirected.Fingerprint.ToArray(), Is.Not.EqualTo(baseline.Fingerprint.ToArray()));
    Assert.That(baseline.MatchesFingerprint(baseline.Fingerprint), Is.True);
    Assert.That(baseline.MatchesFingerprint(renamed.Fingerprint), Is.False);
}

[Test]
public void Implicit_parent_and_redirect_row_order_do_not_change_fingerprint()
{
    var implicitParent = TagCatalogTestData.Load(
        """{"schemaVersion":1,"tags":[{"name":"State.Dead"}],"redirects":[{"from":"Old2","to":"State.Dead"},{"from":"Old1","to":"State"}]}""");
    var explicitParent = TagCatalogTestData.Load(
        """{"schemaVersion":1,"tags":[{"name":"State"},{"name":"State.Dead"}],"redirects":[{"from":"Old1","to":"State"},{"from":"Old2","to":"State.Dead"}]}""");
    Assert.That(explicitParent.Fingerprint.ToArray(),
        Is.EqualTo(implicitParent.Fingerprint.ToArray()));
}

private static string ToHex(ReadOnlySpan<byte> bytes)
{
    const string digits = "0123456789abcdef";
    var chars = new char[bytes.Length * 2];
    for (var i = 0; i < bytes.Length; i++)
    {
        chars[i * 2] = digits[bytes[i] >> 4];
        chars[i * 2 + 1] = digits[bytes[i] & 0x0F];
    }
    return new string(chars);
}
```

- [ ] **Step 3: direct redirect table과 fingerprint writer를 구현한다**

redirect validation 후 `_redirects: Dictionary<string, ushort>`에는 canonical source에서 최종 active target index로 가는 값만 저장한다. fingerprint writer는 `ArrayBufferWriter<byte>` 대신 작은 내부 growable `byte[]` writer를 사용해 netstandard/Unity 의존성을 단순화하고 다음 순서를 정확히 쓴다.

```text
ASCII "BTAG"
uint32-be schemaVersion
uint32-be active node count
for each runtime index: uint32-be UTF8 byte length, canonical path bytes
uint32-be redirect count
for each redirect sorted by canonical from: from length/bytes, to length/bytes
```

핵심 public/내부 코드는 다음 형태다.

```csharp
private readonly byte[] _fingerprint;
public ReadOnlySpan<byte> Fingerprint => _fingerprint;
public bool MatchesFingerprint(ReadOnlySpan<byte> other) =>
    other.SequenceEqual(_fingerprint);

private static byte[] ComputeFingerprint(
    int schemaVersion,
    string[] canonicalNames,
    RedirectEntry[] redirects)
{
    using var sha256 = SHA256.Create();
    return sha256.ComputeHash(BuildCanonicalBytes(schemaVersion, canonicalNames, redirects));
}
```

`canonicalNames[0]`의 `None` 빈 슬롯은 active count와 path loop에서 제외한다. 정수는 `BinaryPrimitives.WriteUInt32BigEndian`, 문자열은 `new UTF8Encoding(false, true)`를 사용한다.

- [ ] **Step 4: redirect/fingerprint 테스트를 GREEN으로 만든다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~TagCatalogRedirectTests|FullyQualifiedName~TagCatalogFingerprintTests"
if ($LASTEXITCODE -ne 0) { throw 'TagCatalog redirect/fingerprint tests failed.' }
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
```

Expected: redirect와 fingerprint 테스트 전체 PASS, golden hash 정확히 일치, 새 runtime meta 존재, Unity EditMode warning CS 0.

- [ ] **Step 5: redirect/fingerprint를 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Json.cs `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Fingerprint.cs* `
  common/tests/Bun3.Gameplay.Tests/TagCatalogRedirectTests.cs `
  common/tests/Bun3.Gameplay.Tests/TagCatalogFingerprintTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 3 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "✨ GameplayTag redirect와 fingerprint 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 3 failed.' }
```

### Task 4: 정렬 배열 기반 `TagContainer`를 구현한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagSearch.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagContainer.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagContainerTests.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagPerformanceContractTests.cs`
- Generate by Unity: matching runtime `.meta` files.

**Interfaces:**
- Produces: `TagCatalog.CreateContainer(int expectedExactKinds = 0)`.
- Produces: `TagContainer.ExactKindCount`, `Add`, `Remove`, `Has`, `HasExact`, `HasAny`, `HasAll`, `HasAnyExact`, `HasAllExact`.
- Produces internal: `TagSearch.LowerBound(ushort[] indices, int count, ushort target, out int comparisons)` and the public-query-equivalent `TagContainer.HasCore(GameplayTag, bool exact, out int comparisons)`.
- Contract: container-to-container query는 같은 `TagCatalog` reference만 허용한다. `None` 단일 query는 false, mutation은 `ArgumentException`.

- [ ] **Step 1: 일반 집합 의미의 RED 테스트를 쓴다**

```csharp
using System;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagContainerTests
{
    private TagCatalog _catalog = null!;
    private GameplayTag _state;
    private GameplayTag _dead;
    private GameplayTag _ghost;
    private GameplayTag _rooted;

    [SetUp]
    public void SetUp()
    {
        _catalog = TagCatalogTestData.Load();
        _state = _catalog.GetRequired("State");
        _dead = _catalog.GetRequired("State.Dead");
        _ghost = _catalog.GetRequired("State.Dead.Ghost");
        _rooted = _catalog.GetRequired("State.Rooted");
    }

    [Test]
    public void Add_is_unique_and_remove_affects_only_explicit_tag()
    {
        var tags = _catalog.CreateContainer();
        Assert.That(tags.Add(_ghost), Is.True);
        Assert.That(tags.Add(_ghost), Is.False);
        Assert.That(tags.ExactKindCount, Is.EqualTo(1));
        Assert.That(tags.HasExact(_ghost), Is.True);
        Assert.That(tags.HasExact(_dead), Is.False);
        Assert.That(tags.Remove(_dead), Is.False);
        Assert.That(tags.Remove(_ghost), Is.True);
    }

    [Test]
    public void Hierarchy_matches_from_owned_child_to_queried_parent_only()
    {
        var tags = _catalog.CreateContainer();
        tags.Add(_ghost);

        Assert.That(tags.Has(_state), Is.True);
        Assert.That(tags.Has(_dead), Is.True);
        Assert.That(tags.Has(_ghost), Is.True);
        Assert.That(tags.Has(_rooted), Is.False);

        var parentOnly = _catalog.CreateContainer();
        parentOnly.Add(_state);
        Assert.That(parentOnly.Has(_ghost), Is.False);
    }

    [Test]
    public void Any_all_and_exact_variants_have_explicit_empty_query_semantics()
    {
        var owned = _catalog.CreateContainer();
        owned.Add(_ghost);
        owned.Add(_rooted);

        var query = _catalog.CreateContainer();
        query.Add(_dead);
        query.Add(_rooted);
        Assert.That(owned.HasAny(query), Is.True);
        Assert.That(owned.HasAll(query), Is.True);
        Assert.That(owned.HasAnyExact(query), Is.True);
        Assert.That(owned.HasAllExact(query), Is.False);

        var empty = _catalog.CreateContainer();
        Assert.That(owned.HasAny(empty), Is.False);
        Assert.That(owned.HasAll(empty), Is.True);
        Assert.That(owned.HasAnyExact(empty), Is.False);
        Assert.That(owned.HasAllExact(empty), Is.True);
    }

    [Test]
    public void None_cross_catalog_and_capacity_fail_atomically()
    {
        Assert.DoesNotThrow(() => _catalog.CreateContainer(0));
        Assert.DoesNotThrow(() => _catalog.CreateContainer(64));
        Assert.Throws<ArgumentOutOfRangeException>(() => _catalog.CreateContainer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _catalog.CreateContainer(65));

        var owned = _catalog.CreateContainer(64);
        Assert.That(owned.Has(GameplayTag.None), Is.False);
        Assert.That(owned.HasExact(GameplayTag.None), Is.False);
        Assert.Throws<ArgumentException>(() => owned.Add(GameplayTag.None));
        Assert.Throws<ArgumentException>(() => owned.Remove(GameplayTag.None));

        var other = TagCatalogTestData.Load().CreateContainer();
        Assert.Throws<ArgumentException>(() => owned.HasAny(other));

        var flat = TagCatalogTestData.Load(TagCatalogTestData.BuildFlatCatalog(65));
        var full = flat.CreateContainer(64);
        for (ushort i = 1; i <= 64; i++)
            Assert.That(full.Add(flat.GetRequiredByIndex(i)), Is.True);
        Assert.Throws<InvalidOperationException>(() => full.Add(flat.GetRequiredByIndex(65)));
        Assert.That(full.ExactKindCount, Is.EqualTo(64));
        Assert.That(full.HasExact(flat.GetRequiredByIndex(65)), Is.False);
    }
}
```

`BuildFlatCatalog`은 Task 2의 test helper를 `internal`로 재사용한다.

- [ ] **Step 2: `TagContainer` 부재로 RED인지 확인한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~TagContainerTests"
```

Expected: `CreateContainer`/`TagContainer` 컴파일 실패.

- [ ] **Step 3: comparison 상한을 직접 관측하는 `TagSearch` 테스트를 추가한다**

`TagPerformanceContractTests.cs`에 모든 길이와 target을 순회한다.

```csharp
[Test]
public void Lower_bound_never_exceeds_seven_or_eleven_index_comparisons()
{
    AssertComparisonBound(64, 7);
    AssertComparisonBound(1_024, 11);
}

[Test]
public void Actual_tag_container_queries_use_one_bounded_search()
{
    var catalog = TagCatalogTestData.Load(TagCatalogTestData.BuildFlatCatalog(65));
    var container = catalog.CreateContainer(64);
    for (ushort i = 1; i <= 64; i++)
        container.Add(catalog.GetRequiredByIndex(i));

    for (ushort i = 1; i <= 65; i++)
    {
        _ = container.HasCore(catalog.GetRequiredByIndex(i), exact: false, out var hierarchical);
        _ = container.HasCore(catalog.GetRequiredByIndex(i), exact: true, out var exact);
        Assert.That(hierarchical, Is.LessThanOrEqualTo(7));
        Assert.That(exact, Is.LessThanOrEqualTo(7));
    }
}

private static void AssertComparisonBound(int length, int expectedMaximum)
{
    var values = new ushort[length];
    for (var i = 0; i < length; i++)
        values[i] = checked((ushort)(i * 2 + 1));

    for (var target = 0; target <= values[length - 1] + 1; target++)
    {
        _ = TagSearch.LowerBound(values, length, checked((ushort)target), out var comparisons);
        Assert.That(comparisons, Is.LessThanOrEqualTo(expectedMaximum), $"length={length}, target={target}");
    }
}
```

- [ ] **Step 4: lower-bound와 compact sorted set을 구현한다**

```csharp
internal static int LowerBound(
    ushort[] indices,
    int count,
    ushort target,
    out int comparisons)
{
    var low = 0;
    var high = count;
    comparisons = 0;
    while (low < high)
    {
        var middle = low + ((high - low) >> 1);
        comparisons++;
        if (indices[middle] < target)
            low = middle + 1;
        else
            high = middle;
    }
    return low;
}
```

`TagContainer`는 `_catalog`, `_indices`, `_count`만 보유한다. `expectedExactKinds`는 0..64인지 검사하고 0이면 `Array.Empty<ushort>()`, 양수면 요청 크기로 정확히 예약한다. 빈 배열의 첫 `Add`에서 capacity 4를 확보하고 이후 최대 64까지만 2배 확장한다. `Add`는 lower-bound 위치에서 중복을 확인한 뒤 `Array.Copy` 한 번으로 뒤를 밀고, `Remove`는 뒤를 한 번 당긴 뒤 마지막 슬롯을 0으로 지운다.

`HasCore(tag, exact, out comparisons)`는 lower-bound를 정확히 한 번 부른다. exact이면 후보 index equality, 계층이면 후보가 `catalog.GetSubtreeEnd(tag)` 이하인지 판정한다. public `Has`/`HasExact`는 같은 core를 호출하고 comparison count만 버린다. `HasAny`/`HasAll`은 query의 exact 배열을 순회해 각각 public `Has`를 부르므로 Q개 query의 총 상한은 7Q다. Exact 변형은 `HasExact`를 부른다. query의 `_catalog`가 `ReferenceEquals`가 아니면 `ArgumentException`을 던진다.

factory와 생성자는 다음 경계를 지킨다.

```csharp
public TagContainer CreateContainer(int expectedExactKinds = 0) =>
    new TagContainer(this, expectedExactKinds);

internal TagContainer(TagCatalog catalog, int expectedExactKinds)
{
    if ((uint)expectedExactKinds > 64u)
        throw new ArgumentOutOfRangeException(nameof(expectedExactKinds));
    _catalog = catalog;
    _indices = expectedExactKinds == 0
        ? Array.Empty<ushort>()
        : new ushort[expectedExactKinds];
}
```

public constructor는 제공하지 않아 항상 카탈로그에 결합되게 한다.

- [ ] **Step 5: 일반 집합과 구조적 상한을 GREEN으로 만든다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~TagContainerTests|FullyQualifiedName~TagPerformanceContractTests"
if ($LASTEXITCODE -ne 0) { throw 'TagContainer tests failed.' }
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
```

Expected: 전체 PASS, 실제 `TagContainer` query가 7회 이하, lower-bound 길이 64/1,024가 각각 7/11회 이하, 새 runtime meta 존재, Unity warning CS 0.

- [ ] **Step 6: `TagContainer`를 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay/Runtime/Tags/TagSearch.cs* `
  common/src/com.bun3.gameplay/Runtime/Tags/TagContainer.cs* `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs `
  common/tests/Bun3.Gameplay.Tests/TagContainerTests.cs `
  common/tests/Bun3.Gameplay.Tests/TagPerformanceContractTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 4 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "✨ 일정 비용의 TagContainer 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 4 failed.' }
```

### Task 5: 조상 누적 정렬 entry 기반 `TagCountContainer`를 구현한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCountContainer.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagCountContainerTests.cs`
- Generate by Unity: `TagCountContainer.cs.meta`.
- Modify: `common/tests/Bun3.Gameplay.Tests/TagPerformanceContractTests.cs`

**Interfaces:**
- Produces: `TagCatalog.CreateCountContainer(int expectedExactKinds = 0)`.
- Produces: `ExactKindCount`, `Add(GameplayTag, int = 1)`, `int Remove(GameplayTag, int = 1)`, `ExactCount`, `Count`, `HasExact`, `Has`, Any/All과 Exact 변형.
- Produces internal: public count queries와 동일한 `GetCountsCore(GameplayTag, out int exact, out int aggregate, out int comparisons)`.
- Contract: exact/aggregate는 양의 `int`; 종류 64, depth 16, entry 1,024 상한; 모든 실패는 원자적.

- [ ] **Step 1: exact/aggregate/제거 의미의 RED 테스트를 쓴다**

```csharp
using System;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TagCountContainerTests
{
    private TagCatalog _catalog = null!;
    private TagCountContainer _counts = null!;
    private GameplayTag _state;
    private GameplayTag _dead;
    private GameplayTag _ghost;
    private GameplayTag _rooted;

    [SetUp]
    public void SetUp()
    {
        _catalog = TagCatalogTestData.Load();
        _counts = _catalog.CreateCountContainer(8);
        _state = _catalog.GetRequired("State");
        _dead = _catalog.GetRequired("State.Dead");
        _ghost = _catalog.GetRequired("State.Dead.Ghost");
        _rooted = _catalog.GetRequired("State.Rooted");
    }

    [Test]
    public void Multiple_sources_update_exact_and_all_ancestors()
    {
        _counts.Add(_ghost, 2);
        _counts.Add(_dead, 1);

        Assert.That(_counts.ExactKindCount, Is.EqualTo(2));
        Assert.That(_counts.ExactCount(_ghost), Is.EqualTo(2));
        Assert.That(_counts.ExactCount(_dead), Is.EqualTo(1));
        Assert.That(_counts.Count(_ghost), Is.EqualTo(2));
        Assert.That(_counts.Count(_dead), Is.EqualTo(3));
        Assert.That(_counts.Count(_state), Is.EqualTo(3));
        Assert.That(_counts.Has(_state), Is.True);
        Assert.That(_counts.HasExact(_state), Is.False);
    }

    [Test]
    public void Siblings_contribute_to_common_parent()
    {
        _counts.Add(_ghost, 2);
        _counts.Add(_rooted, 4);
        Assert.That(_counts.Count(_state), Is.EqualTo(6));
        Assert.That(_counts.Count(_dead), Is.EqualTo(2));
    }

    [Test]
    public void Remove_returns_actual_amount_and_clamps_at_zero()
    {
        _counts.Add(_ghost, 3);
        Assert.That(_counts.Remove(_ghost, 2), Is.EqualTo(2));
        Assert.That(_counts.ExactCount(_ghost), Is.EqualTo(1));
        Assert.That(_counts.Remove(_ghost, 99), Is.EqualTo(1));
        Assert.That(_counts.Remove(_ghost), Is.Zero);
        Assert.That(_counts.Count(_state), Is.Zero);
        Assert.That(_counts.ExactKindCount, Is.Zero);
    }

    [Test]
    public void None_nonpositive_and_capacity_fail_without_mutation()
    {
        Assert.DoesNotThrow(() => _catalog.CreateCountContainer(0));
        Assert.DoesNotThrow(() => _catalog.CreateCountContainer(64));
        Assert.Throws<ArgumentOutOfRangeException>(() => _catalog.CreateCountContainer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _catalog.CreateCountContainer(65));

        Assert.Throws<ArgumentException>(() => _counts.Add(GameplayTag.None));
        Assert.Throws<ArgumentException>(() => _counts.Remove(GameplayTag.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => _counts.Add(_ghost, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _counts.Add(_ghost, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _counts.Remove(_ghost, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _counts.Remove(_ghost, -1));
        Assert.That(_counts.ExactKindCount, Is.Zero);
        Assert.That(_counts.Has(GameplayTag.None), Is.False);
        Assert.That(_counts.HasExact(GameplayTag.None), Is.False);
    }

    [Test]
    public void Any_all_exact_and_empty_query_match_tag_container_semantics()
    {
        _counts.Add(_ghost);
        _counts.Add(_rooted);
        var query = _catalog.CreateContainer();
        query.Add(_dead);
        query.Add(_rooted);

        Assert.That(_counts.HasAny(query), Is.True);
        Assert.That(_counts.HasAll(query), Is.True);
        Assert.That(_counts.HasAnyExact(query), Is.True);
        Assert.That(_counts.HasAllExact(query), Is.False);

        var empty = _catalog.CreateContainer();
        Assert.That(_counts.HasAny(empty), Is.False);
        Assert.That(_counts.HasAll(empty), Is.True);
        Assert.That(_counts.HasAnyExact(empty), Is.False);
        Assert.That(_counts.HasAllExact(empty), Is.True);
        Assert.Throws<ArgumentException>(
            () => _counts.HasAny(TagCatalogTestData.Load().CreateContainer()));
    }
}
```

- [ ] **Step 2: overflow와 종류 상한의 원자성 RED 테스트를 추가한다**

```csharp
[Test]
public void Exact_overflow_keeps_every_entry_unchanged()
{
    _counts.Add(_ghost, int.MaxValue);
    Assert.Throws<OverflowException>(() => _counts.Add(_ghost));
    Assert.That(_counts.ExactCount(_ghost), Is.EqualTo(int.MaxValue));
    Assert.That(_counts.Count(_state), Is.EqualTo(int.MaxValue));
}

[Test]
public void Aggregate_overflow_keeps_sibling_and_parent_counts_unchanged()
{
    _counts.Add(_ghost, int.MaxValue);
    Assert.Throws<OverflowException>(() => _counts.Add(_rooted));
    Assert.That(_counts.ExactCount(_rooted), Is.Zero);
    Assert.That(_counts.ExactCount(_ghost), Is.EqualTo(int.MaxValue));
    Assert.That(_counts.Count(_state), Is.EqualTo(int.MaxValue));
}

[Test]
public void Sixty_fifth_exact_kind_fails_atomically()
{
    var flat = TagCatalogTestData.Load(TagCatalogTestData.BuildFlatCatalog(65));
    var counts = flat.CreateCountContainer(64);
    for (ushort i = 1; i <= 64; i++)
        counts.Add(flat.GetRequiredByIndex(i));

    Assert.Throws<InvalidOperationException>(() => counts.Add(flat.GetRequiredByIndex(65)));
    Assert.That(counts.ExactKindCount, Is.EqualTo(64));
    Assert.That(counts.ExactCount(flat.GetRequiredByIndex(65)), Is.Zero);
    Assert.That(counts.Count(flat.GetRequiredByIndex(65)), Is.Zero);
}
```

- [ ] **Step 3: `TagCountContainer` 부재로 RED인지 확인한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~TagCountContainerTests"
```

Expected: `TagCountContainer`/`CreateCountContainer` 컴파일 실패.

- [ ] **Step 4: parallel sorted arrays와 원자적 mutation을 구현한다**

저장 필드는 다음으로 제한한다.

```csharp
private readonly TagCatalog _catalog;
private ushort[] _indices;
private int[] _exactCounts;
private int[] _aggregateCounts;
private int _entryCount;
private int _exactKindCount;
internal int LastMutationPassCount { get; private set; }
internal int LastMutationDepth { get; private set; }
```

`Add`는 아래 순서를 지킨다.

1. `None`과 count를 검사한다. 단일 `GameplayTag`는 출처를 싣지 않으므로 catalog provenance 검사는 하지 않고 호출자 불변식으로 둔다.
2. tag부터 `GetParent`로 최대 16개의 조상 index를 stackalloc `Span<ushort>`에 수집한다.
3. 기존 exact 종류인지 찾고 새 종류면 64 상한을 먼저 검사한다.
4. 각 조상의 기존 aggregate와 tag의 exact에 `checked(existing + count)`를 계산만 하여 overflow를 사전 검사한다.
5. 필요한 새 조상 entry 수를 세어 capacity를 한 번 확보한다. `expectedExactKinds * 16`을 최대 1,024로 clamp해 생성 시 예약한다.
6. 정렬 배열 끝에서 기존 entry와 새 조상 index를 한 번 merge하며 exact/aggregate 값을 기록한다.
7. `_entryCount`, `_exactKindCount`, `LastMutationDepth = ancestorCount`, `LastMutationPassCount = 1`을 마지막에 commit한다.

`Remove`는 exact가 없으면 0을 반환한다. 실제 제거량은 `Math.Min(requested, exact)`이고 모든 조상 aggregate에서 같은 양을 뺀다. exact와 aggregate가 모두 0인 entry를 한 번의 forward compact pass로 제거하고 마지막 슬롯들을 0으로 지운 뒤 실제 제거량을 반환한다.

모든 query는 `TagSearch.LowerBound` 한 번으로 index를 찾아 exact/aggregate 배열을 읽는다. Any/All은 `TagContainer` query와 같은 catalog를 검사한 뒤 query exact index를 순회한다.

- [ ] **Step 5: mutation pass 상한 테스트를 추가한다**

```csharp
[Test]
public void Mutation_uses_at_most_one_merge_or_compact_pass()
{
    _counts.Add(_ghost);
    Assert.That(_counts.LastMutationPassCount, Is.EqualTo(1));
    Assert.That(_counts.LastMutationDepth, Is.LessThanOrEqualTo(16));
    _counts.Add(_rooted);
    Assert.That(_counts.LastMutationPassCount, Is.EqualTo(1));
    Assert.That(_counts.LastMutationDepth, Is.LessThanOrEqualTo(16));
    _counts.Remove(_ghost);
    Assert.That(_counts.LastMutationPassCount, Is.EqualTo(1));
    Assert.That(_counts.LastMutationDepth, Is.LessThanOrEqualTo(16));
}

[Test]
public void Actual_count_queries_use_one_bounded_search()
{
    var catalog = TagCatalogTestData.Load(TagCatalogTestData.BuildChainCatalog(64, 16, true));
    var counts = catalog.CreateCountContainer(64);
    for (var i = 0; i < 64; i++)
        counts.Add(catalog.GetRequired(TagCatalogTestData.ChainLeaf(i, 16)));

    for (ushort i = 1; i <= catalog.Count; i++)
    {
        counts.GetCountsCore(catalog.GetRequiredByIndex(i), out _, out _, out var comparisons);
        Assert.That(comparisons, Is.LessThanOrEqualTo(11));
    }
}
```

`TagCatalogTestData.BuildChainCatalog(exactKinds, depth, includeMissRoot)`는 `B{i}.L1...L{depth-1}` leaf를 만들고 필요하면 `ZMiss` root를 추가하며, `ChainLeaf`는 같은 leaf path를 반환한다. `TagCountContainer`는 조상 수집 결과를 `LastMutationDepth`, merge/compact 횟수를 `LastMutationPassCount`에 test-only internal로 기록한다. `GetCountsCore`는 lower-bound를 정확히 한 번 부르고 public `ExactCount`/`Count`/`HasExact`/`Has`가 이 core를 공유하므로 실제 public 경로의 11회 상한을 검증한다. Any/All의 총 상한은 각 query마다 이 core 한 번인 11Q다.

- [ ] **Step 6: counted container 전체를 GREEN으로 만든다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~TagCountContainerTests"
if ($LASTEXITCODE -ne 0) { throw 'TagCountContainer tests failed.' }
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
```

Expected: 모든 exact/aggregate/overflow/capacity/mutation/query-bound 테스트 PASS, 새 runtime meta 존재, Unity warning CS 0.

- [ ] **Step 7: `TagCountContainer`를 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay/Runtime/Tags/TagCountContainer.cs* `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs `
  common/tests/Bun3.Gameplay.Tests/TagCountContainerTests.cs `
  common/tests/Bun3.Gameplay.Tests/TagPerformanceContractTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 5 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "✨ 조상 누적 TagCountContainer 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 5 failed.' }
```

### Task 6: legacy 동적 API를 제거하고 무할당·성능 계약으로 교체한다

**Files:**
- Create: `common/tests/Bun3.Gameplay.Tests/LegacyTagApiRemovalTests.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/TagPerformanceContractTests.cs`
- Delete: `common/tests/Bun3.Gameplay.Tests/TagRegistryTests.cs`
- Delete: `common/tests/Bun3.Gameplay.Tests/TagSetTests.cs`
- Delete: `common/src/com.bun3.gameplay/Runtime/Tags/TagRegistry.cs` and `.meta`
- Delete: `common/src/com.bun3.gameplay/Runtime/Tags/TagSet.cs` and `.meta`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/GameplayTag.cs`: Task 1 migration shim 제거.

**Interfaces:**
- Removes: `TagRegistry`, `GetOrRegister`, `TagSet`, public `GameplayTag.Handle`, internal int shim.
- Verifies: catalog/container single queries and 100,000-query batch allocate 0 after warmup.
- Verifies: structure bound is independent of catalog size 5,000/50,000 and M=8/32/64, D=1/4/8/16.

- [ ] **Step 1: legacy public API가 아직 존재해 실패하는 reflection RED 테스트를 쓴다**

```csharp
using System;
using System.Linq;
using System.Reflection;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class LegacyTagApiRemovalTests
{
    [Test]
    public void Dynamic_registry_and_ambiguous_tag_set_are_absent()
    {
        var exported = typeof(GameplayTag).Assembly.GetExportedTypes();
        Assert.That(exported.Any(t => t.FullName == "Bun3.Gameplay.Tags.TagRegistry"), Is.False);
        Assert.That(exported.Any(t => t.FullName == "Bun3.Gameplay.Tags.TagSet"), Is.False);
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Assert.That(typeof(GameplayTag).GetField("Handle", members), Is.Null);
        Assert.That(typeof(GameplayTag).GetProperty("Handle", members), Is.Null);
        Assert.That(typeof(GameplayTag).GetConstructors(members).Any(c =>
            c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(int)), Is.False);
    }
}
```

- [ ] **Step 2: reflection 테스트가 legacy 타입 때문에 RED인지 확인한다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~LegacyTagApiRemovalTests"
```

Expected: `TagRegistry`와 `TagSet` 존재 assertion 실패.

- [ ] **Step 3: allocation smoke를 새 API로 먼저 바꾼다**

기존 `Tag_queries_do_not_allocate`를 다음 구조로 교체한다. 측정 전에 catalog 구성, capacity 예약, Add, JIT warmup을 모두 끝낸다.

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

    _ = set.Has(dead);
    _ = counts.Has(dead);
    _ = counts.Count(dead);
    var before = GC.GetAllocatedBytesForCurrentThread();
    var hits = 0;
    for (var i = 0; i < 100_000; i++)
    {
        if (set.Has(dead)) hits++;
        if (counts.Has(dead)) hits++;
        hits += counts.Count(dead);
    }
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    Assert.That(hits, Is.EqualTo(400_000));
    Assert.That(allocated, Is.Zero);
}

[Test]
public void Reserved_tag_mutations_do_not_allocate()
{
    var catalog = TagCatalogTestData.Load(TagCatalogTestData.BuildChainCatalog(8, 16, false));
    var leaves = new GameplayTag[8];
    for (var i = 0; i < leaves.Length; i++)
        leaves[i] = catalog.GetRequired(TagCatalogTestData.ChainLeaf(i, 16));
    var tags = catalog.CreateContainer(8);
    var counts = catalog.CreateCountContainer(8);

    RunCycles(tags, counts, leaves, 1);
    var before = GC.GetAllocatedBytesForCurrentThread();
    RunCycles(tags, counts, leaves, 100);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    Assert.That(tags.ExactKindCount, Is.Zero);
    Assert.That(counts.ExactKindCount, Is.Zero);
    Assert.That(allocated, Is.Zero);
}

private static void RunCycles(
    TagContainer tags,
    TagCountContainer counts,
    GameplayTag[] leaves,
    int cycles)
{
    for (var cycle = 0; cycle < cycles; cycle++)
    {
        for (var i = 0; i < leaves.Length; i++)
        {
            tags.Add(leaves[i]);
            counts.Add(leaves[i]);
        }
        for (var i = 0; i < leaves.Length; i++)
        {
            tags.Remove(leaves[i]);
            counts.Remove(leaves[i]);
        }
    }
}
```

- [ ] **Step 4: 구조 행렬 테스트를 완성한다**

`TagCatalogTestData`에 exact chain과 미보유 filler를 모두 가진 helper를 추가한다.

```csharp
internal static string BuildPerformanceCatalog(int catalogSize, int exactKinds, int depth)
{
    if (catalogSize < exactKinds * depth)
        throw new ArgumentOutOfRangeException(nameof(catalogSize));
    var json = new System.Text.StringBuilder(catalogSize * 24);
    json.Append("{\"schemaVersion\":1,\"tags\":[");
    var written = 0;
    for (var i = 0; i < exactKinds; i++)
    {
        if (written++ != 0) json.Append(',');
        json.Append("{\"name\":\"").Append(ChainLeaf(i, depth)).Append("\"}");
    }
    for (var i = 0; i < catalogSize - exactKinds * depth; i++)
    {
        if (written++ != 0) json.Append(',');
        json.Append("{\"name\":\"F").Append(i).Append("\"}");
    }
    return json.Append("]}").ToString();
}
```

`TagPerformanceContractTests`는 아래 test와 helper를 그대로 두어 `N=5_000/50_000`, `M=8/32/64`, `D=1/4/8/16`, exact hit/parent hit/miss 전체 72 query case를 실제 public API로 100,000번씩 실행한다.

```csharp
[Test]
public void Catalog_size_kind_depth_and_query_matrix_is_bounded_and_allocation_free()
{
    foreach (var catalogSize in new[] { 5_000, 50_000 })
    foreach (var exactKinds in new[] { 8, 32, 64 })
    foreach (var depth in new[] { 1, 4, 8, 16 })
    {
        var catalog = TagCatalogTestData.Load(
            TagCatalogTestData.BuildPerformanceCatalog(catalogSize, exactKinds, depth));
        var tags = catalog.CreateContainer(exactKinds);
        var counts = catalog.CreateCountContainer(exactKinds);
        var exact = new GameplayTag[exactKinds];
        var parents = new GameplayTag[exactKinds];
        var misses = new GameplayTag[exactKinds];
        for (var i = 0; i < exactKinds; i++)
        {
            exact[i] = catalog.GetRequired(TagCatalogTestData.ChainLeaf(i, depth));
            parents[i] = catalog.GetRequired("B" + i);
            misses[i] = catalog.GetRequired("F" + i);
            tags.Add(exact[i]);
            counts.Add(exact[i]);
        }

        AssertQueryCase(tags, counts, exact, expectedPerIteration: 2);
        AssertQueryCase(tags, counts, parents, expectedPerIteration: 2);
        AssertQueryCase(tags, counts, misses, expectedPerIteration: 0);
    }
}

private static void AssertQueryCase(
    TagContainer tags,
    TagCountContainer counts,
    GameplayTag[] queries,
    int expectedPerIteration)
{
    for (var i = 0; i < queries.Length; i++)
    {
        _ = tags.HasCore(queries[i], exact: false, out var tagComparisons);
        counts.GetCountsCore(queries[i], out _, out _, out var countComparisons);
        Assert.That(tagComparisons, Is.LessThanOrEqualTo(7));
        Assert.That(countComparisons, Is.LessThanOrEqualTo(11));
    }

    _ = RunQueryBatch(tags, counts, queries, 1);
    var before = GC.GetAllocatedBytesForCurrentThread();
    var checksum = RunQueryBatch(tags, counts, queries, 100_000);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.That(checksum, Is.EqualTo(100_000 * expectedPerIteration));
    Assert.That(allocated, Is.Zero);
}

private static int RunQueryBatch(
    TagContainer tags,
    TagCountContainer counts,
    GameplayTag[] queries,
    int iterations)
{
    var checksum = 0;
    for (var i = 0; i < iterations; i++)
    {
        var query = queries[i % queries.Length];
        if (tags.Has(query)) checksum++;
        checksum += counts.Count(query);
    }
    return checksum;
}
```

fixture 생성, 문자열 해석, 배열과 capacity 예약은 측정 밖이다. `HasCore`/`GetCountsCore`는 public query가 공유하는 바로 그 lower-bound를 계수하므로 별도 test-only 검색 구현이 없다. `D=1`에서는 chain root와 leaf가 같아 parent case가 exact와 동일하고, 나머지 깊이에서는 진짜 조상 hit다.

- [ ] **Step 5: old production/tests와 migration shim을 함께 제거한다**

다음 파일 네 개를 삭제한다.

```text
common/src/com.bun3.gameplay/Runtime/Tags/TagRegistry.cs
common/src/com.bun3.gameplay/Runtime/Tags/TagRegistry.cs.meta
common/src/com.bun3.gameplay/Runtime/Tags/TagSet.cs
common/src/com.bun3.gameplay/Runtime/Tags/TagSet.cs.meta
```

`TagRegistryTests.cs`, `TagSetTests.cs`도 삭제한다. `GameplayTag.cs`에서 Task 1의 internal `GameplayTag(int)`와 `Handle` getter를 삭제해 `ushort` constructor 하나만 남긴다.

- [ ] **Step 6: 전체 Gameplay .NET suite와 warning gate를 GREEN으로 만든다**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo
if ($LASTEXITCODE -ne 0) { throw 'Gameplay test suite failed.' }
dotnet build common/src/com.bun3.gameplay/Bun3.Gameplay.csproj --nologo -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Gameplay warning gate failed.' }
$legacyMatches = @(rg -n "TagRegistry|TagSet|GetOrRegister|Handle" `
  common/src/com.bun3.gameplay/Runtime/Tags)
$rgExit = $LASTEXITCODE
if ($rgExit -eq 0) { $legacyMatches; throw 'Legacy GameplayTag API remains.' }
if ($rgExit -gt 1) { throw "rg failed with exit code $rgExit." }
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
```

Expected: Gameplay 전체 PASS, warning/error 0. `rg` exit 1은 정확히 0건이므로 성공이고 0은 잔존 API, 2 이상은 검색 오류로 실패한다.

- [ ] **Step 7: breaking migration을 커밋한다**

```powershell
git add -A -- common/src/com.bun3.gameplay/Runtime/Tags `
  common/tests/Bun3.Gameplay.Tests
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 6 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "🔥 동적 GameplayTag 레지스트리를 제거" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 6 failed.' }
```

### Task 7: 같은 골든 계약을 .NET과 Unity EditMode에서 실행한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Tests/Editor/TagCatalogConformanceTests.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
- Generate by Unity: `TagCatalogConformanceTests.cs.meta`.
- Verify: `unity/Packages/packages-lock.json`.

**Interfaces:**
- Consumes: Tasks 1–6의 최종 public catalog/container API.
- Produces: 동일 소스가 .NET NUnit과 `Bun3.Gameplay.Unity.Tests`에서 검증하는 index/parent/subtree/redirect/fingerprint golden.
- Verifies host seam: peer fingerprint가 다르면 simulation 시작 callback을 호출하지 않고, Ability/Effect/장비의 독립 기여는 마지막 source 제거 전까지 subject count를 유지한다.
- Verifies: UPM resolver가 gameplay package의 `com.unity.nuget.newtonsoft-json: 3.2.2`를 실제 dependency로 해석한다.

- [ ] **Step 1: C# 9 호환 shared conformance test를 쓴다**

```csharp
#nullable enable
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests
{
    [TestFixture]
    public sealed class TagCatalogConformanceTests
    {
        private const string Json =
            "{\"schemaVersion\":1,\"tags\":[" +
            "{\"name\":\"State.Rooted\"}," +
            "{\"name\":\"Ability.Movement.Jump\"}," +
            "{\"name\":\"State.Dead.Ghost\"}]," +
            "\"redirects\":[{\"from\":\"State.Killed\",\"to\":\"State.Dead\"}]}";

        [Test]
        public void Runtime_indices_hierarchy_redirect_and_fingerprint_match_golden()
        {
            using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(Json));
            var catalog = TagCatalog.Load(stream);

            Assert.That(catalog.Count, Is.EqualTo(7));
            Assert.That(catalog.GetRequired("ability").Index, Is.EqualTo(1));
            Assert.That(catalog.GetRequired("ability.movement.jump").Index, Is.EqualTo(3));
            Assert.That(catalog.GetRequired("state.dead").Index, Is.EqualTo(5));
            Assert.That(catalog.GetRequired("state.dead.ghost").Index, Is.EqualTo(6));
            Assert.That(catalog.GetRequired("state.rooted").Index, Is.EqualTo(7));
            Assert.That(catalog.GetParent(catalog.GetRequired("state.dead.ghost")),
                Is.EqualTo(catalog.GetRequired("state.dead")));
            Assert.That(catalog.GetSubtreeEnd(catalog.GetRequired("state")), Is.EqualTo(7));
            Assert.That(catalog.GetSubtreeEnd(catalog.GetRequired("state.dead")), Is.EqualTo(6));
            Assert.That(catalog.GetSubtreeEnd(catalog.GetRequired("state.dead.ghost")), Is.EqualTo(6));
            Assert.That(catalog.IsAncestorOrSelf(
                catalog.GetRequired("state.dead"),
                catalog.GetRequired("state.dead.ghost")), Is.True);
            Assert.That(catalog.IsAncestorOrSelf(
                catalog.GetRequired("state.dead"),
                catalog.GetRequired("state.rooted")), Is.False);
            Assert.That(catalog.GetRequired("STATE.KILLED"),
                Is.EqualTo(catalog.GetRequired("state.dead")));
            Assert.That(ToHex(catalog.Fingerprint),
                Is.EqualTo("feef3116e20f93b5383d8061ffc20ff189fc939a3291d6fa9f09ff3d16ff5f0e"));
        }

        [Test]
        public void Containers_match_the_same_hierarchy_contract()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
            var catalog = TagCatalog.Load(stream);
            var ghost = catalog.GetRequired("State.Dead.Ghost");
            var dead = catalog.GetRequired("State.Dead");

            var tags = catalog.CreateContainer(1);
            tags.Add(ghost);
            var counts = catalog.CreateCountContainer(1);
            counts.Add(ghost, 2);

            Assert.That(tags.Has(dead), Is.True);
            Assert.That(tags.HasExact(dead), Is.False);
            Assert.That(counts.Count(dead), Is.EqualTo(2));
            Assert.That(counts.ExactCount(dead), Is.Zero);
        }

        [Test]
        public void Fingerprint_gate_rejects_mismatched_peer_before_simulation_starts()
        {
            using var localStream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
            var local = TagCatalog.Load(localStream);
            const string changedRedirectJson =
                "{\"schemaVersion\":1,\"tags\":[" +
                "{\"name\":\"State.Rooted\"}," +
                "{\"name\":\"Ability.Movement.Jump\"}," +
                "{\"name\":\"State.Dead.Ghost\"}]," +
                "\"redirects\":[{\"from\":\"State.Gone\",\"to\":\"State.Dead\"}]}";
            using var peerStream = new MemoryStream(Encoding.UTF8.GetBytes(changedRedirectJson));
            var peer = TagCatalog.Load(peerStream);
            var simulationStarts = 0;

            Assert.That(TryStartSimulation(local, peer.Fingerprint, ref simulationStarts), Is.False);
            Assert.That(simulationStarts, Is.Zero);
            var matchingFingerprint = local.Fingerprint.ToArray();
            Assert.That(TryStartSimulation(local, matchingFingerprint, ref simulationStarts), Is.True);
            Assert.That(simulationStarts, Is.EqualTo(1));
        }

        [Test]
        public void Ability_effect_and_equipment_contributions_survive_until_the_last_source_is_removed()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
            var catalog = TagCatalog.Load(stream);
            var state = catalog.GetRequired("State");
            var dead = catalog.GetRequired("State.Dead");
            var subject = catalog.CreateCountContainer(1);
            var abilityGranted = dead;
            var effectGranted = dead;
            var equipmentGranted = dead;

            subject.Add(abilityGranted);
            subject.Add(effectGranted);
            subject.Add(equipmentGranted);
            Assert.That(subject.ExactCount(dead), Is.EqualTo(3));
            Assert.That(subject.Count(state), Is.EqualTo(3));

            Assert.That(subject.Remove(abilityGranted), Is.EqualTo(1));
            Assert.That(subject.ExactCount(dead), Is.EqualTo(2));
            Assert.That(subject.Has(state), Is.True);
            Assert.That(subject.Remove(effectGranted), Is.EqualTo(1));
            Assert.That(subject.ExactCount(dead), Is.EqualTo(1));
            Assert.That(subject.Has(state), Is.True);
            Assert.That(subject.Remove(equipmentGranted), Is.EqualTo(1));
            Assert.That(subject.ExactCount(dead), Is.Zero);
            Assert.That(subject.Count(state), Is.Zero);
            Assert.That(subject.Has(state), Is.False);
        }

        private static bool TryStartSimulation(
            TagCatalog local,
            System.ReadOnlySpan<byte> peerFingerprint,
            ref int simulationStarts)
        {
            if (!local.MatchesFingerprint(peerFingerprint))
                return false;
            simulationStarts++;
            return true;
        }

        private static string ToHex(System.ReadOnlySpan<byte> bytes)
        {
            const string digits = "0123456789abcdef";
            var chars = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 15];
            }
            return new string(chars);
        }
    }
}
```

- [ ] **Step 2: shared source를 .NET test project에 link한다**

```xml
<ItemGroup>
  <Compile Include="..\..\src\com.bun3.gameplay\Tests\Editor\TagCatalogConformanceTests.cs"
           Link="TagCatalogConformanceTests.cs" />
</ItemGroup>
```

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
  --filter "FullyQualifiedName~TagCatalogConformanceTests"
if ($LASTEXITCODE -ne 0) { throw 'Shared tag conformance tests failed.' }
```

Expected: shared 4건 PASS. redirect 의미만 다른 peer는 simulation callback을 실행하지 않고, 같은 fingerprint만 시작하며, Ability/Effect/장비의 마지막 기여가 빠질 때까지 subject의 exact/aggregate 상태가 유지된다.

- [ ] **Step 3: Unity import/resolver로 모든 누락 meta와 lock dependency를 생성한다**

Unity project를 열고 있는 interactive Editor가 없는지 먼저 확인한다. 그 다음 Task 1에서 체크인한 공용 runner로 compile/import만 수행한다.

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
```

Unity가 생성한 package 하위 `.meta`를 유지한다. `unity/Packages/packages-lock.json`의
`com.bun3.gameplay.dependencies`가 다음을 포함해야 한다.

```json
"com.unity.nuget.newtonsoft-json": "3.2.2"
```

local gameplay `version`은 semantic version이 아니라 계속 `file:../../common/src/com.bun3.gameplay`여야 한다.

- [ ] **Step 4: targeted Gameplay EditMode를 실행한다**

공용 runner의 EditMode 경로는 이 저장소에서 Test Runner 완료 전에 종료될 수 있는 `-quit`를 넣지 않고 XML discovery/result, log completion, warning/error를 모두 판정한다.

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
```

Expected: 기존 BigNum smoke와 shared tag conformance 모두 PASS, `error CS`와 warning CS 없음.

- [ ] **Step 5: shared cross-runtime gate와 Unity 생성물을 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay/Tests/Editor/TagCatalogConformanceTests.cs* `
  common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj `
  unity/Packages/packages-lock.json
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 7 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "✅ GameplayTag .NET Unity 공용 계약 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 7 failed.' }
```

### Task 8: transactional Unity 태그 편집 세션을 구현한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Bun3.Gameplay.Editor.asmdef`
- Create: `common/src/com.bun3.gameplay/Editor/AssemblyInfo.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogEditSession.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogEditSessionTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/Bun3.Gameplay.Tests.asmdef`
- Generate by Unity: matching `.meta` files.

**Interfaces:**
- Produces internal: `GameplayTagCatalogEditSession.Open`, `Tags`, `Redirects`, `Add`, `SetComment`, `RelocateSubtree`, `Delete`, `Serialize`.
- Contract: 모든 mutation은 clone→serialize→`TagCatalog.Load` validation→swap 순서다. 실패하면 byte-for-byte 직전 `Serialize()` 결과를 보존한다.
- Contract: parent rename/move는 바뀐 old active subtree 모든 경로에 direct redirect를 만들고 기존 redirect target도 새 final path로 다시 쓴다.

- [ ] **Step 1: Editor assembly와 friend test 경계를 만든다**

`Bun3.Gameplay.Editor.asmdef`:

```json
{
  "name": "Bun3.Gameplay.Editor",
  "rootNamespace": "Bun3.Gameplay.Editor",
  "references": ["Bun3.Gameplay"],
  "includePlatforms": ["Editor"],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

`AssemblyInfo.cs`:

```csharp
#nullable enable
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Bun3.Gameplay.Unity.Tests")]
```

기존 `Bun3.Gameplay.Tests.asmdef`의 `references`에 `Bun3.Gameplay.Editor`를 추가한다. 이 파일은 `overrideReferences: true`를 유지한다.

- [ ] **Step 2: add/comment/canonical serialization RED 테스트를 쓴다**

```csharp
#nullable enable
using Bun3.Gameplay.Editor.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Unity.Tests
{
    [TestFixture]
    public sealed class GameplayTagCatalogEditSessionTests
    {
        [Test]
        public void Add_and_comment_serialize_in_case_insensitive_path_order()
        {
            var session = GameplayTagCatalogEditSession.Open(
                "{\"schemaVersion\":1,\"tags\":[]}");

            session.Add("State.Dead", "사망");
            session.Add("ability.Jump", "점프");
            session.SetComment("STATE.DEAD", "전투 불능");

            var json = session.Serialize();
            Assert.That(json, Does.Contain("\"name\": \"ability.Jump\""));
            Assert.That(json, Does.Contain("\"comment\": \"전투 불능\""));
            Assert.That(json.IndexOf("ability.Jump", System.StringComparison.Ordinal),
                Is.LessThan(json.IndexOf("State.Dead", System.StringComparison.Ordinal)));
            Assert.That(json.EndsWith("\n", System.StringComparison.Ordinal), Is.True);
        }
    }
}
```

- [ ] **Step 3: subtree relocate/redirect rewrite와 atomicity RED 테스트를 추가한다**

```csharp
[Test]
public void Commenting_an_implicit_parent_promotes_only_its_authoring_row()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");
    session.SetComment("State", "상태 루트");
    var json = session.Serialize();
    Assert.That(json, Does.Contain("\"name\": \"State\""));
    Assert.That(json, Does.Contain("\"comment\": \"상태 루트\""));
}

[Test]
public void Relocate_subtree_creates_direct_redirects_and_rewrites_old_targets()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[" +
        "{\"name\":\"State.Dead\"},{\"name\":\"State.Dead.Ghost\"}," +
        "{\"name\":\"State.Dead.Ghost.Spirit\"}]," +
        "\"redirects\":[{\"from\":\"Legacy.Dead\",\"to\":\"State.Dead\"}]}");

    session.RelocateSubtree("State.Dead", "Condition.Deceased");
    var json = session.Serialize();

    Assert.That(json, Does.Contain("Condition.Deceased.Ghost.Spirit"));
    Assert.That(json, Does.Contain("\"from\": \"State.Dead\""));
    Assert.That(json, Does.Contain("\"from\": \"State.Dead.Ghost\""));
    Assert.That(json, Does.Contain("\"from\": \"State.Dead.Ghost.Spirit\""));
    Assert.That(json, Does.Contain("\"from\": \"Legacy.Dead\""));
    Assert.That(json, Does.Not.Contain("\"to\": \"State.Dead\""));
}

[Test]
public void Collision_keeps_the_previous_document_byte_for_byte()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"},{\"name\":\"State.Alive\"}]}");
    var before = session.Serialize();

    Assert.Throws<System.InvalidOperationException>(
        () => session.RelocateSubtree("State.Dead", "State.Alive"));
    Assert.That(session.Serialize(), Is.EqualTo(before));
}

[Test]
public void Case_only_relocate_changes_display_case_without_a_redirect()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");
    session.RelocateSubtree("State.Dead", "state.dead");
    var json = session.Serialize();
    Assert.That(json, Does.Contain("\"name\": \"state.dead\""));
    Assert.That(json, Does.Not.Contain("\"from\""));
}

[Test]
public void Delete_requires_subtree_authorization_and_removes_dangling_redirects()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead.Ghost\"}]," +
        "\"redirects\":[{\"from\":\"Old.Ghost\",\"to\":\"State.Dead.Ghost\"}]}");

    Assert.Throws<System.InvalidOperationException>(() => session.Delete("State.Dead", false));
    session.Delete("State.Dead", true);
    var json = session.Serialize();
    Assert.That(json, Does.Not.Contain("State.Dead"));
    Assert.That(json, Does.Not.Contain("Old.Ghost"));
}
```

- [ ] **Step 4: edit session이 없어 RED인지 Unity EditMode로 확인한다**

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogEditSessionTests'
```

Expected: `GameplayTagCatalogEditSession` 컴파일 실패.

- [ ] **Step 5: immutable authoring snapshot과 transactional apply를 구현한다**

Editor 내부 row와 API를 다음으로 고정한다.

```csharp
internal readonly struct EditableTagRow
{
    internal EditableTagRow(string name, string comment) { Name = name; Comment = comment; }
    internal string Name { get; }
    internal string Comment { get; }
}

internal readonly struct EditableRedirectRow
{
    internal EditableRedirectRow(string from, string to) { From = from; To = to; }
    internal string From { get; }
    internal string To { get; }
}

internal sealed class GameplayTagCatalogEditSession
{
    internal static GameplayTagCatalogEditSession Open(string json);
    internal System.Collections.Generic.IReadOnlyList<EditableTagRow> Tags { get; }
    internal System.Collections.Generic.IReadOnlyList<EditableRedirectRow> Redirects { get; }
    internal void Add(string path, string comment = "");
    internal void SetComment(string path, string comment);
    internal void RelocateSubtree(string oldPath, string newPath);
    internal void Delete(string path, bool includeDescendants);
    internal string Serialize();
}
```

`Open`은 먼저 UTF-8 bytes로 `TagCatalog.Load`를 호출하고 성공한 JSON만 Newtonsoft `JObject`로 authoring row에 읽는다. `Apply`는 현재 row/redirect list를 깊은 복사하고 mutation을 복사본에만 수행한 뒤 canonical serialize와 공용 `TagCatalog.Load`를 통과한 경우에만 필드를 교체한다. `SetComment` 대상이 암시 부모이면 catalog의 표시 path로 explicit authoring row 하나를 승격한 뒤 comment를 기록한다. 이는 active canonical path를 바꾸지 않으므로 index/fingerprint도 바뀌지 않는다.

`RelocateSubtree`는 old catalog의 index 1..Count를 순회해 `oldPath` 자신과 `oldPath + "."` prefix인 모든 active path를 수집한다. canonical old/new path가 같으면 explicit row와 descendant의 표시 casing만 바꾸고 redirect를 만들지 않는다. 그 외에는 explicit tag rows의 prefix를 새 prefix로 바꾸고, 수집한 active old path 각각을 suffix가 같은 new active path로 direct redirect한다. 기존 redirect target도 같은 prefix mapping으로 다시 쓴다. source/target collision은 common loader가 감지하며 실패 시 snapshot을 바꾸지 않는다.

`Serialize`는 `JsonTextWriter`의 `Formatting.Indented`, indentation 2, `StringWriter.NewLine = "\n"`, invariant culture를 사용한다. property 순서는 root `schemaVersion`, `tags`, `redirects`; tag `name`, `comment`; redirect `from`, `to`다. tags와 redirects는 ASCII-fold canonical path ordinal 순으로 기록하고 UTF-16 string 끝에 LF 하나를 붙인다.

- [ ] **Step 6: session 테스트를 GREEN으로 만들고 Unity가 meta를 생성하게 한다**

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogEditSessionTests'
```

Expected: add/comment/relocate/delete/atomicity 전체 PASS, 새 Editor 폴더·asmdef·C#·test의 `.meta` 생성, warning CS 0.

- [ ] **Step 7: transactional editor core를 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay/Editor `
  common/src/com.bun3.gameplay/Editor.meta `
  common/src/com.bun3.gameplay/Tests/Editor `
  common/src/com.bun3.gameplay/Tests/Editor/Bun3.Gameplay.Tests.asmdef
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 8 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "✨ GameplayTag 트랜잭션 편집 세션 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 8 failed.' }
```

### Task 9: validation-before-write 파일 어댑터를 구현한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogFileAdapter.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogFileAdapterTests.cs`
- Generate by Unity: matching `.meta` files.

**Interfaces:**
- Produces internal: `Load(string absolutePath)`, `Save(string absolutePath, GameplayTagCatalogEditSession session)`, `TryToAssetPath`.
- Contract: UTF-8 no BOM, validation before any destination mutation, same directory temporary file, existing destination은 `File.Replace`, 새 destination은 `File.Move`.
- Contract: project `Assets/` 내부 경로만 `AssetDatabase.ImportAsset`하며 외부 절대 경로는 파일 저장까지만 수행한다.

- [ ] **Step 1: valid save/reload RED 테스트를 쓴다**

test 파일 상단에 `using System.Linq;`를 포함한다.

```csharp
[Test]
public void Save_writes_utf8_without_bom_and_reload_reads_external_change()
{
    var path = System.IO.Path.Combine(_temporaryDirectory, "GameplayTags.json");
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}");

    GameplayTagCatalogFileAdapter.Save(path, session);
    var bytes = System.IO.File.ReadAllBytes(path);
    Assert.That(bytes.Take(3).ToArray(), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
    Assert.That(GameplayTagCatalogFileAdapter.Load(path).Serialize(), Does.Contain("State.Dead"));

    System.IO.File.WriteAllText(path,
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Alive\"}]}",
        new System.Text.UTF8Encoding(false, true));
    Assert.That(GameplayTagCatalogFileAdapter.Load(path).Serialize(), Does.Contain("State.Alive"));
}
```

test fixture의 `[SetUp]`은 `Path.Combine(Path.GetTempPath(), "bun3-tag-file-tests-" + Guid.NewGuid().ToString("N"))`를 만들고 `[TearDown]`은 그 정확한 디렉터리만 `Directory.Delete(path, true)`한다.

- [ ] **Step 2: invalid staged data가 기존 파일을 보존하는 RED 테스트를 쓴다**

파일 어댑터의 serialization seam을 시험할 수 있도록 internal `SaveJson(string absolutePath, string json)`도 둔다.

```csharp
[Test]
public void Invalid_json_never_overwrites_existing_file()
{
    var path = System.IO.Path.Combine(_temporaryDirectory, "GameplayTags.json");
    const string original = "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"}]}";
    System.IO.File.WriteAllText(path, original, new System.Text.UTF8Encoding(false, true));

    Assert.Throws<Bun3.Gameplay.Tags.TagCatalogException>(
        () => GameplayTagCatalogFileAdapter.SaveJson(
            path, "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State_Bad\"}]}"));

    Assert.That(System.IO.File.ReadAllText(path), Is.EqualTo(original));
    Assert.That(System.IO.Directory.GetFiles(_temporaryDirectory, "*.tmp"), Is.Empty);
}
```

- [ ] **Step 3: 파일 어댑터 부재로 RED인지 targeted EditMode에서 확인한다**

Run:

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogFileAdapterTests'
```

Expected: file adapter type 부재로 컴파일 실패.

- [ ] **Step 4: validate-stage-replace-import 순서를 구현한다**

```csharp
internal static void SaveJson(string absolutePath, string json)
{
    var bytes = new UTF8Encoding(false, true).GetBytes(json);
    using (var validation = new MemoryStream(bytes, false))
        _ = TagCatalog.Load(validation);

    var directory = Path.GetDirectoryName(Path.GetFullPath(absolutePath))
        ?? throw new ArgumentException("저장 디렉터리가 없습니다.", nameof(absolutePath));
    Directory.CreateDirectory(directory);
    var temporary = Path.Combine(
        directory,
        "." + Path.GetFileName(absolutePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
    try
    {
        File.WriteAllBytes(temporary, bytes);
        if (File.Exists(absolutePath))
            File.Replace(temporary, absolutePath, null);
        else
            File.Move(temporary, absolutePath);
    }
    finally
    {
        if (File.Exists(temporary))
            File.Delete(temporary);
    }

    if (TryToAssetPath(absolutePath, out var assetPath))
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
}
```

`TryToAssetPath`는 `Path.GetFullPath(Path.Combine(Application.dataPath, ".."))`와 대상 full path를 비교할 때 directory separator 경계를 포함하고, `Assets/` 아래일 때만 slash를 `/`로 바꾼 project-relative path를 반환한다.

- [ ] **Step 5: 파일 어댑터 테스트를 GREEN으로 만들고 meta를 생성한다**

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogFileAdapterTests'
```

Expected: valid save/reload, BOM 없음, invalid 보존, temp 정리 모두 PASS.

- [ ] **Step 6: 파일 어댑터를 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogFileAdapter.cs* `
  common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogFileAdapterTests.cs*
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 9 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "✨ GameplayTag JSON 안전 저장 어댑터 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 9 failed.' }
```

### Task 10: Unreal식 기본 관리 경험을 제공하는 Unity 트리 UI를 구현한다

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogViewModel.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeView.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagValidationWindow.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogViewModelTests.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`
- Generate by Unity: matching `.meta` files.

**Interfaces:**
- Produces: menu `Bun3/Gameplay Tags` and `GameplayTagCatalogWindow`.
- Produces internal model/controller: full tree rows, case-insensitive search 결과와 ancestor context, file/session/dirty/selected path 상태 및 authoring commands.
- Consumes: Task 8 edit session and Task 9 file adapter only; UI가 JSON을 직접 조작하지 않는다.

- [ ] **Step 1: 검색 결과와 ancestor context의 RED 테스트를 쓴다**

```csharp
[Test]
public void Search_keeps_matching_rows_and_their_ancestor_context()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[" +
        "{\"name\":\"State.Dead.Ghost\",\"comment\":\"유령 상태\"}," +
        "{\"name\":\"State.Alive\"},{\"name\":\"Ability.Jump\"}]}");
    var model = new GameplayTagCatalogViewModel(session);

    var rows = model.Filter("gHoSt");

    Assert.That(rows.Select(r => r.Path),
        Is.EqualTo(new[] { "State", "State.Dead", "State.Dead.Ghost" }));
    Assert.That(rows[2].Comment, Is.EqualTo("유령 상태"));
    Assert.That(rows[2].IsDirectMatch, Is.True);
    Assert.That(rows[0].IsDirectMatch, Is.False);
}

[Test]
public void Empty_search_returns_deterministic_preorder()
{
    var session = GameplayTagCatalogEditSession.Open(
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"},{\"name\":\"Ability.Jump\"}]}");
    var model = new GameplayTagCatalogViewModel(session);
    Assert.That(model.Filter("").Select(r => r.Path),
        Is.EqualTo(new[] { "Ability", "Ability.Jump", "State", "State.Dead" }));
}
```

test 파일에 `using System.Linq;`를 명시한다.

- [ ] **Step 2: EditorWindow 생성과 메뉴 smoke RED 테스트를 쓴다**

```csharp
[Test]
public void Window_opens_without_loading_a_catalog()
{
    var window = UnityEditor.EditorWindow.GetWindow<GameplayTagCatalogWindow>();
    try
    {
        Assert.That(window.titleContent.text, Is.EqualTo("Gameplay Tags"));
    }
    finally
    {
        window.Close();
    }
}

[Test]
public void Tree_row_label_exposes_the_comment_as_a_tooltip()
{
    var row = new GameplayTagTreeRowModel(
        index: 2, parentIndex: 1, path: "State.Dead", comment: "전투 불능", directMatch: true);
    var content = GameplayTagTreeView.CreateLabelContent(row);
    Assert.That(content.text, Is.EqualTo("Dead"));
    Assert.That(content.tooltip, Is.EqualTo("전투 불능"));
}

[Test]
public void Controller_executes_file_and_authoring_workflow_without_bypassing_the_session()
{
    var path = System.IO.Path.Combine(_temporaryDirectory, "GameplayTags.json");
    var controller = new GameplayTagCatalogWindowController();
    controller.New(path);
    controller.Add("State.Dead", "사망");
    controller.SetComment("State.Dead", "전투 불능");
    controller.RelocateSubtree("State.Dead", "Condition.Deceased");
    Assert.That(controller.IsDirty, Is.True);
    Assert.That(controller.SelectedPath, Is.EqualTo("Condition.Deceased"));
    controller.Save();
    Assert.That(controller.IsDirty, Is.False);
    Assert.That(System.IO.File.ReadAllText(path), Does.Contain("Condition.Deceased"));

    controller.Add("Condition.Stunned");
    Assert.That(controller.Reload(discardDirty: false), Is.False);
    Assert.That(controller.Session!.Serialize(), Does.Contain("Condition.Stunned"));
    Assert.That(controller.Reload(discardDirty: true), Is.True);
    Assert.That(controller.Session!.Serialize(), Does.Not.Contain("Condition.Stunned"));
    controller.Delete("Condition.Deceased", includeDescendants: false);
    Assert.That(controller.Session!.Serialize(), Does.Not.Contain("Condition.Deceased"));
}

[Test]
public void Failed_command_preserves_state_and_produces_validation_diagnostics()
{
    var path = System.IO.Path.Combine(_temporaryDirectory, "GameplayTags.json");
    System.IO.File.WriteAllText(path,
        "{\"schemaVersion\":1,\"tags\":[{\"name\":\"State.Dead\"},{\"name\":\"State.Alive\"}]}",
        new System.Text.UTF8Encoding(false, true));
    var controller = new GameplayTagCatalogWindowController();
    controller.Open(path);
    var before = controller.Session!.Serialize();

    var succeeded = controller.TryExecute(
        () => controller.RelocateSubtree("State.Dead", "State.Alive"), out var error);

    Assert.That(succeeded, Is.False);
    Assert.That(error, Is.Not.Null);
    Assert.That(controller.Session!.Serialize(), Is.EqualTo(before));
    var diagnostic = GameplayTagValidationWindow.FormatDiagnostic(path, error!);
    Assert.That(diagnostic, Does.Contain(path));
    Assert.That(diagnostic, Does.Contain(error!.Message));
}

[Test]
public void Validation_diagnostic_includes_json_path_line_and_position()
{
    const string invalid =
        "{\n  \"schemaVersion\":1,\n  \"tags\":[{\"name\":\"State_Bad\"}]\n}";
    using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalid));
    var error = Assert.Throws<Bun3.Gameplay.Tags.TagCatalogException>(
        () => Bun3.Gameplay.Tags.TagCatalog.Load(stream));
    var diagnostic = GameplayTagValidationWindow.FormatDiagnostic("GameplayTags.json", error!);
    Assert.That(diagnostic, Does.Contain("tags[0].name"));
    Assert.That(diagnostic, Does.Contain(error!.LineNumber.ToString()));
    Assert.That(diagnostic, Does.Contain(error.LinePosition.ToString()));
}
```

window test fixture는 `private string _temporaryDirectory = null!;`을 두고 `[SetUp]`에서 `Path.Combine(Path.GetTempPath(), "bun3-tag-window-tests-" + Guid.NewGuid().ToString("N"))`를 만든다. `[TearDown]`은 이 정확한 디렉터리가 존재할 때만 `Directory.Delete(_temporaryDirectory, true)`로 지운다. workflow test는 실제 file adapter를 통과하므로 New/Save/Reload의 파일 경계와 session mutation의 원자성을 함께 검증한다.

- [ ] **Step 3: view model/window 부재로 RED인지 targeted EditMode에서 확인한다**

Run:

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogViewModelTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

Expected: 두 production type을 찾지 못해 컴파일 실패.

- [ ] **Step 4: UI 독립 view model을 구현한다**

```csharp
internal readonly struct GameplayTagTreeRowModel
{
    internal GameplayTagTreeRowModel(
        ushort index, ushort parentIndex, string path, string comment, bool directMatch)
    {
        Index = index;
        ParentIndex = parentIndex;
        Path = path;
        Comment = comment;
        IsDirectMatch = directMatch;
    }

    internal ushort Index { get; }
    internal ushort ParentIndex { get; }
    internal string Path { get; }
    internal string Comment { get; }
    internal bool IsDirectMatch { get; }
}
```

`GameplayTagCatalogViewModel` constructor는 session의 canonical JSON을 common `TagCatalog`로 열어 index/parent/preorder를 가져오고 explicit row의 comment를 ASCII-fold path dictionary로 결합한다. `Filter(search)`는 빈 검색이면 전체 row를 반환한다. 검색어가 있으면 `Path.IndexOf(search, OrdinalIgnoreCase)` 또는 `Comment.IndexOf(search, OrdinalIgnoreCase)`가 성공한 row와 그 parent chain을 boolean include 배열에 표시한 뒤 원래 preorder 순으로 반환한다.

`GameplayTagCatalogWindowController`는 IMGUI나 dialog API를 참조하지 않고 다음 상태 전이만 소유한다.

```csharp
internal sealed class GameplayTagCatalogWindowController
{
    internal string FilePath { get; private set; } = string.Empty;
    internal GameplayTagCatalogEditSession? Session { get; private set; }
    internal string SelectedPath { get; private set; } = string.Empty;
    internal bool IsDirty { get; private set; }

    internal void New(string absolutePath);
    internal void Open(string absolutePath);
    internal bool Reload(bool discardDirty);
    internal void Save();
    internal void Add(string path, string comment = "");
    internal void SetComment(string path, string comment);
    internal void RelocateSubtree(string oldPath, string newPath);
    internal void Delete(string path, bool includeDescendants);
    internal bool TryExecute(System.Action command, out System.Exception? error);
}
```

`New`는 빈 session을 validation/save한 뒤에만 path/session을 교체하고, `Open`/승인된 `Reload`는 file adapter가 완전히 읽은 session으로 한 번에 교체한다. mutation은 session 호출이 성공한 뒤에만 dirty/selection을 갱신한다. `Save` 성공 뒤에만 dirty를 지우며 `Reload(false)`는 dirty session을 그대로 두고 false다. `TryExecute`는 command 예외를 반환하되 session/controller 상태를 더 바꾸지 않는다. Window는 모든 toolbar/detail command를 이 controller로만 보내고 false/error를 각각 discard dialog 취소와 validation window 표시로 매핑한다.

- [ ] **Step 5: IMGUI TreeView와 validation 창을 구현한다**

`GameplayTagTreeView : UnityEditor.IMGUI.Controls.TreeView`는 model row의 `Index`를 stable integer id로 쓰고 parent index로 `TreeViewItem`을 연결한다. internal static `CreateLabelContent(row)`은 leaf segment와 comment로 `new GUIContent(segment, row.Comment)`를 만들고 `RowGUI`가 이를 그대로 그려 test와 production rendering이 같은 tooltip 경로를 사용한다. `SearchField` 값 변경 시 `Reload()`한다.

`GameplayTagValidationWindow`는 다음 API로 열고 오류를 한 행씩 표시한다.

```csharp
internal static void Show(string filePath, System.Exception error);
internal static string FormatDiagnostic(string filePath, System.Exception error);
```

오류가 `TagCatalogException`이면 `JsonPath`, `LineNumber`, `LinePosition`, `Message`를 모두 표시한다. 그 외 IO 예외는 file path와 message를 표시한다. stack trace는 접을 수 있는 상세 영역에만 둔다.

- [ ] **Step 6: 작성 workflow를 가진 `GameplayTagCatalogWindow`를 구현한다**

```csharp
[UnityEditor.MenuItem("Bun3/Gameplay Tags")]
public static void OpenWindow()
{
    var window = GetWindow<GameplayTagCatalogWindow>();
    window.Show();
}

private void OnEnable()
{
    titleContent = new UnityEngine.GUIContent("Gameplay Tags");
    minSize = new UnityEngine.Vector2(640f, 420f);
    EnsureTreeViewState();
}
```

window는 다음 영역을 갖는다.

1. toolbar: JSON file path, `New`, `Open`, `Reload`, `Save`, search.
2. left: `GameplayTagTreeView`.
3. right detail: selected full path, comment, `Add Root`, `Add Child`, `Rename/Move`, `Delete`.
4. bottom status: loaded fingerprint 앞 8 bytes, active count, dirty 상태.

`New`는 `EditorUtility.SaveFilePanel`로 `.json` 경로를 받고 `{"schemaVersion":1,"tags":[]}` session을 만든 뒤 file adapter로 첫 저장한다. `Open`은 `EditorUtility.OpenFilePanel`로 `.json`을 고르고 file adapter `Load`를 호출한다. `Reload`는 dirty면 `DisplayDialogComplex`로 discard/cancel을 묻는다. `Save`는 file adapter만 호출한다. Add/rename/move/comment는 edit session 메서드 실행 후 view model을 다시 만든다. delete는 leaf면 한 번, descendant가 있으면 `DisplayDialog`로 subtree 삭제를 명시 확인하고 `Delete(path, true)`를 호출한다. 모든 예외는 validation window로 보내고 기존 session과 selection을 유지한다.

- [ ] **Step 7: view model과 window tests를 GREEN으로 만들고 전체 Gameplay EditMode를 실행한다**

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogViewModelTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
```

Expected: 검색/ancestor/tooltip, New/Open/Save/Reload, add/comment/rename/delete, dirty 취소와 validation error 보존, window smoke 및 기존 session/file/shared tests 전체 PASS, warning CS 0.

- [ ] **Step 8: Unity 작성 UI를 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay/Editor/Tags `
  common/src/com.bun3.gameplay/Tests/Editor
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 10 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "✨ GameplayTag 트리 에디터 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 10 failed.' }
```

### Task 11: Mono/IL2CPP 성능 검증과 0.5.0 릴리스 게이트를 닫는다

**Files:**
- Create: `common/src/com.bun3.gameplay/Tests/Runtime/Bun3.Gameplay.Runtime.Tests.asmdef`
- Create: `common/src/com.bun3.gameplay/Tests/Runtime/TagPerformanceFixture.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Runtime/TagRuntimePerformanceTests.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Runtime/TagTests.Mono.json`
- Create: `common/src/com.bun3.gameplay/Tests/Runtime/TagTests.IL2CPP.json`
- Create: `common/tests/Bun3.Gameplay.Tests/TagPerformanceBenchmarkTests.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/Assert-TagPerformance.ps1`
- Modify: `common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayUnitySmokeTests.cs`
- Generate by Unity: matching `.meta` files.
- Verify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`, `package.json`, `unity/Packages/packages-lock.json`.

**Interfaces:**
- Produces player test assembly `Bun3.Gameplay.Runtime.Tests`.
- Verifies official Unity Test Framework player target route: `-testPlatform StandaloneWindows64` plus `testSettingsFile` scripting backend. Reference: [Unity Test Framework command-line arguments](https://docs.unity3d.com/Packages/com.unity.test-framework@2.0/manual/reference-command-line.html).
- Uses the official Any Platform package-test boundary: runtime tests reference the runtime package and test assemblies, never `UnityEditor.TestRunner`. Reference: [Unity package runtime test assembly](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-tests.html).
- Hard gates: query checksum, comparison bounds, .NET의 `GC.GetAllocatedBytesForCurrentThread() == 0`, Unity Mono/IL2CPP의 `GC.Alloc` recorder sample block 0, .NET/Unity 결과 parity, warning 0.
- Measurement gate: N/M/D/hit matrix의 단일 조회 및 100,000-query new/legacy 결과와, 별도 Add/Remove·9:1 read/write matrix의 p50·p95·p99를 기록한다. 모든 read-heavy percentile/batch가 legacy보다 느리거나 어떤 workload든 예약 범위 allocation이 생기면 릴리스를 중단한다.

- [ ] **Step 1: runtime player test assembly와 backend settings를 만든다**

`Bun3.Gameplay.Runtime.Tests.asmdef`:

```json
{
  "name": "Bun3.Gameplay.Runtime.Tests",
  "rootNamespace": "Bun3.Gameplay.Runtime.Tests",
  "references": [
    "Bun3.Gameplay",
    "UnityEngine.TestRunner"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

runtime assembly는 Any Platform이므로 Editor-only `UnityEditor.TestRunner`를 참조하지 않는다. NUnit precompiled reference와 `UnityEngine.TestRunner`만으로 player test assembly를 표시한다.

`TagTests.Mono.json`:

```json
{
  "scriptingBackend": "Mono2x"
}
```

`TagTests.IL2CPP.json`:

```json
{
  "scriptingBackend": "IL2CPP"
}
```

- [ ] **Step 2: runtime workload와 무할당 player test를 쓴다**

`TagPerformanceFixture.cs`는 file-scoped namespace를 쓰지 않는 C# 9 공유 소스다. Unity 의존 코드는 `#if UNITY_5_3_OR_NEWER` 안에만 두어 .NET link compile에는 나타나지 않게 한다. 정확한 factory는 `TagRuntimeFixture.Create(int catalogSize, int exactKinds, int depth, TagContainerKind containerKind, bool startEmpty = false)` 하나다. fixture는 `WarmUp()`, `WarmUpMutation()`, `RunNewQueries(int iterations, TagQueryKind kind)`, `RunLegacyQueries(int iterations, TagQueryKind kind)`, `RunReservedAddRemoveCycles(int operations)`를 제공한다. static `TagPerformanceFixture`는 `MeasureMatrix(string backendName)`와 `MeasureMutationMatrix(string backendName)`, test-only `LegacyTagQueryBaseline`, private `MeasureAllocation(Action workload)`를 소유한다. 결과 타입은 다음 값을 가진다.

```csharp
internal enum TagContainerKind
{
    TagContainer,
    TagCountContainer
}

internal enum TagQueryKind
{
    ExactHit,
    ParentHit,
    Miss
}

internal enum TagMutationKind
{
    AddRemove,
    ReadWriteMixed
}

internal readonly struct TagPerformanceResult
{
    internal TagPerformanceResult(
        string backend, int catalogSize, int exactKinds, int depth,
        TagContainerKind containerKind, TagQueryKind queryKind,
        long newP50Ticks, long newP95Ticks, long newP99Ticks,
        long legacyP50Ticks, long legacyP95Ticks, long legacyP99Ticks,
        long allocationCount)
    {
        Backend = backend; CatalogSize = catalogSize; ExactKinds = exactKinds; Depth = depth;
        ContainerKind = containerKind; QueryKind = queryKind;
        NewP50Ticks = newP50Ticks; NewP95Ticks = newP95Ticks; NewP99Ticks = newP99Ticks;
        LegacyP50Ticks = legacyP50Ticks; LegacyP95Ticks = legacyP95Ticks;
        LegacyP99Ticks = legacyP99Ticks; AllocationCount = allocationCount;
    }

    internal string Backend { get; }
    internal int CatalogSize { get; }
    internal int ExactKinds { get; }
    internal int Depth { get; }
    internal TagContainerKind ContainerKind { get; }
    internal TagQueryKind QueryKind { get; }
    internal long NewP50Ticks { get; }
    internal long NewP95Ticks { get; }
    internal long NewP99Ticks { get; }
    internal long LegacyP50Ticks { get; }
    internal long LegacyP95Ticks { get; }
    internal long LegacyP99Ticks { get; }
    internal long AllocationCount { get; }

    internal string ToLogLine()
    {
        return $"TAGPERF backend={Backend} N={CatalogSize} M={ExactKinds} D={Depth} " +
            $"container={ContainerKind} kind={QueryKind} " +
            $"new_p50_ticks={NewP50Ticks} new_p95_ticks={NewP95Ticks} new_p99_ticks={NewP99Ticks} " +
            $"legacy_p50_ticks={LegacyP50Ticks} legacy_p95_ticks={LegacyP95Ticks} " +
            $"legacy_p99_ticks={LegacyP99Ticks} alloc_count={AllocationCount}";
    }
}


internal readonly struct TagMutationPerformanceResult
{
    internal TagMutationPerformanceResult(
        string backend, int catalogSize, int exactKinds, int depth,
        TagContainerKind containerKind, TagMutationKind mutationKind,
        long newP50Ticks, long newP95Ticks, long newP99Ticks,
        long legacyP50Ticks, long legacyP95Ticks, long legacyP99Ticks,
        long allocationCount)
    {
        Backend = backend; CatalogSize = catalogSize; ExactKinds = exactKinds; Depth = depth;
        ContainerKind = containerKind; MutationKind = mutationKind;
        NewP50Ticks = newP50Ticks; NewP95Ticks = newP95Ticks; NewP99Ticks = newP99Ticks;
        LegacyP50Ticks = legacyP50Ticks; LegacyP95Ticks = legacyP95Ticks;
        LegacyP99Ticks = legacyP99Ticks; AllocationCount = allocationCount;
    }

    internal string Backend { get; }
    internal int CatalogSize { get; }
    internal int ExactKinds { get; }
    internal int Depth { get; }
    internal TagContainerKind ContainerKind { get; }
    internal TagMutationKind MutationKind { get; }
    internal long NewP50Ticks { get; }
    internal long NewP95Ticks { get; }
    internal long NewP99Ticks { get; }
    internal long LegacyP50Ticks { get; }
    internal long LegacyP95Ticks { get; }
    internal long LegacyP99Ticks { get; }
    internal long AllocationCount { get; }

    internal string ToLogLine()
    {
        return $"TAGMUT backend={Backend} N={CatalogSize} M={ExactKinds} D={Depth} " +
            $"container={ContainerKind} kind={MutationKind} " +
            $"new_p50_ticks={NewP50Ticks} new_p95_ticks={NewP95Ticks} new_p99_ticks={NewP99Ticks} " +
            $"legacy_p50_ticks={LegacyP50Ticks} legacy_p95_ticks={LegacyP95Ticks} " +
            $"legacy_p99_ticks={LegacyP99Ticks} " +
            $"alloc_count={AllocationCount}";
    }
}
```

`Bun3.Gameplay.Tests.csproj`에는 shared workload link를 추가한다.

```xml
<Compile Include="..\..\src\com.bun3.gameplay\Tests\Runtime\TagPerformanceFixture.cs"
         Link="TagPerformanceFixture.cs" />
```

`TagPerformanceBenchmarkTests`는 환경 변수로 release benchmark만 opt-in한다.

```csharp
[Test]
public void DotNet_read_and_mutation_matrices_report_release_metrics()
{
    if (!string.Equals(
            Environment.GetEnvironmentVariable("BUN3_RUN_TAG_BENCHMARKS"),
            "1",
            StringComparison.Ordinal))
        Assert.Ignore("Set BUN3_RUN_TAG_BENCHMARKS=1 for the release performance gate.");

    var readRows = TagPerformanceFixture.MeasureMatrix("DotNet");
    var mutationRows = TagPerformanceFixture.MeasureMutationMatrix("DotNet");
    Assert.That(readRows, Has.Length.EqualTo(144));
    Assert.That(mutationRows, Has.Length.EqualTo(96));
    foreach (var row in readRows)
        TestContext.Out.WriteLine(row.ToLogLine());
    foreach (var row in mutationRows)
        TestContext.Out.WriteLine(row.ToLogLine());
}
```

세 runtime의 결과 판정이 어긋나지 않도록 `Assert-TagPerformance.ps1` 하나를 체크인한다.

```powershell
param(
  [Parameter(Mandatory = $true)][string]$ExpectedBackend,
  [string]$LogPath,
  [string]$ResultPath
)
$ErrorActionPreference = 'Stop'
if (-not $ResultPath -and -not $LogPath) { throw 'ResultPath or LogPath is required.' }
$lines = @()
if ($ResultPath) {
  [xml]$xml = Get-Content -Raw -Encoding UTF8 -LiteralPath $ResultPath
  if ([int]$xml.'test-run'.testcasecount -eq 0 `
    -or $xml.'test-run'.result -ne 'Passed' `
    -or [int]$xml.'test-run'.failed -ne 0) {
    throw "Tag tests failed or discovered zero tests: $ResultPath"
  }
  $lines += @($xml.SelectNodes('//output') | ForEach-Object { $_.InnerText -split "`r?`n" })
}
$lines += @(
  if ($LogPath) {
    Get-Content -Encoding UTF8 -LiteralPath $LogPath
  }
)
$pattern = 'TAGPERF backend=(\S+) N=(\d+) M=(\d+) D=(\d+) ' +
  'container=(TagContainer|TagCountContainer) kind=(ExactHit|ParentHit|Miss) ' +
  'new_p50_ticks=(\d+) new_p95_ticks=(\d+) new_p99_ticks=(\d+) ' +
  'legacy_p50_ticks=(\d+) legacy_p95_ticks=(\d+) legacy_p99_ticks=(\d+) alloc_count=(\d+)$'
$rows = @($lines | Where-Object { $_ -like '*TAGPERF *' } |
  ForEach-Object { [regex]::Match($_, $pattern) })
if ($rows.Count -ne 144 -or @($rows | Where-Object { -not $_.Success }).Count -ne 0) {
  throw "Expected 144 parseable TAGPERF rows, got $($rows.Count)."
}
$readSeen = @{}
$readKindCounts = @{ ExactHit = 0; ParentHit = 0; Miss = 0 }
foreach ($row in $rows) {
  if ($row.Groups[1].Value -ne $ExpectedBackend) {
    throw "Unexpected backend in TAGPERF row: $($row.Value)"
  }
  $n = [int]$row.Groups[2].Value; $m = [int]$row.Groups[3].Value
  $d = [int]$row.Groups[4].Value; $container = $row.Groups[5].Value
  $kind = $row.Groups[6].Value
  if ($n -notin @(5000, 50000) -or $m -notin @(8, 32, 64) `
    -or $d -notin @(1, 4, 8, 16)) { throw "Invalid TAGPERF identity: $($row.Value)" }
  $key = "$n|$m|$d|$container|$kind"
  if ($readSeen.ContainsKey($key)) { throw "Duplicate TAGPERF row: $key" }
  $readSeen[$key] = $true; $readKindCounts[$kind]++
  $new50 = [long]$row.Groups[7].Value; $new95 = [long]$row.Groups[8].Value
  $new99 = [long]$row.Groups[9].Value; $old50 = [long]$row.Groups[10].Value
  $old95 = [long]$row.Groups[11].Value; $old99 = [long]$row.Groups[12].Value
  $allocated = [long]$row.Groups[13].Value
  if ($new50 -gt $new95 -or $new95 -gt $new99 `
    -or $old50 -gt $old95 -or $old95 -gt $old99 `
    -or $new50 -gt $old50 -or $new95 -gt $old95 -or $new99 -gt $old99 `
    -or $allocated -ne 0) {
    throw "GameplayTag performance gate failed: $($row.Value)"
  }
}
if ($readKindCounts.ExactHit -ne 48 -or $readKindCounts.ParentHit -ne 48 `
  -or $readKindCounts.Miss -ne 48) { throw 'TAGPERF matrix is incomplete.' }
$mutationPattern = 'TAGMUT backend=(\S+) N=(\d+) M=(\d+) D=(\d+) ' +
  'container=(TagContainer|TagCountContainer) kind=(AddRemove|ReadWriteMixed) ' +
  'new_p50_ticks=(\d+) new_p95_ticks=(\d+) new_p99_ticks=(\d+) ' +
  'legacy_p50_ticks=(\d+) legacy_p95_ticks=(\d+) legacy_p99_ticks=(\d+) alloc_count=(\d+)$'
$mutationRows = @($lines | Where-Object { $_ -like '*TAGMUT *' } |
  ForEach-Object { [regex]::Match($_, $mutationPattern) })
if ($mutationRows.Count -ne 96 -or @($mutationRows | Where-Object { -not $_.Success }).Count -ne 0) {
  throw "Expected 96 parseable TAGMUT rows, got $($mutationRows.Count)."
}
$mutationSeen = @{}
$mutationKindCounts = @{ AddRemove = 0; ReadWriteMixed = 0 }
foreach ($row in $mutationRows) {
  if ($row.Groups[1].Value -ne $ExpectedBackend) {
    throw "Unexpected backend in TAGMUT row: $($row.Value)"
  }
  $n = [int]$row.Groups[2].Value; $m = [int]$row.Groups[3].Value
  $d = [int]$row.Groups[4].Value; $container = $row.Groups[5].Value
  $kind = $row.Groups[6].Value
  if ($n -notin @(5000, 50000) -or $m -notin @(8, 32, 64) `
    -or $d -notin @(1, 4, 8, 16)) { throw "Invalid TAGMUT identity: $($row.Value)" }
  $key = "$n|$m|$d|$container|$kind"
  if ($mutationSeen.ContainsKey($key)) { throw "Duplicate TAGMUT row: $key" }
  $mutationSeen[$key] = $true; $mutationKindCounts[$kind]++
  $new50 = [long]$row.Groups[7].Value; $new95 = [long]$row.Groups[8].Value
  $new99 = [long]$row.Groups[9].Value; $old50 = [long]$row.Groups[10].Value
  $old95 = [long]$row.Groups[11].Value; $old99 = [long]$row.Groups[12].Value
  $allocated = [long]$row.Groups[13].Value
  if ($new50 -gt $new95 -or $new95 -gt $new99 `
    -or $old50 -gt $old95 -or $old95 -gt $old99 `
    -or $allocated -ne 0 `
    -or ($kind -eq 'ReadWriteMixed' -and `
      ($new50 -gt $old50 -or $new95 -gt $old95 -or $new99 -gt $old99))) {
    throw "GameplayTag mutation performance gate failed: $($row.Value)"
  }
}
if ($mutationKindCounts.AddRemove -ne 48 -or $mutationKindCounts.ReadWriteMixed -ne 48) {
  throw 'TAGMUT matrix is incomplete.'
}
```

`TagRuntimePerformanceTests`는 C# 9 블록 namespace를 쓰고 다음 test를 포함한다.

```csharp
[Test]
public void Read_and_mutation_matrices_report_release_metrics()
{
#if ENABLE_IL2CPP
    const string backend = "IL2CPP";
#else
    const string backend = "Mono";
#endif
    var readRows = TagPerformanceFixture.MeasureMatrix(backend);
    var mutationRows = TagPerformanceFixture.MeasureMutationMatrix(backend);
    Assert.That(readRows, Has.Length.EqualTo(144));
    Assert.That(mutationRows, Has.Length.EqualTo(96));
    foreach (var row in readRows)
        TestContext.Out.WriteLine(row.ToLogLine());
    foreach (var row in mutationRows)
        TestContext.Out.WriteLine(row.ToLogLine());
}

[Test]
public void One_hundred_thousand_hierarchical_queries_allocate_zero()
{
    var fixture = TagRuntimeFixture.Create(
        catalogSize: 50_000,
        exactKinds: 64,
        depth: 16,
        containerKind: TagContainerKind.TagCountContainer);
    fixture.WarmUp();

    var checksum = 0;
    Assert.That(
        () => checksum = fixture.RunNewQueries(100_000, TagQueryKind.ParentHit),
        UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
    Assert.That(checksum, Is.EqualTo(100_000));
}

[Test]
public void Reserved_mutation_cycles_allocate_zero()
{
    var fixture = TagRuntimeFixture.Create(
        catalogSize: 50_000,
        exactKinds: 64,
        depth: 16,
        containerKind: TagContainerKind.TagCountContainer,
        startEmpty: true);
    fixture.WarmUpMutation();
    Assert.That(
        () => fixture.RunReservedAddRemoveCycles(1_000),
        UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
}
```

shared source의 private `MeasureAllocation(Action workload)`는 `#if UNITY_5_3_OR_NEWER`에서 `UnityEngine.Profiling.Recorder.Get("GC.Alloc")`를 warmup/flush하고 current thread로 filter한 뒤 workload 전후 `sampleBlockCount`를 반환한다. .NET 분기에서는 `GC.GetAllocatedBytesForCurrentThread()` 차이를 반환한다. 따라서 `MeasureMatrix`가 각 immutable row를 만들 때 실측 `AllocationCount`를 직접 넣으며 wrapper가 값을 조작하지 않는다. Unity Test Framework의 `AllocatingGCMemoryConstraint`도 같은 recorder 경로를 별도 최대-case test에서 교차 검증하므로 IL2CPP에 구현되지 않은 `GC.GetAllocatedBytesForCurrentThread()`를 호출하지 않는다. 필드 의미는 backend-native allocation count(.NET bytes, Unity GC.Alloc blocks)이며 언제나 측정 결과다. `TagRuntimeFixture`는 시작 시 JSON을 만들고 catalog/tag/query/container를 모두 준비한다. `RunNewQueries` loop 안에서는 문자열, LINQ, delegate, closure, 배열 생성이 없어야 한다.

```csharp
private static long MeasureAllocation(System.Action workload)
{
#if UNITY_5_3_OR_NEWER
    var recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
    recorder.enabled = false;
#if !UNITY_WEBGL
    recorder.FilterToCurrentThread();
#endif
    recorder.enabled = true;
    try { workload(); }
    finally
    {
        recorder.enabled = false;
#if !UNITY_WEBGL
        recorder.CollectFromAllThreads();
#endif
    }
    return recorder.sampleBlockCount;
#else
    var before = System.GC.GetAllocatedBytesForCurrentThread();
    workload();
    return System.GC.GetAllocatedBytesForCurrentThread() - before;
#endif
}
```

각 row의 `Action`은 fixture/query를 준비할 때 한 번 만들고 warmup한 뒤 이 메서드에 재사용한다. delegate/closure 생성은 recorder나 byte counter가 시작되기 전에 끝나야 한다.

- [ ] **Step 3: legacy baseline과 percentile matrix test를 구현한다**

test 전용 `LegacyTagQueryBaseline`은 exact tags를 `Dictionary<ushort, int>`에 보유하고 각 query마다 모든 exact tag를 열거하면서 `catalog.GetParent`로 조상 chain을 걷는다. 현재 삭제한 `TagSet`의 `O(M x D)` 동작을 재현하되 production assembly에는 포함하지 않는다.

matrix는 다음 조합을 모두 돈다.

```text
N = 5,000 / 50,000
M = 8 / 32 / 64
D = 1 / 4 / 8 / 16
ContainerKind = TagContainer / TagCountContainer
QueryKind = ExactHit / ParentHit / Miss
```

별도 mutation matrix는 같은 N/M/D/ContainerKind 48개 조합에 `AddRemove`와 `ReadWriteMixed` 두 workload를 적용해 96행을 만든다.

`TagRuntimeFixture.Create`는 `M`개의 서로 겹치지 않는 depth `D` chain을 만들고 그 leaf를 exact tag로 보유한다. 각 chain은 root `B{m}` 뒤에 `L1`부터 `L{D-1}`까지의 세그먼트를 붙이고, 암시 부모를 포함한 chain node `M*D`개를 뺀 나머지를 root `F{n}`으로 채워 active count를 정확히 `N`으로 맞춘다. ExactHit은 M개 leaf, ParentHit은 M개 chain root, Miss는 M개 미보유 filler를 미리 해석한 배열로 두고 각 workload가 round-robin으로 순회한다.

각 조합은 new와 legacy를 번갈아 3회 작은 batch로 warmup한다. 단일 public-query 지연 분포는 타이머 해상도보다 짧은 합법적 호출이 0 tick으로 뭉개지는 것을 줄이기 위해 `const int LatencyBlockSize = 16`의 독립 query call을 한 측정 block으로 묶는다. 미리 할당한 `long[1_001]`에 각 16-call block의 전체 tick을 기록하고, sample마다 new/legacy 측정 순서를 바꾸며 같은 round-robin query를 사용한다. 배열을 정렬해 index 500/950/990을 p50/p95/p99로 기록한다. parser는 0 tick도 유효한 타이머 결과로 허용하고 단조성·legacy 비교만 검사한다. 이와 별개로 100,000-query batch를 new/legacy 각각 한 번 측정해 checksum과 batch 회귀(`newBatchTicks <= legacyBatchTicks`)를 assert한다. `TagPerformanceResult.ToLogLine()` 형식은 다음으로 고정한다. backend는 .NET test가 `DotNet`, `#if ENABLE_IL2CPP` player가 `IL2CPP`, 그 외 player가 `Mono`를 전달한다. .NET과 Unity player 모두 `TestContext.Out.WriteLine`을 사용하며, Unity Test Framework가 `ITestResult.Output`을 NUnit XML `<output>`에 직렬화하므로 player gate는 XML에서 행을 회수한다.

```text
TAGPERF backend={Backend} N=50000 M=64 D=16 container=TagCountContainer kind=ParentHit new_p50_ticks={newP50Ticks} new_p95_ticks={newP95Ticks} new_p99_ticks={newP99Ticks} legacy_p50_ticks={legacyP50Ticks} legacy_p95_ticks={legacyP95Ticks} legacy_p99_ticks={legacyP99Ticks} alloc_count=0
TAGMUT backend={Backend} N=50000 M=64 D=16 container=TagCountContainer kind=ReadWriteMixed new_p50_ticks={newP50Ticks} new_p95_ticks={newP95Ticks} new_p99_ticks={newP99Ticks} legacy_p50_ticks={legacyP50Ticks} legacy_p95_ticks={legacyP95Ticks} legacy_p99_ticks={legacyP99Ticks} alloc_count=0
```

각 read 조합은 checksum 일치, 100,000-query batch `new <= legacy`, backend-native new allocation count 0을 assert한다. timing은 결과 XML/log에서 세 percentile 모두 `new <= legacy`인지 최종 gate script가 판정한다. 별도 mutation workload는 `CreateContainer(64)`와 `CreateCountContainer(64)`로 용량을 예약한 뒤 미리 해석한 leaf를 사용한다. `AddRemove`는 new와 `Dictionary<ushort,int>` legacy가 같은 0/1 또는 count 증감 결과로 되돌아오는 1,000-operation sample을 각각 101번 측정해 진단 percentile을 남기되 timing 회귀 gate는 걸지 않는다.

`ReadWriteMixed`는 정확히 `9 reads + 1 write`를 한 group으로 하고 sample당 100 group(1,000 operations), 101 samples를 new/legacy 순서를 번갈아 측정한다. write는 같은 pre-resolved leaf의 Remove/Add를 번갈아 최종 상태를 원래대로 되돌리고 두 구현이 같은 checksum/count를 내야 한다. 같은 sequence의 별도 100,000-operation batch도 `new <= legacy`를 fixture 안에서 assert한다. parser는 mixed 48행의 new p50/p95/p99가 각각 legacy 이하인지 판정한다. 모든 mutation 행은 새 컨테이너의 allocation이 .NET bytes 0 / Unity GC.Alloc block 0이어야 하며 percentile은 0을 포함해 단조여야 한다. 이 방식은 기존 96행을 유지하면서 read-heavy 혼합 workload의 실제 legacy 회귀를 실행 가능하게 만든다.

- [ ] **Step 4: Unity smoke에 public wire/catalog 계약을 추가한다**

기존 `GameplayUnitySmokeTests`에 작은 JSON을 `MemoryStream`으로 열어 다음을 assert하는 test를 추가한다.

```csharp
Assert.That(catalog.TryGetByIndex(catalog.GetRequired("State.Dead").Index, out var wire), Is.True);
Assert.That(wire, Is.EqualTo(catalog.GetRequired("state.dead")));
Assert.That(catalog.TryGetByIndex(checked((ushort)(catalog.Count + 1)), out _), Is.False);
```

catalog count가 65,535일 때 `catalog.Count + 1` cast가 overflow하므로 smoke fixture는 2개 노드로 고정한다.

- [ ] **Step 5: Unity import로 Runtime test meta를 생성하고 Gameplay EditMode를 재실행한다**

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Import
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode
```

Expected: Runtime tests/settings를 포함한 모든 새 package asset에 `.meta`, Gameplay EditMode 전체 PASS, warning CS 0.

- [ ] **Step 6: .NET server runtime에서 percentile matrix를 실행한다**

```powershell
$dotnetLog = Join-Path $env:TEMP "bun3-tags-dotnet-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')).log"
$env:BUN3_RUN_TAG_BENCHMARKS = '1'
try {
  dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
    --filter "FullyQualifiedName~TagPerformanceBenchmarkTests" `
    --logger "console;verbosity=detailed" 2>&1 | Tee-Object -LiteralPath $dotnetLog
  if ($LASTEXITCODE -ne 0) { throw 'GameplayTag .NET performance test failed.' }
} finally {
  Remove-Item Env:BUN3_RUN_TAG_BENCHMARKS -ErrorAction SilentlyContinue
}

& 'common/tests/Bun3.Gameplay.Tests/Assert-TagPerformance.ps1' `
  -LogPath $dotnetLog -ExpectedBackend 'DotNet'
```

Expected: `TAGPERF` 144행과 `TAGMUT` 96행, checksum/allocation assertions PASS, read p50/p95/p99가 legacy 이하.

- [ ] **Step 7: Mono player에서 runtime test와 성능 matrix를 실행한다**

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' `
  -Mode Player -Backend Mono
```

공용 runner가 player XML의 nonzero discovery/result와 `<output>`을 검사한다. `TAGPERF`는 144(`2*3*4*2*3`)행, `TAGMUT`는 96(`2*3*4*2*2`)행이어야 한다.

- [ ] **Step 8: 같은 player tests를 IL2CPP로 실행한다**

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' `
  -Mode Player -Backend IL2CPP
```

Expected: IL2CPP player build/run 성공, 144개 `TAGPERF`와 96개 `TAGMUT` 행, 모든 checksum/allocation/timing gate 통과. 컴파일 toolchain 부재는 테스트 실패로 숨기지 말고 환경 차단으로 보고한다.

- [ ] **Step 9: NuGet package와 UPM metadata를 검증한다**

```powershell
$packDir = Join-Path $env:TEMP "bun3-gameplay-pack-$([Guid]::NewGuid().ToString('N'))"
dotnet pack common/src/com.bun3.gameplay/Bun3.Gameplay.csproj -c Release --nologo -o $packDir
if ($LASTEXITCODE -ne 0) { throw 'Gameplay package build failed.' }
$nupkgs = @(Get-ChildItem -LiteralPath $packDir -Filter 'Bun3.Gameplay.0.5.0.nupkg')
if ($nupkgs.Count -ne 1) { throw "Expected one Bun3.Gameplay 0.5.0 nupkg, got $($nupkgs.Count)." }
$nupkg = $nupkgs[0]
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
try {
  $entries = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
  if ($entries.Count -ne 1) { throw "Expected one nuspec, got $($entries.Count)." }
  $entry = $entries[0]
  $reader = [System.IO.StreamReader]::new($entry.Open())
  try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $archive.Dispose() }
$dependency = $nuspec.package.metadata.dependencies.group.dependency |
  Where-Object { $_.id -eq 'Newtonsoft.Json' }
if ($nuspec.package.metadata.version -ne '0.5.0' -or $dependency.version -ne '[13.0.2]') {
  throw 'Gameplay nupkg version or exact Newtonsoft dependency mismatch.'
}
$upm = Get-Content -Raw -Encoding UTF8 -LiteralPath `
  'common/src/com.bun3.gameplay/package.json' | ConvertFrom-Json
if ($upm.version -ne '0.5.0' -or $upm.unity -ne '2022.3' `
  -or $upm.dependencies.'com.unity.nuget.newtonsoft-json' -ne '3.2.2') {
  throw 'Gameplay UPM version, Unity floor, or Newtonsoft dependency mismatch.'
}
$lock = Get-Content -Raw -Encoding UTF8 -LiteralPath `
  'unity/Packages/packages-lock.json' | ConvertFrom-Json
$lockedGameplay = $lock.dependencies.'com.bun3.gameplay'
if ($lockedGameplay.version -ne 'file:../../common/src/com.bun3.gameplay' `
  -or $lockedGameplay.depth -ne 0 `
  -or $lockedGameplay.source -ne 'local' `
  -or $lockedGameplay.dependencies.'com.unity.nuget.newtonsoft-json' -ne '3.2.2') {
  throw 'Gameplay local lock entry or Newtonsoft dependency mismatch.'
}
```

- [ ] **Step 10: 전체 저장소 회귀와 Unity 전체 EditMode를 실행한다**

```powershell
dotnet clean Bun3.sln --nologo
if ($LASTEXITCODE -ne 0) { throw 'Solution clean failed.' }
dotnet build Bun3.sln --nologo -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
dotnet test common/tests/Bun3.Common.Tests/Bun3.Common.Tests.csproj --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Common tests failed.' }
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Gameplay tests failed.' }
dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Server tests failed.' }
git diff --check
if ($LASTEXITCODE -ne 0) { throw 'Working-tree diff check failed.' }
```

전체 EditMode는 다음처럼 assembly 제한 없이 실행한다. 공용 runner가 XML testcasecount/result, log `Run completed`, `warning CS`/`error CS`를 판정한다.

```powershell
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' `
  -Mode EditMode -AllEditMode
```

- [ ] **Step 11: package meta 완전성과 변경 범위를 검사한다**

```powershell
$root = (Resolve-Path 'common/src/com.bun3.gameplay').Path
$missing = Get-ChildItem -LiteralPath $root -Recurse -Force |
  Where-Object { $_.Name -notlike '*.meta' -and -not (Test-Path -LiteralPath ($_.FullName + '.meta')) } |
  Where-Object { $_.FullName -notmatch '[\\/](Library|artifacts)[\\/]' }
if ($missing) { $missing.FullName; throw 'Unity-visible asset/folder meta missing.' }

git status --short
git diff --check
```

Expected: 계획된 gameplay package, gameplay tests, package lock만 변경. Unity가 건드린 `ProjectSettings`가 있으면 diff 원인을 확인하고 원래 의미로 복구한 뒤 Step 5–10을 다시 실행한다.

- [ ] **Step 12: runtime player gate와 릴리스 metadata를 커밋한다**

```powershell
git add -- common/src/com.bun3.gameplay `
  common/tests/Bun3.Gameplay.Tests `
  unity/Packages/packages-lock.json
if ($LASTEXITCODE -ne 0) { throw 'Staging Task 11 failed.' }
git diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Staged diff check failed.' }
git diff --cached --stat
if ($LASTEXITCODE -ne 0) { throw 'Staged diff stat failed.' }
git commit -m "✅ GameplayTag 0.5.0 릴리스 검증 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
if ($LASTEXITCODE -ne 0) { throw 'Committing Task 11 failed.' }
```

- [ ] **Step 13: fresh HEAD 검증과 clean worktree를 확인한다**

커밋 이후 fresh HEAD에서 아래 전체 gate를 다시 실행한다.

```powershell
dotnet clean Bun3.sln --nologo
if ($LASTEXITCODE -ne 0) { throw 'Fresh solution clean failed.' }
dotnet build Bun3.sln --nologo -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Fresh solution build failed.' }
dotnet test common/tests/Bun3.Common.Tests/Bun3.Common.Tests.csproj --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Fresh Common tests failed.' }
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Fresh Gameplay tests failed.' }
dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Fresh Server tests failed.' }
$dotnetLog = Join-Path $env:TEMP "bun3-tags-dotnet-fresh-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')).log"
$env:BUN3_RUN_TAG_BENCHMARKS = '1'
try {
  dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --nologo `
    --filter "FullyQualifiedName~TagPerformanceBenchmarkTests" `
    --logger "console;verbosity=detailed" 2>&1 | Tee-Object -LiteralPath $dotnetLog
  if ($LASTEXITCODE -ne 0) { throw 'Fresh .NET performance test failed.' }
} finally {
  Remove-Item Env:BUN3_RUN_TAG_BENCHMARKS -ErrorAction SilentlyContinue
}
& 'common/tests/Bun3.Gameplay.Tests/Assert-TagPerformance.ps1' `
  -LogPath $dotnetLog -ExpectedBackend 'DotNet'
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode EditMode -AllEditMode
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Player -Backend Mono
& 'common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1' -Mode Player -Backend IL2CPP
git diff --check 25cb26c...HEAD
if ($LASTEXITCODE -ne 0) { throw 'Whole GameplayTag branch diff check failed.' }
$status = @(& git status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Fresh worktree status check failed.' }
if ($status.Count -ne 0) { $status; throw 'Fresh worktree is not clean.' }
$commitHash = & git log -1 --format='%H'
if ($LASTEXITCODE -ne 0 -or -not $commitHash) { throw 'Unable to read final commit hash.' }
$commitBodyLines = @(& git log -1 --format='%B')
if ($LASTEXITCODE -ne 0) { throw 'Unable to read final commit body.' }
$commitBody = ($commitBodyLines -join "`n").TrimEnd("`r", "`n")
$expectedTrailer = 'Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>'
if (-not $commitBody.EndsWith("`n$expectedTrailer", [StringComparison]::Ordinal)) {
  throw 'Final commit does not end with the exact Co-Authored-By trailer.'
}
$coAuthorOutput = @(& git log -1 --format='%(trailers:key=Co-Authored-By,valueonly)')
$coAuthorExit = $LASTEXITCODE
$coAuthors = @($coAuthorOutput | Where-Object { $_.Length -ne 0 })
if ($coAuthorExit -ne 0 `
  -or $coAuthors.Count -ne 1 `
  -or $coAuthors[0] -ne 'Claude Fable 5 <noreply@anthropic.com>') {
  throw 'Final commit Co-Authored-By trailer count or value is invalid.'
}
"FINAL_HEAD=$commitHash"
```

Expected: worktree clean, 마지막 커밋 trailer 정확, 모든 required gate PASS. pack/player 임시 산출물은 `$env:TEMP`에만 있어 저장소에 남지 않는다.

## 실행 체크포인트

- Task 1–3 후: JSON 정본이 결정적 `ushort` catalog와 fingerprint로 바뀐다.
- Task 4–6 후: 기존 동적 registry/set가 사라지고 두 bounded container 및 allocation gate가 대체한다.
- Task 7 후: .NET/Unity EditMode semantic parity가 고정된다.
- Task 8–10 후: Unity editor가 공용 validator를 우회하지 않고 같은 JSON만 작성한다.
- Task 11 후: Mono/IL2CPP player, package metadata, full repository regression까지 0.5.0 gate가 닫힌다.

## 완료 정의

- 서버/.NET과 Unity가 같은 fixture에 대해 index, parent, subtree, redirect, fingerprint를 동일하게 만든다.
- peer fingerprint 불일치가 simulation 시작 전에 거부되고 Ability/Effect/장비의 마지막 기여 제거까지 subject count가 유지됨을 shared 통합 테스트로 검증한다.
- 태그 이름은 ASCII 영숫자 세그먼트만 받고 대소문자를 무시하며, 255자/16단계/65,535개 경계가 테스트된다.
- `TagRegistry`, `GetOrRegister`, `TagSet`, public `Handle`이 production API에 남지 않는다.
- `TagContainer`와 `TagCountContainer`가 Unreal 방향의 exact/hierarchical/Any/All 의미와 빈 query 의미를 지킨다.
- 64종/overflow/invalid mutation 실패가 원자적이고 `TagCountContainer.Remove`는 실제 제거량을 반환한다.
- 단일 조회는 7/11 비교 상한, 100,000-query와 9:1 mixed workload의 steady-state allocation 0 및 legacy 비회귀를 .NET/Mono/IL2CPP에서 검증한다.
- Unity UI가 검색 트리, add, comment, rename/move, leaf/subtree delete, reload, validation diagnostics를 제공한다.
- NuGet/UPM은 0.5.0이고 Newtonsoft versions가 exact pin되며 Unity 2022.3 하한을 유지한다.
- `dotnet build` warning/error 0, Common/Gameplay/Server tests PASS, Unity 전체 EditMode 및 Mono/IL2CPP player tests PASS, `git diff --check` PASS, worktree clean이다.
