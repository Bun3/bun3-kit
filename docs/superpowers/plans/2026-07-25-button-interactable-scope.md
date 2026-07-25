# ButtonInteractableScope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 버튼의 `interactable`을 여러 조건의 중첩으로 결정하되, 비활성 사유는 **계산 시점이 아니라 사용자가 그 버튼을 클릭한 시점**에 재생한다.

**Architecture:** 계산과 재생을 분리한다. `ButtonInteractableScope`(ref struct)는 `Update()`마다 생성·소멸하며 조건을 집계하고 `Dispose()`에서 `interactable`을 반영한 뒤 사유를 버튼 GameObject의 `ButtonDisabledClickReceiver`에 위탁한다. Receiver는 `IPointerClickHandler`로 비활성 버튼의 클릭을 받아 `IButtonDisabledHandler.Handle(reason)`을 호출한다. 이 구조는 `Selectable.interactable = false`가 레이캐스트를 막지 않는다는 성질에 기반한다 — `Button.OnPointerClick`만 no-op이 될 뿐, EventSystem은 같은 GameObject의 다른 `IPointerClickHandler` 구현체에 이벤트를 정상 전달한다.

**Tech Stack:** Unity 6000.3.14f1, uGUI(`com.unity.ugui` 2.0.0), Unity Test Framework 1.6.0 (PlayMode), NUnit.

**Spec:** `docs/superpowers/specs/2026-07-25-button-interactable-scope-design.md`

## Global Constraints

- 대상 패키지는 `Packages/com.bun3.ui`. 네임스페이스는 런타임 `Bun3.UI.Buttons`, 테스트 `Bun3.UI.Tests`.
- `Packages/com.bun3.ui/Samples/`는 `~` 없는 폴더라 **항상 컴파일된다.** 런타임 공개 API를 바꾸면 같은 커밋에서 샘플도 함께 고쳐야 컴파일이 통과한다.
- `DisabledReason`은 `DisabledMessage`와 `DisabledAction` 중 하나만 갖는다. 생성자 단위로 상호 배타적이다.
- 사유 선택 규칙: **사유를 동반한 첫 실패**가 이긴다. 사유 없는 실패(`Require(false)`)는 비활성화만 시키고 사유 슬롯을 점유하지 않는다.
- `AddComponent`는 `Application.isPlaying`일 때만 수행한다. 에디터에서 컴포넌트가 씬/프리팹에 저장되는 것을 막는다.
- 클릭 재생 조건 4가지 전부 만족 시에만 `Handle` 호출: 좌클릭 / `!button.IsInteractable()` / 사유 non-empty / 핸들러 non-null.
- Unity 에디터 실행 경로: `E:\Unitys\6000.3.14f1\Editor\Unity.exe`
- 테스트 산출물 경로: `$env:TEMP\bun3-ui-tests\` (프로젝트 트리 밖, 커밋 대상 아님)
- **git add는 항상 명시적 경로로.** 작업 트리에 이 작업과 무관한 변경(`Assets/Scenes/SampleScene.unity`, `ProjectSettings/*`, `Packages/com.bun3.core/Runtime/Utils/`, `Packages/com.bun3.ui/Runtime/PointerHandler/`)이 있다. 경로 없는 `git add -A`와 `git commit -a`를 절대 쓰지 않는다. 삭제까지 스테이징해야 할 때는 `git add -A -- <경로들>`처럼 반드시 pathspec을 붙인다.
- 테스트 파일에서 `using System;`과 `using UnityEngine;`을 함께 쓰면 `Object`가 모호해져 `CS0104`가 난다(`System.Object` vs `UnityEngine.Object`). 둘 다 필요하면 `UnityEngine.Object.DestroyImmediate(...)`처럼 정규화한다.

---

## Unity 배치모드 테스트 실행 방법

모든 Task의 테스트 실행 단계는 아래 절차를 쓴다. **Unity 에디터가 이 프로젝트를 열고 있으면 프로젝트 락 때문에 실패한다.**

**1) 에디터가 떠 있는지 확인 (떠 있으면 종료 후 진행)**

```powershell
Get-Process Unity -ErrorAction SilentlyContinue | Select-Object Id, MainWindowTitle
```

**2) 테스트 실행**

```powershell
New-Item -ItemType Directory -Force "$env:TEMP\bun3-ui-tests" | Out-Null
& "E:\Unitys\6000.3.14f1\Editor\Unity.exe" -runTests -batchmode `
    -projectPath "E:\Projects\unity" `
    -testPlatform PlayMode `
    -testFilter "Bun3.UI.Tests" `
    -testResults "$env:TEMP\bun3-ui-tests\results.xml" `
    -logFile "$env:TEMP\bun3-ui-tests\unity.log"
"exit code: $LASTEXITCODE"
```

종료 코드: `0` = 전체 통과, `2` = 테스트 실패, `3` = 실행 자체 실패(대개 컴파일 에러).

**3) 결과 요약**

```powershell
[xml]$r = Get-Content "$env:TEMP\bun3-ui-tests\results.xml"
"total={0} passed={1} failed={2}" -f $r.'test-run'.total, $r.'test-run'.passed, $r.'test-run'.failed
$r.SelectNodes("//test-case[@result='Failed']") | ForEach-Object { "FAIL: " + $_.fullname; $_.failure.message.'#cdata-section' }
```

**4) 컴파일 에러 확인 (종료 코드 3일 때)**

```powershell
Select-String -Path "$env:TEMP\bun3-ui-tests\unity.log" -Pattern "error CS" | Select-Object -First 30
```

**부수 효과:** 이 배치모드 실행은 새로 추가한 `.cs` / `.asmdef` 파일의 `.meta`도 생성한다. 따라서 **테스트 실행 → git add** 순서를 지켜야 `.meta`가 함께 커밋된다.

---

## 파일 구조

| 경로 | 책임 | 상태 |
|---|---|---|
| `Packages/com.bun3.ui/Runtime/Buttons/DisabledReason.cs` | 비활성 사유 데이터 (message XOR action) | 생성 |
| `Packages/com.bun3.ui/Runtime/Buttons/IButtonDisabledHandler.cs` | 사유 재생 전략 (상태 없음) | 수정 |
| `Packages/com.bun3.ui/Runtime/Buttons/ButtonInteractableScope.cs` | 조건 집계 + `interactable` 반영 + 사유 위탁 | 수정 |
| `Packages/com.bun3.ui/Runtime/Buttons/ButtonDisabledClickReceiver.cs` | 사유 보관 + 비활성 클릭 감지 | 생성 |
| `Packages/com.bun3.ui/Samples/ButtonInteractableScope/ButtonInteractableScopeSample.cs` | 사용 예시 | 수정 |
| `Packages/com.bun3.ui/Tests/Runtime/bun3.ui.Tests.asmdef` | PlayMode 테스트 어셈블리 | 생성 |
| `Packages/com.bun3.ui/Tests/Runtime/ButtonScopeTestFixture.cs` | 테스트 공용 헬퍼 (버튼 생성, 클릭 디스패치, SpyHandler) | 생성 |
| `Packages/com.bun3.ui/Tests/Runtime/DisabledReasonTests.cs` | `DisabledReason` 단위 테스트 | 생성 |
| `Packages/com.bun3.ui/Tests/Runtime/ButtonInteractableScopeTests.cs` | 집계·사유 선택·수명 테스트 | 생성 |
| `Packages/com.bun3.ui/Tests/Runtime/ButtonDisabledClickReceiverTests.cs` | 클릭 게이팅 테스트 | 생성 |

---

## Task 0: 진행 중인 폴더 이동 커밋 (선행 정리)

작업 트리에 `Runtime/ButtonInteractableScope/` → `Runtime/Buttons/` 폴더 이동이 커밋되지 않은 채 남아 있다. 이후 Task의 diff를 읽을 수 있게 먼저 커밋한다.

**Files:**
- Modify: `Packages/com.bun3.ui/Runtime/bun3.ui.asmdef`
- Rename: `Packages/com.bun3.ui/Runtime/ButtonInteractableScope/*` → `Packages/com.bun3.ui/Runtime/Buttons/*`

**Interfaces:**
- Consumes: 없음
- Produces: 없음 (파일 위치만 정리)

- [ ] **Step 1: 이동 대상만 스테이징**

삭제(구 폴더)와 추가(신 폴더)를 함께 스테이징해야 하므로 `-A`를 쓰되, **pathspec을 반드시 붙여** 무관한 변경이 딸려오지 않게 한다.

```powershell
git add -A -- Packages/com.bun3.ui/Runtime/ButtonInteractableScope.meta `
              Packages/com.bun3.ui/Runtime/ButtonInteractableScope `
              Packages/com.bun3.ui/Runtime/Buttons.meta `
              Packages/com.bun3.ui/Runtime/Buttons `
              Packages/com.bun3.ui/Runtime/bun3.ui.asmdef
```

- [ ] **Step 2: 스테이징 내용 확인**

```powershell
git status --short
```

기대: `Packages/com.bun3.ui/Runtime/...` 항목만 스테이징(초록)되어 있고, `Assets/Scenes/SampleScene.unity`, `ProjectSettings/*`, `Packages/com.bun3.core/Runtime/Utils/`, `Packages/com.bun3.ui/Runtime/PointerHandler/`는 **스테이징되지 않은(빨강) 상태로 남아 있어야 한다.** 스테이징됐다면 `git restore --staged <경로>`로 뺀다.

- [ ] **Step 3: 커밋**

```powershell
git commit -m "♻️ Move ButtonInteractableScope into Buttons folder"
```

---

## Task 1: PlayMode 테스트 어셈블리 구축

패키지에 테스트 어셈블리가 하나도 없다. 기능 코드를 건드리기 전에 **테스트 인프라 자체가 초록인지** 독립적으로 확인한다.

**Files:**
- Create: `Packages/com.bun3.ui/Tests/Runtime/bun3.ui.Tests.asmdef`
- Create: `Packages/com.bun3.ui/Tests/Runtime/TestAssemblySmokeTests.cs`
- Modify (조건부): `Packages/manifest.json`

**Interfaces:**
- Consumes: 없음
- Produces: `bun3.ui.Tests` 어셈블리. `bun3.ui`를 참조하며 PlayMode에서 실행된다. 이후 모든 Task의 테스트가 이 어셈블리에 들어간다.

- [ ] **Step 1: 테스트 asmdef 생성**

`Packages/com.bun3.ui/Tests/Runtime/bun3.ui.Tests.asmdef`:

```json
{
    "name": "bun3.ui.Tests",
    "rootNamespace": "Bun3.UI.Tests",
    "references": [
        "bun3.ui",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`includePlatforms`가 비어 있어야 PlayMode 어셈블리가 된다. `Editor`를 넣으면 `Application.isPlaying`이 false가 되어 Receiver 자동 부착 경로를 테스트할 수 없다.

- [ ] **Step 2: 스모크 테스트 작성**

`Packages/com.bun3.ui/Tests/Runtime/TestAssemblySmokeTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace Bun3.UI.Tests
{
    public class TestAssemblySmokeTests
    {
        [Test]
        public void RunsInPlayMode()
        {
            Assert.IsTrue(Application.isPlaying, "PlayMode 어셈블리가 아니다. asmdef의 includePlatforms를 비워야 한다.");
        }

        [Test]
        public void ReferencesRuntimeAssembly()
        {
            Assert.IsNotNull(typeof(Bun3.UI.Buttons.ButtonInteractableScope));
        }
    }
}
```

- [ ] **Step 3: 테스트 실행**

위 "Unity 배치모드 테스트 실행 방법" 1~3단계를 수행한다.

기대: `total=2 passed=2 failed=0`, 종료 코드 `0`.

`total=0`이면 Test Runner가 패키지 테스트를 못 찾은 것이다. Step 4로 간다. 그 외 실패면 Step 4를 건너뛰고 원인을 고친다.

- [ ] **Step 4: (total=0일 때만) manifest에 testables 추가**

`Packages/manifest.json`의 최상위에 `dependencies`와 형제로 추가한다:

```json
  "testables": [
    "com.bun3.ui"
  ]
```

추가 후 Step 3을 다시 실행해 `total=2 passed=2`를 확인한다. Step 3이 이미 통과했다면 이 파일을 건드리지 않는다.

- [ ] **Step 5: 커밋**

```powershell
git add Packages/com.bun3.ui/Tests*
# Step 4를 수행한 경우에만 추가
# git add Packages/manifest.json
git status --short
git commit -m "✅ Add PlayMode test assembly for bun3.ui"
```

`Tests*`는 PowerShell이 `Tests` 폴더와 Unity가 생성한 `Tests.meta`로 확장한다. `Packages/com.bun3.ui`를 통째로 add하면 안 된다 — 무관한 `Runtime/PointerHandler/`가 딸려온다.

`git status --short`에서 `.meta` 파일들이 함께 스테이징됐는지 확인한다. 없으면 Step 3을 다시 실행해 Unity가 생성하게 한다.

---

## Task 2: 타입 재편 — 사유 데이터 분리, 핸들러 단일 메서드화, 재생 지점 제거

`Dispose()`에서 `Handle()`을 호출하던 구조를 걷어낸다. 이 Task가 끝나면 사유는 **아무 데서도 재생되지 않는다.** 재생은 Task 3에서 붙인다.

**Files:**
- Create: `Packages/com.bun3.ui/Runtime/Buttons/DisabledReason.cs`
- Modify: `Packages/com.bun3.ui/Runtime/Buttons/IButtonDisabledHandler.cs` (전체 교체)
- Modify: `Packages/com.bun3.ui/Runtime/Buttons/ButtonInteractableScope.cs` (전체 교체)
- Modify: `Packages/com.bun3.ui/Samples/ButtonInteractableScope/ButtonInteractableScopeSample.cs` (전체 교체)
- Create: `Packages/com.bun3.ui/Tests/Runtime/ButtonScopeTestFixture.cs`
- Create: `Packages/com.bun3.ui/Tests/Runtime/DisabledReasonTests.cs`
- Create: `Packages/com.bun3.ui/Tests/Runtime/ButtonInteractableScopeTests.cs`
- Delete: `Packages/com.bun3.ui/Tests/Runtime/TestAssemblySmokeTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: Task 1의 `bun3.ui.Tests` 어셈블리
- Produces:
  - `Bun3.UI.Buttons.DisabledReason` — `readonly struct`. `string DisabledMessage { get; }`, `Action DisabledAction { get; }`, `bool IsEmpty { get; }`, 생성자 `DisabledReason(string)` / `DisabledReason(Action)`
  - `Bun3.UI.Buttons.IButtonDisabledHandler` — `void Handle(DisabledReason reason)` 단 하나
  - `Bun3.UI.Buttons.ButtonInteractableScope` — `static IButtonDisabledHandler DefaultHandler { get; set; }` (null 대입 시 내부 NullHandler로 복구), 생성자 `(Button, IButtonDisabledHandler = null)`, `void Require(bool, string = null)`, `void Require(bool, Action)`, `void Dispose()`
  - `Bun3.UI.Tests.ButtonScopeTestFixture` — 테스트 기반 클래스. `protected Button NewButton()`, `protected static void Click(Button, PointerEventData.InputButton = Left)`
  - `Bun3.UI.Tests.SpyHandler` — `internal sealed class`. `int CallCount`, `DisabledReason Last`

- [ ] **Step 1: 테스트 공용 픽스처 작성**

`Packages/com.bun3.ui/Tests/Runtime/ButtonScopeTestFixture.cs`:

```csharp
using System.Collections.Generic;
using Bun3.UI.Buttons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.UI.Tests
{
    /// <summary>
    /// 테스트용 버튼 생성/정리와 클릭 디스패치를 제공한다.
    /// </summary>
    public abstract class ButtonScopeTestFixture
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        protected Button NewButton(string name = "TestButton")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Button));
            _spawned.Add(go);
            return go.GetComponent<Button>();
        }

        /// <summary>
        /// EventSystem의 실제 디스패치 경로를 그대로 탄다.
        /// 버튼 GameObject의 모든 IPointerClickHandler 구현체에 전달되므로,
        /// Button과 Receiver가 같은 이벤트를 어떻게 처리하는지 함께 검증할 수 있다.
        /// </summary>
        protected static void Click(
            Button button,
            PointerEventData.InputButton mouseButton = PointerEventData.InputButton.Left)
        {
            var data = new PointerEventData(EventSystem.current) { button = mouseButton };
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        [TearDown]
        public void TearDownFixture()
        {
            foreach (var go in _spawned)
            {
                if (go)
                    Object.DestroyImmediate(go);
            }

            _spawned.Clear();

            // 정적 상태가 테스트 간에 새지 않게 한다. null 대입은 NullHandler로 복구된다.
            ButtonInteractableScope.DefaultHandler = null;
        }
    }

    internal sealed class SpyHandler : IButtonDisabledHandler
    {
        public int CallCount { get; private set; }
        public DisabledReason Last { get; private set; }

        public void Handle(DisabledReason reason)
        {
            CallCount++;
            Last = reason;
        }
    }
}
```

- [ ] **Step 2: 실패하는 테스트 작성 — DisabledReason**

`Packages/com.bun3.ui/Tests/Runtime/DisabledReasonTests.cs`:

```csharp
using System;
using Bun3.UI.Buttons;
using NUnit.Framework;

namespace Bun3.UI.Tests
{
    public class DisabledReasonTests
    {
        [Test]
        public void Default_IsEmpty()
        {
            Assert.IsTrue(default(DisabledReason).IsEmpty);
        }

        [Test]
        public void MessageConstructor_CarriesMessageOnly()
        {
            var reason = new DisabledReason("not enough gold");

            Assert.IsFalse(reason.IsEmpty);
            Assert.AreEqual("not enough gold", reason.DisabledMessage);
            Assert.IsNull(reason.DisabledAction);
        }

        [Test]
        public void ActionConstructor_CarriesActionOnly()
        {
            Action action = () => { };
            var reason = new DisabledReason(action);

            Assert.IsFalse(reason.IsEmpty);
            Assert.AreSame(action, reason.DisabledAction);
            Assert.IsNull(reason.DisabledMessage);
        }

        [Test]
        public void NullMessage_IsEmpty()
        {
            Assert.IsTrue(new DisabledReason((string)null).IsEmpty);
        }

        [Test]
        public void NullAction_IsEmpty()
        {
            Assert.IsTrue(new DisabledReason((Action)null).IsEmpty);
        }
    }
}
```

`new DisabledReason(null)`은 두 생성자 사이에서 모호해 컴파일 에러가 난다. 테스트에서 반드시 캐스팅한다.

- [ ] **Step 3: 실패하는 테스트 작성 — 스코프 집계와 "재생하지 않음"**

`Packages/com.bun3.ui/Tests/Runtime/ButtonInteractableScopeTests.cs`:

```csharp
using Bun3.UI.Buttons;
using NUnit.Framework;

namespace Bun3.UI.Tests
{
    public class ButtonInteractableScopeTests : ButtonScopeTestFixture
    {
        [Test]
        public void AllConditionsMet_ButtonStaysInteractable()
        {
            var button = NewButton();
            button.interactable = false;

            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(true, "never shown");
                scope.Require(true);
            }

            Assert.IsTrue(button.interactable);
        }

        [Test]
        public void AnyFailedCondition_DisablesButton()
        {
            var button = NewButton();

            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(true);
                scope.Require(false, "not enough gold");
                scope.Require(true);
            }

            Assert.IsFalse(button.interactable);
        }

        [Test]
        public void Dispose_DoesNotInvokeHandler()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, "not enough gold");
            }

            Assert.AreEqual(0, handler.CallCount, "사유는 Dispose가 아니라 클릭 시점에만 재생돼야 한다.");
        }

        [Test]
        public void DefaultHandler_NullAssignment_FallsBackToNoOp()
        {
            ButtonInteractableScope.DefaultHandler = null;

            Assert.IsNotNull(ButtonInteractableScope.DefaultHandler);

            var button = NewButton();
            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(false, "not enough gold");
            }

            Assert.IsFalse(button.interactable);
        }
    }
}
```

- [ ] **Step 4: 스모크 테스트 삭제**

```powershell
Remove-Item Packages/com.bun3.ui/Tests/Runtime/TestAssemblySmokeTests.cs
Remove-Item Packages/com.bun3.ui/Tests/Runtime/TestAssemblySmokeTests.cs.meta
```

- [ ] **Step 5: 테스트 실행해서 실패 확인**

"Unity 배치모드 테스트 실행 방법" 1~4단계 수행.

기대: 종료 코드 `3`. 로그에 `error CS0117` 또는 `error CS1061` 계열 — `DisabledReason` 타입 없음, `IButtonDisabledHandler.Handle` 없음. 컴파일이 안 되므로 테스트가 실행되지 않는 것이 정상이다.

- [ ] **Step 6: DisabledReason 구현**

`Packages/com.bun3.ui/Runtime/Buttons/DisabledReason.cs` (신규):

```csharp
using System;

namespace Bun3.UI.Buttons
{
    /// <summary>
    /// 버튼이 비활성화된 사유. 메시지 또는 동작 중 하나만 갖는다.
    /// </summary>
    public readonly struct DisabledReason
    {
        /// <summary>표시할 메시지. <see cref="DisabledAction"/>이 있으면 null이다.</summary>
        public string DisabledMessage { get; }

        /// <summary>실행할 동작. <see cref="DisabledMessage"/>가 있으면 null이다.</summary>
        public Action DisabledAction { get; }

        /// <summary>전달할 사유가 없으면 true. 이 경우 핸들러는 호출되지 않는다.</summary>
        public bool IsEmpty => DisabledMessage == null && DisabledAction == null;

        public DisabledReason(string disabledMessage)
        {
            DisabledMessage = disabledMessage;
            DisabledAction = null;
        }

        public DisabledReason(Action disabledAction)
        {
            DisabledMessage = null;
            DisabledAction = disabledAction;
        }
    }
}
```

- [ ] **Step 7: IButtonDisabledHandler 축소**

`Packages/com.bun3.ui/Runtime/Buttons/IButtonDisabledHandler.cs` 전체를 교체:

```csharp
namespace Bun3.UI.Buttons
{
    /// <summary>
    /// 비활성 버튼이 클릭됐을 때 사유를 재생하는 전략.
    /// </summary>
    /// <remarks>
    /// 구현체는 상태를 갖지 않아야 한다. 여러 버튼이 하나의
    /// <see cref="ButtonInteractableScope.DefaultHandler"/>를 공유하기 때문이다.
    /// </remarks>
    public interface IButtonDisabledHandler
    {
        /// <summary>사유 하나를 재생한다. 비어 있지 않은 사유만 전달된다.</summary>
        void Handle(DisabledReason reason);
    }
}
```

- [ ] **Step 8: ButtonInteractableScope 재작성**

`Packages/com.bun3.ui/Runtime/Buttons/ButtonInteractableScope.cs` 전체를 교체:

```csharp
using System;
using UnityEngine.UI;

namespace Bun3.UI.Buttons
{
    /// <summary>
    /// 여러 조건을 모아 버튼의 <see cref="Selectable.interactable"/>을 결정한다.
    /// 조건이 실패하면 사유를 보관해 두었다가, 사용자가 그 버튼을 클릭할 때 재생한다.
    /// </summary>
    public ref struct ButtonInteractableScope
    {
        private sealed class NullHandler : IButtonDisabledHandler
        {
            public static readonly NullHandler Instance = new NullHandler();

            public void Handle(DisabledReason reason) { }
        }

        private static IButtonDisabledHandler _defaultHandler = NullHandler.Instance;

        /// <summary>
        /// 생성자에 핸들러를 주지 않았을 때 쓰이는 핸들러.
        /// null을 대입하면 아무 것도 하지 않는 기본 핸들러로 되돌아간다.
        /// </summary>
        public static IButtonDisabledHandler DefaultHandler
        {
            get => _defaultHandler;
            set => _defaultHandler = value ?? NullHandler.Instance;
        }

        private readonly Button _button;
        private readonly IButtonDisabledHandler _handler;

        private bool _interactable;
        private DisabledReason _reason;
        private bool _disposed;

        public ButtonInteractableScope(Button button, IButtonDisabledHandler handler = null)
        {
            _button = button;
            _handler = handler ?? DefaultHandler;

            _interactable = true;
            _reason = default;
            _disposed = false;
        }

        /// <summary>
        /// 조건을 누적한다. 하나라도 실패하면 버튼은 비활성화된다.
        /// </summary>
        /// <param name="disabledMessage">
        /// 실패 사유 메시지. null이면 사유 없이 조용히 비활성화된다.
        /// </param>
        public void Require(bool condition, string disabledMessage = null)
        {
            _interactable &= condition;

            if (!condition && disabledMessage != null)
                _reason = new DisabledReason(disabledMessage);
        }

        /// <summary>
        /// 조건을 누적한다. 하나라도 실패하면 버튼은 비활성화된다.
        /// </summary>
        /// <param name="disabledAction">
        /// 비활성 버튼이 클릭됐을 때 실행할 동작.
        /// </param>
        /// <remarks>
        /// 매 프레임 호출되는 곳에서 메서드 그룹(<c>Require(cond, OpenPopup)</c>)을 넘기면
        /// 프레임마다 델리게이트가 할당된다. <see cref="Action"/> 필드에 한 번 캐싱해 넘길 것.
        /// </remarks>
        public void Require(bool condition, Action disabledAction)
        {
            _interactable &= condition;

            if (!condition && disabledAction != null)
                _reason = new DisabledReason(disabledAction);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (!_button)
                return;

            _button.interactable = _interactable;
        }
    }
}
```

이 시점에는 `_reason`이 보관만 되고 어디에도 전달되지 않는다. Task 3에서 위탁 경로를 붙인다.

- [ ] **Step 9: 샘플 갱신**

`Packages/com.bun3.ui/Samples/ButtonInteractableScope/ButtonInteractableScopeSample.cs` 전체를 교체:

```csharp
using System;
using Bun3.UI.Buttons;
using UnityEngine;
using UnityEngine.UI;

namespace Bun3.UI.Samples
{
    /// <summary>
    /// <see cref="ButtonInteractableScope"/> 사용 예시.
    /// 여러 조건을 모아 버튼의 interactable을 결정하고,
    /// 비활성 버튼이 클릭되면 사유를 <see cref="IButtonDisabledHandler"/>로 재생한다.
    /// </summary>
    public class ButtonInteractableScopeSample : MonoBehaviour
    {
        [SerializeField] private Button _purchaseButton;
        [SerializeField] private int _gold;
        [SerializeField] private int _itemCount;

        private const int Price = 100;
        private const int RequiredItems = 1;

        // 매 프레임 델리게이트가 할당되지 않도록 한 번만 캐싱한다.
        private Action _openShopHoursPopup;

        private void Awake()
        {
            ButtonInteractableScope.DefaultHandler = new ToastDisabledHandler();
            _openShopHoursPopup = OpenShopHoursPopup;
        }

        private void Update()
        {
            using var scope = new ButtonInteractableScope(_purchaseButton);
            scope.Require(_gold >= Price, "Not enough gold.");
            scope.Require(_itemCount >= RequiredItems, "Not enough materials.");
            scope.Require(IsShopOpen(), _openShopHoursPopup);
        }

        private bool IsShopOpen() => true;

        private void OpenShopHoursPopup() => Debug.Log("[Popup] Shop hours");

        private sealed class ToastDisabledHandler : IButtonDisabledHandler
        {
            public void Handle(DisabledReason reason)
            {
                if (reason.DisabledAction != null)
                    reason.DisabledAction.Invoke();
                else if (reason.DisabledMessage != null)
                    Debug.Log($"[Disabled] {reason.DisabledMessage}");
            }
        }
    }
}
```

- [ ] **Step 10: 테스트 실행해서 통과 확인**

"Unity 배치모드 테스트 실행 방법" 1~3단계 수행.

기대: `total=9 passed=9 failed=0`, 종료 코드 `0`.
(DisabledReasonTests 5개 + ButtonInteractableScopeTests 4개)

- [ ] **Step 11: 커밋**

```powershell
git add Packages/com.bun3.ui/Runtime/Buttons `
        Packages/com.bun3.ui/Samples/ButtonInteractableScope `
        Packages/com.bun3.ui/Tests
git status --short
git commit -m "♻️ Split DisabledReason out and drop Dispose-time replay"
```

---

## Task 3: Receiver 도입 — 클릭 시점 재생

**Files:**
- Create: `Packages/com.bun3.ui/Runtime/Buttons/ButtonDisabledClickReceiver.cs`
- Modify: `Packages/com.bun3.ui/Runtime/Buttons/ButtonInteractableScope.cs` (`Dispose()`만)
- Create: `Packages/com.bun3.ui/Tests/Runtime/ButtonDisabledClickReceiverTests.cs`

**Interfaces:**
- Consumes: Task 2의 `DisabledReason`, `IButtonDisabledHandler`, `ButtonInteractableScope`, `ButtonScopeTestFixture`, `SpyHandler`
- Produces:
  - `Bun3.UI.Buttons.ButtonDisabledClickReceiver` — `public sealed class`, `MonoBehaviour`, `IPointerClickHandler`.
    `internal void Set(Button button, DisabledReason reason, IButtonDisabledHandler handler)`,
    `internal void Clear()`, `public void OnPointerClick(PointerEventData eventData)`
  - `ButtonInteractableScope.Dispose()`가 사유를 Receiver에 위탁한다.

- [ ] **Step 1: 실패하는 테스트 작성**

`Packages/com.bun3.ui/Tests/Runtime/ButtonDisabledClickReceiverTests.cs`:

```csharp
using System;
using Bun3.UI.Buttons;
using NUnit.Framework;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.UI.Tests
{
    public class ButtonDisabledClickReceiverTests : ButtonScopeTestFixture
    {
        private static void Disable(Button button, SpyHandler handler, string message)
        {
            using var scope = new ButtonInteractableScope(button, handler);
            scope.Require(false, message);
        }

        [Test]
        public void ClickWhileDisabled_ReplaysMessageReason()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");

            Click(button);

            Assert.AreEqual(1, handler.CallCount);
            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage);
        }

        [Test]
        public void ClickWhileDisabled_DoesNotInvokeOnClick()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            var clicked = 0;
            button.onClick.AddListener(() => clicked++);

            Disable(button, handler, "not enough gold");
            Click(button);

            Assert.AreEqual(0, clicked, "비활성 버튼의 onClick은 발화하면 안 된다.");
            Assert.AreEqual(1, handler.CallCount);
        }

        [Test]
        public void ClickWhileDisabled_ReplaysActionReason()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            var invoked = 0;
            Action popup = () => invoked++;

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, popup);
            }

            Click(button);

            Assert.AreEqual(1, handler.CallCount);
            Assert.AreSame(popup, handler.Last.DisabledAction);

            // 재생 방식은 핸들러 구현의 책임이다. SpyHandler는 실행하지 않는다.
            Assert.AreEqual(0, invoked);
        }

        [Test]
        public void RightClickWhileDisabled_DoesNotReplay()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");

            Click(button, PointerEventData.InputButton.Right);

            Assert.AreEqual(0, handler.CallCount);
        }

        [Test]
        public void ClickWhileInteractable_DoesNotReplay_AndInvokesOnClick()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            var clicked = 0;
            button.onClick.AddListener(() => clicked++);

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(true, "never shown");
            }

            Click(button);

            Assert.AreEqual(0, handler.CallCount);
            Assert.AreEqual(1, clicked);
        }

        [Test]
        public void AllConditionsMet_NoReceiverIsAdded()
        {
            var button = NewButton();

            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(true, "never shown");
            }

            Assert.IsFalse(button.TryGetComponent(out ButtonDisabledClickReceiver _),
                "비활성화될 일이 없는 버튼에는 컴포넌트가 붙지 않아야 한다.");
        }

        [Test]
        public void ReasonedFailure_AddsReceiver()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");

            Assert.IsTrue(button.TryGetComponent(out ButtonDisabledClickReceiver _));
        }

        [Test]
        public void UnreasonedFailure_DisablesWithoutAddingReceiver()
        {
            var button = NewButton();

            using (var scope = new ButtonInteractableScope(button))
            {
                scope.Require(false);
            }

            Assert.IsFalse(button.interactable);
            Assert.IsFalse(button.TryGetComponent(out ButtonDisabledClickReceiver _));
        }

        [Test]
        public void BecomingInteractableAgain_ClearsStoredReason()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            Disable(button, handler, "not enough gold");

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(true);
            }

            Assert.IsTrue(button.interactable);
            Assert.IsTrue(button.TryGetComponent(out ButtonDisabledClickReceiver _),
                "한 번 붙은 컴포넌트는 제거하지 않는다.");

            // 버튼을 다시 비활성화하되 사유는 주지 않는다.
            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false);
            }

            Click(button);

            Assert.AreEqual(0, handler.CallCount, "이전 프레임의 사유가 남아 있으면 안 된다.");
        }
    }
}
```

- [ ] **Step 2: 테스트 실행해서 실패 확인**

"Unity 배치모드 테스트 실행 방법" 1~4단계 수행.

기대: 종료 코드 `3`. 로그에 `error CS0246` — `ButtonDisabledClickReceiver` 타입을 찾을 수 없음.

- [ ] **Step 3: Receiver 구현**

`Packages/com.bun3.ui/Runtime/Buttons/ButtonDisabledClickReceiver.cs` (신규):

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.UI.Buttons
{
    /// <summary>
    /// <see cref="ButtonInteractableScope"/>가 결정한 비활성 사유를 보관하고,
    /// 비활성 버튼이 클릭되면 재생한다.
    /// </summary>
    /// <remarks>
    /// <see cref="Selectable.interactable"/>이 false여도 레이캐스트는 막히지 않는다.
    /// <see cref="Button.OnPointerClick"/>만 no-op이 될 뿐, EventSystem은 같은
    /// GameObject의 다른 <see cref="IPointerClickHandler"/> 구현체에 이벤트를 전달한다.
    ///
    /// 스코프가 필요할 때 자동으로 붙인다. 직접 추가하거나 조작할 필요는 없다.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ButtonDisabledClickReceiver : MonoBehaviour, IPointerClickHandler
    {
        private Button _button;
        private DisabledReason _reason;
        private IButtonDisabledHandler _handler;

        internal void Set(Button button, DisabledReason reason, IButtonDisabledHandler handler)
        {
            _button = button;
            _reason = reason;
            _handler = handler;
        }

        internal void Clear()
        {
            _reason = default;
            _handler = null;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            if (!_button || _button.IsInteractable())
                return;

            if (_reason.IsEmpty || _handler == null)
                return;

            _handler.Handle(_reason);
        }
    }
}
```

- [ ] **Step 4: Dispose에 위탁 경로 추가**

`ButtonInteractableScope.cs`의 `using` 선언에 `UnityEngine`을 추가한다:

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;
```

`Dispose()` 메서드 전체를 교체한다:

```csharp
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (!_button)
                return;

            _button.interactable = _interactable;

            // 에디터에서 컴포넌트가 씬/프리팹에 저장되는 것을 막는다.
            if (!Application.isPlaying)
                return;

            if (_reason.IsEmpty)
            {
                if (_button.TryGetComponent(out ButtonDisabledClickReceiver existing))
                    existing.Clear();

                return;
            }

            if (!_button.TryGetComponent(out ButtonDisabledClickReceiver receiver))
                receiver = _button.gameObject.AddComponent<ButtonDisabledClickReceiver>();

            receiver.Set(_button, _reason, _handler);
        }
```

- [ ] **Step 5: 테스트 실행해서 통과 확인**

"Unity 배치모드 테스트 실행 방법" 1~3단계 수행.

기대: `total=18 passed=18 failed=0`, 종료 코드 `0`.
(DisabledReason 5 + Scope 4 + Receiver 9)

- [ ] **Step 6: 커밋**

```powershell
git add Packages/com.bun3.ui/Runtime/Buttons Packages/com.bun3.ui/Tests
git status --short
git commit -m "✨ Replay disabled reason on click via ButtonDisabledClickReceiver"
```

---

## Task 4: 사유 선택 규칙과 수명 엣지 케이스

**Files:**
- Modify: `Packages/com.bun3.ui/Runtime/Buttons/ButtonInteractableScope.cs` (`Require` 두 개)
- Modify: `Packages/com.bun3.ui/Tests/Runtime/ButtonInteractableScopeTests.cs` (테스트 추가)

**Interfaces:**
- Consumes: Task 3의 전체 API
- Produces: 공개 시그니처 변경 없음. `Require`의 사유 선택 동작만 확정된다 — 사유를 동반한 첫 실패가 이긴다.

- [ ] **Step 1: 실패하는 테스트 추가**

`ButtonInteractableScopeTests.cs`의 `DefaultHandler_NullAssignment_FallsBackToNoOp` 메서드 **아래**, 클래스 닫는 중괄호 **위**에 아래를 삽입한다. 파일 상단 `using`에 `System`만 추가한다:

```csharp
using System;
using Bun3.UI.Buttons;
using NUnit.Framework;
```

`using UnityEngine;`은 넣지 않는다. `using System;`과 함께 있으면 `Object`가 `System.Object` / `UnityEngine.Object` 사이에서 모호해져 `CS0104`가 난다. 아래 테스트는 `UnityEngine.Object.DestroyImmediate`로 정규화해 호출한다.

추가할 테스트:

```csharp
        [Test]
        public void MultipleReasonedFailures_FirstOneWins()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, "not enough gold");
                scope.Require(false, "not enough materials");
            }

            Click(button);

            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage,
                "선언 순서가 우선순위다.");
        }

        [Test]
        public void UnreasonedFailureFirst_LaterReasonStillWins()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false);
                scope.Require(false, "not enough gold");
                scope.Require(false, "not enough materials");
            }

            Click(button);

            Assert.AreEqual(1, handler.CallCount,
                "사유 없는 실패는 사유 슬롯을 점유하지 않는다.");
            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage);
        }

        [Test]
        public void MessageReasonBeforeActionReason_MessageWins()
        {
            var button = NewButton();
            var handler = new SpyHandler();
            Action popup = () => { };

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, "not enough gold");
                scope.Require(false, popup);
            }

            Click(button);

            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage);
            Assert.IsNull(handler.Last.DisabledAction);
        }

        [Test]
        public void DoubleDispose_IsIdempotent()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            var scope = new ButtonInteractableScope(button, handler);
            scope.Require(false, "not enough gold");
            scope.Dispose();

            button.interactable = true;
            scope.Dispose();

            Assert.IsTrue(button.interactable, "두 번째 Dispose는 아무 것도 하지 않아야 한다.");
        }

        [Test]
        public void TwoScopesOnSameButton_LastDisposeWins()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            using (var first = new ButtonInteractableScope(button, handler))
            {
                first.Require(false, "from first scope");
            }

            using (var second = new ButtonInteractableScope(button, handler))
            {
                second.Require(false, "from second scope");
            }

            Click(button);

            Assert.AreEqual(1, handler.CallCount);
            Assert.AreEqual("from second scope", handler.Last.DisabledMessage);
        }

        [Test]
        public void DestroyedButton_DisposeDoesNotThrow()
        {
            var button = NewButton();
            UnityEngine.Object.DestroyImmediate(button.gameObject);

            Assert.DoesNotThrow(() =>
            {
                using var scope = new ButtonInteractableScope(button);
                scope.Require(false, "not enough gold");
            });
        }

        [Test]
        public void NullButton_DisposeDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                using var scope = new ButtonInteractableScope(null);
                scope.Require(false, "not enough gold");
            });
        }

        [Test]
        public void TwoButtonsSharingDefaultHandler_DoNotCrossContaminate()
        {
            ButtonInteractableScope.DefaultHandler = new SpyHandler();
            var shared = (SpyHandler)ButtonInteractableScope.DefaultHandler;

            var gold = NewButton("GoldButton");
            var level = NewButton("LevelButton");

            using (var scope = new ButtonInteractableScope(gold))
            {
                scope.Require(false, "not enough gold");
            }

            using (var scope = new ButtonInteractableScope(level))
            {
                scope.Require(false, "level too low");
            }

            Click(gold);
            Assert.AreEqual("not enough gold", shared.Last.DisabledMessage);

            Click(level);
            Assert.AreEqual("level too low", shared.Last.DisabledMessage);

            Assert.AreEqual(2, shared.CallCount);
        }
```

- [ ] **Step 2: 테스트 실행해서 실패 확인**

"Unity 배치모드 테스트 실행 방법" 1~3단계 수행.

기대: 종료 코드 `2`. 실패 목록에 아래 3개가 나온다 (나머지는 통과):

- `MultipleReasonedFailures_FirstOneWins` — `"not enough materials"`를 받음 (현재는 나중 사유가 덮어쓴다)
- `UnreasonedFailureFirst_LaterReasonStillWins` — `"not enough materials"`를 받음
- `MessageReasonBeforeActionReason_MessageWins` — `DisabledMessage`가 null이고 `DisabledAction`이 채워져 있음

`SpyHandler.Last`가 `internal`이라 다른 어셈블리에서 안 보인다는 에러가 나면, `SpyHandler`가 테스트 어셈블리 안(`ButtonScopeTestFixture.cs`)에 있는지 확인한다.

- [ ] **Step 3: 사유 선택 규칙 구현**

`ButtonInteractableScope.cs`의 `Require` 두 개에 `_reason.IsEmpty` 가드를 추가한다.

```csharp
        public void Require(bool condition, string disabledMessage = null)
        {
            _interactable &= condition;

            if (!condition && _reason.IsEmpty && disabledMessage != null)
                _reason = new DisabledReason(disabledMessage);
        }
```

```csharp
        public void Require(bool condition, Action disabledAction)
        {
            _interactable &= condition;

            if (!condition && _reason.IsEmpty && disabledAction != null)
                _reason = new DisabledReason(disabledAction);
        }
```

`_interactable &= condition`은 가드 밖에 그대로 둔다. 사유가 확정된 뒤에도 활성 여부 집계는 계속돼야 한다(실제로는 이미 false지만, 규칙을 코드로 명확히 남긴다).

- [ ] **Step 4: XML 문서에 선택 규칙 명시**

`Require(bool, string)`의 `<summary>` 아래에 다음 `<remarks>`를 추가한다:

```csharp
        /// <remarks>
        /// 여러 조건이 함께 실패하면 <b>사유를 동반한 첫 실패</b>가 이긴다.
        /// 선언 순서가 곧 우선순위다. 사유 없이 <c>Require(false)</c>만 호출하면
        /// 버튼은 비활성화되지만 사유 슬롯은 비어 있어, 뒤따르는 조건의 사유가 채택된다.
        /// </remarks>
```

`Require(bool, Action)`의 기존 `<remarks>`(델리게이트 할당 주의)에는 다음 문장을 앞에 덧붙인다:

```csharp
        /// 여러 조건이 함께 실패하면 사유를 동반한 첫 실패가 이긴다.
        /// <br/>
```

- [ ] **Step 5: 테스트 실행해서 통과 확인**

"Unity 배치모드 테스트 실행 방법" 1~3단계 수행.

기대: `total=26 passed=26 failed=0`, 종료 코드 `0`.
(DisabledReason 5 + Scope 12 + Receiver 9)

- [ ] **Step 6: 커밋**

```powershell
git add Packages/com.bun3.ui/Runtime/Buttons Packages/com.bun3.ui/Tests
git status --short
git commit -m "✨ First reasoned failure wins when multiple conditions fail"
```

---

## Task 5: 샘플 씬 수동 검증

자동 테스트는 `ExecuteEvents`로 클릭을 직접 디스패치한다. 실제 EventSystem + GraphicRaycaster 경로(레이캐스트가 비활성 버튼에도 도달하는지)는 씬에서 한 번 확인한다.

**Files:**
- Modify: `Assets/Scenes/SampleScene.unity` (검증용 오브젝트 배치 — **커밋하지 않는다**)

**Interfaces:**
- Consumes: Task 4까지의 전체 구현
- Produces: 없음 (검증 전용)

- [ ] **Step 1: 씬 구성**

Unity 에디터에서 `Assets/Scenes/SampleScene.unity`를 연다.

1. `GameObject > UI > Button - TextMeshPro`로 버튼 생성 (Canvas와 EventSystem이 자동 생성된다)
2. 버튼 GameObject에 `ButtonInteractableScopeSample` 컴포넌트 추가
3. Inspector에서 `Purchase Button`에 자기 자신의 Button을 드래그
4. `Gold = 0`, `Item Count = 0`으로 설정

- [ ] **Step 2: 시나리오 확인**

Play를 누르고 Console을 보며 아래를 순서대로 확인한다.

| # | 조작 | 기대 |
|---|---|---|
| 1 | Play 직후 그냥 대기 (10초) | 버튼이 회색(비활성). **Console에 아무 로그도 안 쌓임** — 매 프레임 Update가 도는데도 사유가 재생되지 않아야 한다 |
| 2 | 버튼 클릭 | `[Disabled] Not enough gold.` 1회 |
| 3 | 버튼 3회 더 클릭 | 로그 3줄 추가 (클릭당 1회) |
| 4 | Inspector에서 `Gold = 100`으로 변경 후 클릭 | `[Disabled] Not enough materials.` — 선언 순서상 다음 사유로 넘어감 |
| 5 | `Item Count = 1`로 변경 | 버튼이 활성 색으로 전환 |
| 6 | 활성 상태에서 클릭 | Console에 `[Disabled]` 로그 없음 |
| 7 | Hierarchy에서 버튼 선택 → Inspector | `Button Disabled Click Receiver` 컴포넌트가 붙어 있음 |
| 8 | Play 정지 후 Hierarchy에서 버튼 선택 | `Button Disabled Click Receiver`가 **없음** (플레이 중에만 추가되므로) |

- [ ] **Step 3: 씬 변경 되돌리기**

`Assets/Scenes/SampleScene.unity`는 이 작업의 산출물이 아니고, 작업 시작 전부터 무관한 수정이 들어 있다. 저장하지 말고 씬을 닫거나, 저장했다면 되돌린다:

```powershell
git status --short Assets/Scenes/SampleScene.unity
```

작업 전 상태(`M`)와 달라졌다면 `git checkout -- Assets/Scenes/SampleScene.unity`는 **쓰지 않는다** — 사용자의 기존 수정까지 날아간다. 대신 에디터에서 추가한 오브젝트만 삭제하고 저장한다.

- [ ] **Step 4: 전체 테스트 최종 확인**

"Unity 배치모드 테스트 실행 방법" 1~3단계 수행.

기대: `total=26 passed=26 failed=0`, 종료 코드 `0`.

- [ ] **Step 5: 계획 문서 커밋**

```powershell
git add docs/superpowers/plans/2026-07-25-button-interactable-scope.md
git commit -m "📝 Add ButtonInteractableScope implementation plan"
```

---

## 최종 상태 확인

- [ ] `Packages/com.bun3.ui/Runtime/Buttons/`에 4개 파일: `DisabledReason.cs`, `IButtonDisabledHandler.cs`, `ButtonInteractableScope.cs`, `ButtonDisabledClickReceiver.cs`
- [ ] `ButtonInteractableScope.cs`에 `Handle` 호출이 없다 (`Select-String -Path Packages/com.bun3.ui/Runtime/Buttons/ButtonInteractableScope.cs -Pattern "Handle"` → 0건)
- [ ] PlayMode 테스트 26개 전부 통과
- [ ] 샘플 씬 시나리오 8개 전부 확인
- [ ] `git status --short`에 이 작업과 무관한 파일(`ProjectSettings/*`, `Packages/com.bun3.core/Runtime/Utils/`, `Packages/com.bun3.ui/Runtime/PointerHandler/`)이 **커밋되지 않은 채** 남아 있다

---

## 부록: 실행 결과 (2026-07-25)

계획을 그대로 실행하지 않았다. 아래는 실제로 일어난 일과 계획이 틀렸던 부분이다.
계획 본문은 예측 시점 그대로 두고, 정정은 여기에만 기록한다.

### 커밋

| Task | 커밋 | 테스트 |
|---|---|---|
| 0 | `e606add` | — |
| 1 | `ab09b43` | 2/2 |
| 2 | `3797300` | 9/9 |
| 3 | `96a380e` | 18/18 |
| 3 (수정) | `7d7332d` | 21/21 |
| 4 | `a46a3ab` | 29/29 |
| 4 (정리) | `2edd9c2` | 28/28 |

최종 테스트 28개. Task 5(샘플 씬 수동 검증)는 수행하지 않았다.

### 계획이 틀렸던 것

1. **배치모드 컴파일 실패의 종료 코드는 1이지 3이 아니다.** Task 2 Step 5와 Task 3 Step 2의
   "기대: 종료 코드 3"은 틀렸다. 3은 실행 자체가 실패했을 때다. 테스트 실패는 2가 맞다.
2. **레드 상태 컴파일 에러 코드도 틀렸다.** 실제로는 `CS0246`(타입 없음)과
   `CS0535`(인터페이스 멤버 미구현)였다. 계획이 적은 `CS0117`/`CS1061`이 아니다.
3. **Task 4의 예상 테스트 수 26개는 틀렸다.** Task 3에 수정 패스가 붙어 21개가 되었고,
   Task 4에서 29개, 중복 제거 후 28개가 되었다.
4. **`testables` 항목은 필요 없었다.** Task 1 Step 4는 실행하지 않았다. `Packages/` 아래
   embedded 패키지의 테스트는 Test Runner가 자동으로 잡는다.
5. **`IsInteractable()` 검증을 빠뜨렸다.** spec §4.3을 쓸 때 "리시버가 이미 사유 유무로
   게이팅하니 `IsInteractable()`과 `.interactable` 필드는 관측상 차이가 없다"고 판단해
   테스트를 넣지 않았다. 틀렸다. 사유를 심어둔 뒤 외부에서 `button.interactable = true`로
   되돌리면 두 경로가 갈라진다. Task 3 리뷰에서 잡혀 수정 패스로 보강했다.
6. **`TwoScopesOnSameButton_LastDisposeWins`는 중복이었다.** Task 3 수정 패스에서 추가한
   `RedisablingWithNewReason_ReusesReceiver_AndReplaysLatestReason`이 엄밀한 상위 집합이라
   `2edd9c2`에서 삭제했다.

### 계획에 없던 발견

- **이 프로젝트는 new Input System을 쓴다.** 테스트용 `EventSystem`에 레거시
  `StandaloneInputModule`을 붙이면 `Input.mousePosition`에서 예외가 난다. `RaycastAll`은
  입력 모듈이 필요 없으므로 붙이지 않는다.
- **Unity 에디터가 프로젝트를 열고 있으면 배치모드 CLI가 전부 실패한다.** 프로젝트 락 때문이며
  우회 플래그는 없다. 로그에 `Application will terminate with return code 1`만 남는다.
- **`CanvasGroup` 부착/변경은 `Selectable`에 동기적으로 반영된다.**
  `SetParent` → `OnTransformParentChanged` → `OnCanvasGroupChanged` → 부모 체인 재순회가
  같은 프레임에 끝나므로, 해당 테스트는 `[UnityTest]` + `yield return null` 없이
  평범한 `[Test]`로 충분하다.

### 남은 Minor (미해결)

- `Application.isPlaying` 가드(`ButtonInteractableScope.Dispose`)는 PlayMode에서 도달 불가라
  커버되지 않는다. EditMode 테스트가 있어야 덮인다.
- `ButtonDisabledClickReceiver`의 `_reason.IsEmpty || _handler == null`은 두 항이 각각
  독립적으로 커버되지 않는다. `Clear()`가 둘을 함께 null로 만들기 때문이다.
- `Clear()`는 `_button`을 남겨두므로 `!_button` 분기가 테스트되지 않는다.
- 레이캐스트 테스트는 히트만 확인하고 디스패치까지 잇지 않는다. `SpyHandler`가 선언만 되고
  쓰이지 않는다.
- `Require(bool, Action)`의 `<remarks>`가 `Require(bool, string)` 쪽보다 설명이 짧다.
- `ButtonDisabledClickReceiverTests.cs`가 226줄이며 단위 테스트와 씬 스캐폴딩 테스트가 섞여 있다.
