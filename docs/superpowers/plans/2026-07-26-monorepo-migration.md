# bun3 모노레포 전환 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기존 `Bun3/unity` 레포를 단일 git 모노레포(`bun3-workspace`)로 승격하고, 공용 라이브러리 `com.bun3.common`(UPM+NuGet 이중 포장)과 서버 라이브러리 뼈대 `Bun3.Server.Core`를 신설하며, Unity 패키지를 새 네이밍 규약으로 풀 리네임한다.

**Architecture:** 스펙 `docs/superpowers/specs/2026-07-26-monorepo-structure-design.md` 참조. 레포 루트에 `Bun3.sln`(dotnet+server, unity 제외), `dotnet/src/com.bun3.common`은 한 폴더가 UPM 패키지(package.json+asmdef)이자 NuGet 소스(csproj)이며 unity가 `file:` 상대경로로 참조한다. 서버 재사용은 fork가 아닌 모듈 라이브러리.

**Tech Stack:** Unity 6000.3.14f1, .NET SDK 10.0.103 (netstandard2.1 타겟), git, gh CLI (Bun3 계정 인증됨).

## Global Constraints

- 이 계획은 **이동·리네임·뼈대 생성만** 한다. 기능 변경/리팩터링 금지 (스펙 10장).
- `com.bun3.common` 코드: **netstandard2.1 호환, LangVersion 9.0, UnityEngine/UnityEditor/UniTask 의존 금지**.
- 네이밍 정렬 원칙: **네임스페이스 = asmdef 이름 = 어셈블리 이름 = UPM 패키지명(com. 제거, PascalCase)** (스펙 7장).
- `Directory.Build.props`는 **레포 루트에 두지 않는다**. `dotnet/`, `server/` 폴더에만 (스펙 5장).
- Unity 에셋/스크립트 이동 시 **`.meta` 파일을 반드시 동반 이동** (GUID 보존). `.meta`를 삭제·재생성하지 않는다.
- Unity가 새로 생성한 `.meta`는 커밋한다.
- 커밋 메시지는 기존 히스토리 스타일(gitmoji 접두: ♻️ 이동/리네임, ✨ 신규, 📝 문서)을 따르고 끝에 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`를 붙인다.
- 레포 루트 = `E:\Projects\unity` (로컬 폴더명은 바꾸지 않는다. Task 1 이후 Unity 프로젝트 경로는 `E:\Projects\unity\unity`).
- 태그 생성, NuGet publish, template 레포 생성은 하지 않는다 (스펙 10장 비범위).
- **Unity 검증 방법** (여러 Task에서 반복 사용, 이하 "Unity 컴파일 검증"이라 칭함):
  1. Unity 에디터가 열려 있으면 먼저 닫아달라고 사용자에게 요청한다 (batchmode는 프로젝트 잠금 충돌).
  2. 에디터 경로 탐색: `Get-Content "$env:APPDATA\UnityHub\editors-v2.json"`에서 `6000.3.14f1`의 `location`을 찾는다. 없으면 `C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe` 확인.
  3. 실행: `& "<Unity.exe>" -batchmode -quit -projectPath "E:\Projects\unity\unity" -logFile "$env:TEMP\unity-compile.log"` 후 로그에서 `error CS` 검색. 없으면 통과.
  4. 에디터 경로를 못 찾으면: 사용자에게 "Unity로 `unity/` 프로젝트를 한 번 열어 콘솔 에러 없음을 확인해달라"고 요청하고 응답을 기다린다.

---

### Task 1: 레포 승격 — Unity 프로젝트를 unity/ 하위로 이동

**Files:**
- Move (git): `Assets/`, `Packages/`, `ProjectSettings/`, `.idea/`, `.gitignore` → `unity/` 하위로
- Modify: `.gitattributes` (루트 유지, 경로 규칙 2줄 수정)
- Move (untracked, 물리 이동): `Library/`, `UserSettings/`, `Logs/` → `unity/` 하위로
- Delete (untracked 생성물): `Temp/`, `obj/`, `unity.sln`, `unity.sln.DotSettings.user`, 루트의 모든 `*.csproj`

**Interfaces:**
- Consumes: 없음 (첫 태스크)
- Produces: 이후 모든 태스크가 의존하는 경로 구조 — Unity 프로젝트는 `unity/`, 레포 루트는 비워짐. `docs/`와 `.gitattributes`는 루트에 남는다.

**배경지식:** `.gitattributes`의 `[attr]lfs` 같은 매크로 정의는 **최상위 .gitattributes에서만 동작**한다(파일 안 주석에도 명시됨). 그래서 이 파일은 unity/로 옮기지 않고 루트에 남긴다. 확장자 기반 규칙은 레포 전체에 적용되어도 무해하다. 반면 `.gitignore`의 rooted 패턴(`/[Ll]ibrary/` 등)은 파일이 있는 디렉터리 기준이므로 unity/로 옮기면 그대로 동작한다.

- [ ] **Step 1: 작업 전 상태 확인**

```bash
cd "E:/Projects/unity"
git status --porcelain   # 깨끗해야 함. 아니면 사용자에게 확인
git log --oneline -1     # 시작 커밋 기록해두기
```

- [ ] **Step 2: 추적 파일 git mv**

```bash
cd "E:/Projects/unity"
mkdir unity
git mv Assets Packages ProjectSettings .idea .gitignore unity/
git status --porcelain | head   # 전부 R(rename)이어야 함
```

- [ ] **Step 3: .gitattributes 경로 규칙 수정 (루트 유지)**

`.gitattributes`에서 아래 2줄만 수정:

```
Assets/Plugins/**           linguist-generated
Packages/packages-lock.json linguist-generated
```
→
```
unity/Assets/Plugins/**           linguist-generated
unity/Packages/packages-lock.json linguist-generated
```

- [ ] **Step 4: 미추적 산출물 물리 이동/삭제**

```powershell
Set-Location "E:\Projects\unity"
Move-Item Library, UserSettings, Logs -Destination unity\    # Library 이동으로 재임포트 방지
Remove-Item -Recurse -Force Temp, obj -ErrorAction SilentlyContinue
Remove-Item -Force unity.sln, unity.sln.DotSettings.user -ErrorAction SilentlyContinue
Remove-Item -Force *.csproj    # Unity가 새 경로에서 재생성함
```

`.superpowers/`, `.claude/`, `docs/`는 루트에 그대로 둔다.

- [ ] **Step 5: 히스토리 보존 검증**

```bash
cd "E:/Projects/unity"
git log --follow --oneline -- unity/Packages/com.bun3.core/package.json | tail -3
```
Expected: 과거 커밋들이 그대로 보임 (rename 추적).

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "♻️ Promote repo to monorepo: move Unity project into unity/

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Unity 컴파일 검증** (Global Constraints의 절차. 새 경로 `unity/`에서 정상 컴파일 확인. Unity가 `unity/` 안에 unity.sln/csproj를 재생성하는데, 이는 unity/.gitignore가 무시하므로 git status는 깨끗해야 함)

---

### Task 2: 루트 스캐폴딩 + GitHub 레포 리네임

**Files:**
- Create: `README.md` (레포 루트)
- Create: `.gitignore` (레포 루트, .NET용)

**Interfaces:**
- Consumes: Task 1의 폴더 구조
- Produces: 루트 `.gitignore` (dotnet 산출물 무시 — Task 3, 6이 의존), 리네임된 GitHub 레포 `Bun3/bun3-workspace`

- [ ] **Step 1: 루트 .gitignore 작성**

```gitignore
# .NET build outputs (dotnet/, server/ — artifacts layout)
artifacts/
bin/
obj/
*.user
.vs/
/.idea/
```

주의: `unity/` 쪽은 자체 `.gitignore`가 처리하므로 여기에 Unity 규칙을 넣지 않는다. `/.idea/`는 루트 한정(슬래시 접두) — `unity/.idea`는 추적 유지.

- [ ] **Step 2: 루트 README.md 작성**

```markdown
# bun3-workspace

Bun3의 개인 프레임워크 모노레포.

| 폴더 | 내용 |
|---|---|
| `dotnet/` | 전 플랫폼 공용 .NET 라이브러리 (`com.bun3.common` = UPM+NuGet 이중 포장) |
| `server/` | 서버 재사용 모듈 라이브러리 (`Bun3.Server.*`, NuGet 배포) |
| `unity/` | Unity 패키지 개발 프로젝트 (`com.bun3.unity.*`) |

- 설계 문서: `docs/superpowers/specs/2026-07-26-monorepo-structure-design.md`
- 솔루션: 루트 `Bun3.sln`(dotnet+server). `unity/unity.sln`은 Unity 자동 생성물.
- 외부 소비: 서버/닷넷은 NuGet, Unity는 `?path=` UPM git URL. 서버 앱 시작점은
  별도 `bun3-server-template` 레포(추후).
```

- [ ] **Step 3: 커밋**

```bash
cd "E:/Projects/unity"
git add README.md .gitignore
git commit -m "✨ Add workspace root README and .NET gitignore

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: GitHub 레포 리네임 (사용자 확인 필수)**

외부에 영향 있는 작업이므로 **사용자에게 레포명 `bun3-workspace` 확정을 확인받은 후** 실행:

```bash
cd "E:/Projects/unity"
gh repo rename bun3-workspace --yes   # gh가 origin remote URL도 자동 갱신
git remote -v                         # https://github.com/Bun3/bun3-workspace.git 확인
```

- [ ] **Step 5: push 및 확인**

```bash
git push origin main
```
Expected: 성공. (구 URL은 GitHub이 리다이렉트하지만, UPM `?path=` 소비처가 생기면 새 URL 기준으로 안내할 것)

---

### Task 3: com.bun3.common 뼈대 + 루트 Bun3.sln

**Files:**
- Create: `dotnet/Directory.Build.props`
- Create: `dotnet/src/com.bun3.common/package.json`
- Create: `dotnet/src/com.bun3.common/Bun3.Common.asmdef`
- Create: `dotnet/src/com.bun3.common/Bun3.Common.csproj`
- Create: `dotnet/src/com.bun3.common/Runtime/Bun3CommonInfo.cs`
- Create: `Bun3.sln` (레포 루트)
- Modify: `unity/Packages/manifest.json` (file: 의존성 추가)

**Interfaces:**
- Consumes: Task 1 경로 구조, Task 2 루트 .gitignore
- Produces: `Bun3.Common` 어셈블리 (네임스페이스 `Bun3.Common`) — Task 5가 코드 이주 대상으로, Task 6이 `<ProjectReference Include="..\..\..\dotnet\src\com.bun3.common\Bun3.Common.csproj" />`로 참조. 루트 `Bun3.sln` — Task 6이 프로젝트 추가.

**배경지식:** `UseArtifactsOutput=true`를 `dotnet/Directory.Build.props`에 두면 bin/obj가 패키지 폴더 안이 아니라 `dotnet/artifacts/`에 생성된다. 이게 없으면 dotnet 빌드 산출물(dll)이 UPM 패키지 폴더 안에 생겨 Unity가 dll을 임포트해 **타입 중복 에러**가 난다. 반드시 csproj가 아닌 Directory.Build.props에 있어야 동작한다.

- [ ] **Step 1: dotnet/Directory.Build.props 작성**

```xml
<Project>
  <PropertyGroup>
    <UseArtifactsOutput>true</UseArtifactsOutput>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: 패키지 3종 파일 작성**

`dotnet/src/com.bun3.common/package.json`:
```json
{
  "name": "com.bun3.common",
  "displayName": "Bun3 Common",
  "version": "0.1.0",
  "unity": "6000.3",
  "description": "Platform-agnostic shared library for Bun3 packages. netstandard2.1; no UnityEngine dependency.",
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  }
}
```

`dotnet/src/com.bun3.common/Bun3.Common.asmdef`:
```json
{
    "name": "Bun3.Common",
    "rootNamespace": "Bun3.Common",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`dotnet/src/com.bun3.common/Bun3.Common.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <RootNamespace>Bun3.Common</RootNamespace>
    <AssemblyName>Bun3.Common</AssemblyName>
    <PackageId>Bun3.Common</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-workspace</RepositoryUrl>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: 최초 소스 파일 작성** (빈 asmdef은 Unity가 "no scripts" 경고를 내므로 실제 상수 1개를 둔다)

`dotnet/src/com.bun3.common/Runtime/Bun3CommonInfo.cs`:
```csharp
namespace Bun3.Common
{
    /// <summary>Identity constants for the com.bun3.common package.</summary>
    public static class Bun3CommonInfo
    {
        public const string PackageName = "com.bun3.common";
    }
}
```

- [ ] **Step 4: 루트 솔루션 생성 및 빌드 검증**

```bash
cd "E:/Projects/unity"
dotnet new sln --name Bun3
dotnet sln Bun3.sln add dotnet/src/com.bun3.common/Bun3.Common.csproj
dotnet build Bun3.sln
```
Expected: Build succeeded, 산출물은 `dotnet/artifacts/` 아래 (패키지 폴더 안에 bin/obj **없어야 함** — `ls dotnet/src/com.bun3.common`로 확인).

- [ ] **Step 5: unity manifest에 file: 의존성 추가**

`unity/Packages/manifest.json`의 `"dependencies"` 객체에 한 줄 추가:
```json
    "com.bun3.common": "file:../../dotnet/src/com.bun3.common",
```
(manifest.json의 `file:` 경로는 `unity/Packages/` 폴더 기준 상대경로 → `../../`는 레포 루트)

- [ ] **Step 6: Unity 컴파일 검증** (Global Constraints 절차). 통과 후 Unity가 `dotnet/src/com.bun3.common/` 안에 생성한 `.meta` 파일들과 `unity/Packages/packages-lock.json` 변경을 확인한다.

- [ ] **Step 7: 커밋** (생성된 .meta 포함)

```bash
cd "E:/Projects/unity"
git add Bun3.sln dotnet/ unity/Packages/manifest.json unity/Packages/packages-lock.json
git commit -m "✨ Add com.bun3.common (UPM+NuGet dual package) and root Bun3.sln

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Unity 패키지 풀 리네임

**Files:**
- Delete: `unity/Packages/com.bun3.core/Runtime/RuntimeExample.cs`(+.meta), `unity/Packages/com.bun3.core/Samples/Example/`(폴더+.meta) — 템플릿 잔재 (쓰레기 네임스페이스 `Unity9a6b65d5b34d8ea74ae5.Unitycore`의 출처)
- Move: `unity/Packages/com.bun3.core/` → `unity/Packages/com.bun3.unity.core/`
- Move: `unity/Packages/com.bun3.ui/` → `unity/Packages/com.bun3.unity.ui/`
- Rename+Modify: asmdef 7개 (아래 매핑표)
- Modify: 두 package.json, 모든 `.cs`의 네임스페이스, `unity/Packages/packages-lock.json`(재생성)

**Interfaces:**
- Consumes: Task 1 경로 구조
- Produces: 어셈블리/네임스페이스 `Bun3.Unity.Core`, `Bun3.Unity.Core.Editor`, `Bun3.Unity.UI`, `Bun3.Unity.UI.Editor`, `Bun3.Unity.UI.Tests`, 샘플 `Bun3.Unity.Core.Samples.UnifiedToggleGroup`, `Bun3.Unity.UI.Samples.ButtonInteractableScope` — Task 5가 이 구조에서 코드를 이주.

**배경지식:** asmdef 간 참조는 전부 `GUID:xxxx` 형식(확인됨)이라 asmdef 파일명/`name` 변경이 참조를 깨지 않는다. `.meta`가 동반 이동하면 GUID가 보존되어 씬/프리팹의 스크립트 참조도 유지된다. 단, `[SerializeReference]`로 직렬화된 managed reference는 **타입을 "네임스페이스+어셈블리명" 문자열로 저장**하므로, 둘 다 바뀌는 이번 리네임에서 `[MovedFrom]` 어트리뷰트가 필요하다.

- [ ] **Step 1: 템플릿 잔재 삭제**

```bash
cd "E:/Projects/unity"
git grep -l "RuntimeExample\|SampleExample" -- unity | grep -v "Example"   # 참조자 없음 확인 (출력 없어야 함)
git rm unity/Packages/com.bun3.core/Runtime/RuntimeExample.cs unity/Packages/com.bun3.core/Runtime/RuntimeExample.cs.meta
git rm -r unity/Packages/com.bun3.core/Samples/Example
git rm unity/Packages/com.bun3.core/Samples/Example.meta 2>/dev/null || true
```

- [ ] **Step 2: 패키지 폴더 리네임**

```bash
git mv unity/Packages/com.bun3.core unity/Packages/com.bun3.unity.core
git mv unity/Packages/com.bun3.ui unity/Packages/com.bun3.unity.ui
```

- [ ] **Step 3: package.json 수정**

`com.bun3.unity.core/package.json`: `"name": "com.bun3.unity.core"`, `"displayName": "Bun3 Unity Core"` (version 0.3.0 유지, 나머지 필드 유지).
`com.bun3.unity.ui/package.json`: `"name": "com.bun3.unity.ui"`, `"displayName": "Bun3 Unity UI"`, dependencies의 `"com.bun3.core": "0.3.0"` → `"com.bun3.unity.core": "0.3.0"` (version 0.2.0 유지).

- [ ] **Step 4: asmdef 리네임 (git mv로 파일명 변경 → 내용의 name/rootNamespace 수정)**

| 현재 파일 (unity/Packages/ 기준) | 새 파일명 | 새 `name`/`rootNamespace` |
|---|---|---|
| `com.bun3.unity.core/Runtime/bun3.core.asmdef` | `Bun3.Unity.Core.asmdef` | `Bun3.Unity.Core` |
| `com.bun3.unity.core/Editor/bun3.core.Editor.asmdef` | `Bun3.Unity.Core.Editor.asmdef` | `Bun3.Unity.Core.Editor` |
| `com.bun3.unity.core/Samples/UnifiedToggleGroup/Bun3.Core.Samples.UnifiedToggleGroup.asmdef` | `Bun3.Unity.Core.Samples.UnifiedToggleGroup.asmdef` | `Bun3.Unity.Core.Samples.UnifiedToggleGroup` |
| `com.bun3.unity.ui/Runtime/bun3.ui.asmdef` | `Bun3.Unity.UI.asmdef` | `Bun3.Unity.UI` |
| `com.bun3.unity.ui/Editor/bun3.ui.Editor.asmdef` | `Bun3.Unity.UI.Editor.asmdef` | `Bun3.Unity.UI.Editor` |
| `com.bun3.unity.ui/Tests/Runtime/bun3.ui.Tests.asmdef` | `Bun3.Unity.UI.Tests.asmdef` | `Bun3.Unity.UI.Tests` |
| `com.bun3.unity.ui/Samples/ButtonInteractableScope/Bun3.UI.Samples.ButtonInteractableScope.asmdef` | `Bun3.Unity.UI.Samples.ButtonInteractableScope.asmdef` | `Bun3.Unity.UI.Samples.ButtonInteractableScope` |

각 파일: `git mv <old>.asmdef <new>.asmdef && git mv <old>.asmdef.meta <new>.asmdef.meta` (**meta 동반 필수**), 그 후 파일 내 `"name"`과 `"rootNamespace"` 필드를 표의 값으로 수정. `rootNamespace`가 없는 파일이면 추가. 참조(`references`)는 GUID라 수정 불필요이나, 혹시 이름 참조가 있는지 확인: `git grep -n '"bun3\.' -- 'unity/**/*.asmdef'` → 출력 없어야 함.

- [ ] **Step 5: 네임스페이스 일괄 치환**

```bash
cd "E:/Projects/unity"
git ls-files 'unity/**/*.cs' | xargs sed -i 's/\bBun3\.Core\b/Bun3.Unity.Core/g; s/\bBun3\.UI\b/Bun3.Unity.UI/g'
# 기존 오기 Bun3.Core.Editor.Editor → (위 치환 후) Bun3.Unity.Core.Editor.Editor를 정정:
git ls-files 'unity/**/*.cs' | xargs sed -i 's/Bun3\.Unity\.Core\.Editor\.Editor/Bun3.Unity.Core.Editor/g'
git grep -nE '\bBun3\.(Core|UI)\b' -- 'unity/**/*.cs'   # 출력 없어야 함
```

- [ ] **Step 6: [MovedFrom] 부여 (SerializeReference 직렬화 데이터 마이그레이션)**

1. 직렬화된 옛 타입 문자열이 남아있는 에셋 확인:
   ```bash
   git grep -ln "Bun3.Core\|bun3.core\|Bun3.UI\|bun3.ui" -- 'unity/**/*.unity' 'unity/**/*.prefab' 'unity/**/*.asset'
   ```
2. `[SerializeReference]` 필드에 담기는 타입 목록 확인:
   ```bash
   git grep -ln "SerializeReference" -- 'unity/**/*.cs'
   ```
3. 2에서 찾은 필드의 원소가 되는 **구체 클래스들**(UnifiedToggle 옵션류)에 어트리뷰트 부여. 예 (`Bun3.Unity.Core.UnifiedToggle`의 옵션 클래스):
   ```csharp
   using UnityEngine.Scripting.APIUpdating;

   [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
   public class SomeToggleOption : IToggleOption
   ```
   규칙: `sourceNamespace` = 치환 전 네임스페이스, `sourceAssembly` = 치환 전 asmdef `name`(소문자, 예: `bun3.core`, `bun3.ui`). 1번에서 에셋 매치가 0건이면 이 단계는 건너뛴다 (직렬화된 참조가 없으므로).

- [ ] **Step 7: Unity 컴파일 검증 + 테스트 실행**

Global Constraints 절차로 컴파일 검증 후, 테스트:
```powershell
& "<Unity.exe>" -batchmode -projectPath "E:\Projects\unity\unity" -runTests -testPlatform PlayMode -testResults "$env:TEMP\bun3-tests.xml" -logFile "$env:TEMP\unity-tests.log"
```
결과 xml에서 `result="Passed"` 확인 (기존 `bun3.ui.Tests` → `Bun3.Unity.UI.Tests`의 테스트 전부). batchmode 불가 시 사용자에게 Test Runner 실행 요청. 또한 Step 6-1에서 에셋 매치가 있었다면, 사용자에게 해당 씬/프리팹을 열어 데이터 유실 없는지 확인 요청.

- [ ] **Step 8: 커밋** (packages-lock.json 재생성분 포함)

```bash
cd "E:/Projects/unity"
git add -A
git commit -m "♻️ Rename packages to com.bun3.unity.* and namespaces to Bun3.Unity.*

- Align namespace = asmdef = assembly = package name (spec §7)
- Remove template leftovers (RuntimeExample, Samples/Example)
- Add [MovedFrom] for SerializeReference migration

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: 공용 코드 선별 이주 (Bun3.Unity.Core → Bun3.Common)

**Files:**
- Inspect: `unity/Packages/com.bun3.unity.core/Runtime/Utils/*.cs` (3파일), `unity/Packages/com.bun3.unity.core/Runtime/Threading/*.cs` (2파일), 기타 Runtime 하위 전부
- Move (조건 충족 파일만): → `dotnet/src/com.bun3.common/Runtime/<영역>/` (+.meta 동반)
- Modify: 이동 파일의 네임스페이스, 호출부 using, `com.bun3.unity.core`의 asmdef(참조 추가)와 package.json(의존성 추가)

**Interfaces:**
- Consumes: Task 3의 `Bun3.Common` 어셈블리/asmdef, Task 4의 리네임된 패키지 구조
- Produces: `Bun3.Common.<영역>` 네임스페이스의 이주된 유틸리티. `Bun3.Unity.Core` asmdef가 `Bun3.Common`을 참조.

**이주 판정 기준 (전부 충족해야 이동):**
1. `using UnityEngine`, `using UnityEditor`, `using Cysharp.Threading.Tasks`(UniTask) 없음 — 파일 내용 기준으로 실제 확인 (using이 없어도 본문에서 Unity 타입을 정규화 이름으로 쓰면 탈락)
2. netstandard2.1에 존재하는 API만 사용
3. 코드 수정 없이 이동+네임스페이스 변경만으로 컴파일 가능 (기능 변경 금지 제약)

참고: `Threading/`의 CancellationScope는 UniTask 의존이 확인되면 잔류가 예상 결과다. **0개 이동도 유효한 결과**이며, 그 경우 판정 근거를 커밋 없이 사용자에게 보고하고 태스크를 종료한다.

- [ ] **Step 1: 후보 판정**

```bash
cd "E:/Projects/unity"
for f in $(git ls-files 'unity/Packages/com.bun3.unity.core/Runtime/**/*.cs'); do
  echo "=== $f"; grep -nE "using Unity|using Cysharp|UnityEngine\.|UnityEditor\." "$f" | head -3
done
```
매치가 없는 파일 = 이주 후보. 각 후보는 파일을 직접 읽어 기준 2·3도 확인한다.

- [ ] **Step 2: 이동 (git mv, meta 동반)**

후보 파일별로 (예: `Utils/SomeUtil.cs`):
```bash
mkdir -p dotnet/src/com.bun3.common/Runtime/Utils
git mv unity/Packages/com.bun3.unity.core/Runtime/Utils/SomeUtil.cs dotnet/src/com.bun3.common/Runtime/Utils/SomeUtil.cs
git mv unity/Packages/com.bun3.unity.core/Runtime/Utils/SomeUtil.cs.meta dotnet/src/com.bun3.common/Runtime/Utils/SomeUtil.cs.meta
```
파일 내 `namespace Bun3.Unity.Core.Utils` → `namespace Bun3.Common.Utils`. 이동한 타입이 `[SerializeReference]`/`[Serializable]`로 에셋에 직렬화되어 있으면 Task 4 Step 6과 동일한 `[MovedFrom(true, sourceNamespace: "Bun3.Unity.Core.Utils", sourceAssembly: "Bun3.Unity.Core")]`을 부여 — 단 `Bun3.Common`은 UnityEngine 참조 금지이므로, **MovedFrom이 필요한 타입은 이주 대상에서 제외**한다(UnityEngine.Scripting 네임스페이스가 UnityEngine 어셈블리 소속이므로).

- [ ] **Step 3: 참조 연결**

`com.bun3.unity.core/Runtime/Bun3.Unity.Core.asmdef`의 `references` 배열에 `"Bun3.Common"` 추가 (기존 GUID 항목들과 이름 항목 혼용 가능). `com.bun3.unity.core/package.json`의 `dependencies`에 `"com.bun3.common": "0.1.0"` 추가. 호출부의 `using Bun3.Unity.Core.Utils;` → `using Bun3.Common.Utils;`로 갱신:
```bash
git grep -ln "Bun3.Unity.Core.Utils" -- 'unity/**/*.cs'   # 이동한 네임스페이스별로 확인 후 수정
```

- [ ] **Step 4: 양쪽 빌드 검증**

```bash
cd "E:/Projects/unity" && dotnet build Bun3.sln
```
Expected: Build succeeded. 이어서 Unity 컴파일 검증 + Task 4 Step 7과 동일한 테스트 실행.

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "♻️ Move platform-agnostic utilities from Bun3.Unity.Core to Bun3.Common

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: server/ 뼈대 (Bun3.Server.Core) + 마무리

**Files:**
- Create: `server/Directory.Build.props`
- Create: `server/src/Bun3.Server.Core/Bun3.Server.Core.csproj`
- Create: `server/src/Bun3.Server.Core/Bun3ServerCoreInfo.cs`
- Modify: `Bun3.sln` (프로젝트 추가)

**Interfaces:**
- Consumes: Task 3의 `Bun3.Common.csproj`와 루트 `Bun3.sln`
- Produces: `Bun3.Server.Core` 어셈블리 (net10.0) — 향후 서버 모듈들(Ticking/Sessions 등, 비범위)의 참조 기준점

- [ ] **Step 1: server/Directory.Build.props 작성** (Task 3 Step 1과 동일 내용)

```xml
<Project>
  <PropertyGroup>
    <UseArtifactsOutput>true</UseArtifactsOutput>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: 프로젝트 생성**

`server/src/Bun3.Server.Core/Bun3.Server.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Bun3.Server.Core</RootNamespace>
    <PackageId>Bun3.Server.Core</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-workspace</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\dotnet\src\com.bun3.common\Bun3.Common.csproj" />
  </ItemGroup>

</Project>
```

`server/src/Bun3.Server.Core/Bun3ServerCoreInfo.cs`:
```csharp
using Bun3.Common;

namespace Bun3.Server.Core;

/// <summary>Identity constants for the Bun3.Server.Core module.</summary>
public static class Bun3ServerCoreInfo
{
    public const string ModuleName = "Bun3.Server.Core";

    /// <summary>Proves the Bun3.Common project reference wires up.</summary>
    public const string CommonPackageName = Bun3CommonInfo.PackageName;
}
```

- [ ] **Step 3: 솔루션 추가 및 빌드 검증**

```bash
cd "E:/Projects/unity"
dotnet sln Bun3.sln add server/src/Bun3.Server.Core/Bun3.Server.Core.csproj
dotnet build Bun3.sln
```
Expected: 두 프로젝트 모두 Build succeeded (ProjectReference로 Bun3.Common 상수 참조가 컴파일됨).

- [ ] **Step 4: 커밋 및 push**

```bash
git add server/ Bun3.sln
git commit -m "✨ Add Bun3.Server.Core skeleton referencing Bun3.Common

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
git push origin main
```

- [ ] **Step 5: 전체 최종 검증**

```bash
cd "E:/Projects/unity"
dotnet build Bun3.sln                                   # 성공
git log --follow --oneline -- unity/Packages/com.bun3.unity.core/package.json | tail -3   # 히스토리 보존
git status --porcelain                                  # 깨끗
```
마지막으로 Unity 컴파일 검증 1회. 모두 통과하면 사용자에게 완료 보고 (스펙 9장 6단계의 완료 조건 충족).
