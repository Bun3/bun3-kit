# ButtonInteractableScope 설계

- 날짜: 2026-07-25
- 대상 패키지: `com.bun3.ui` (`Bun3.UI.Buttons`)

## 1. 문제

버튼의 `interactable`을 여러 조건의 중첩으로 결정하고, 조건이 실패했을 때 그 사유(토스트 메시지 또는 임의 동작)를 사용자에게 전달하고 싶다.

기존 구현은 `ButtonInteractableScope.Dispose()`에서 `IButtonDisabledHandler.Handle()`을 호출한다. 스코프는 `Update()`에서 매 프레임 생성·소멸하므로, **상태를 계산하는 시점마다** 사유가 재생된다. 실제로 필요한 것은 **비활성화된 버튼을 사용자가 클릭한 시점**에 사유를 재생하는 것이다.

### 기각한 대안

| 대안 | 기각 사유 |
|---|---|
| `Button.onClick`에 사유 재생 콜백을 추가 | `interactable == false`면 `onClick` 자체가 발화하지 않음. 반대로 `interactable`을 항상 `true`로 두고 `onClick` 앞단에서 가로채는 방식은 실제 클릭 핸들러가 실행될 위험이 크다. |
| 외부 컴포넌트에서 `IPointerClickHandler`로 감지 | 감지자가 대상 버튼과 사유 저장소를 모르므로 `interactable` 체크와 사유 조회가 모두 번거롭다. |

### 성립 근거

`Selectable.interactable = false`는 **레이캐스트를 막지 않는다.** `Button.OnPointerClick`이 `IsActive() && IsInteractable()` 검사로 no-op이 될 뿐, EventSystem은 같은 GameObject의 다른 `IPointerClickHandler` 구현 컴포넌트에 이벤트를 정상 전달한다. 따라서 버튼 옆에 붙은 컴포넌트는 비활성 버튼의 클릭을 받을 수 있다.

## 2. 구조

계산 시점과 반응 시점을 서로 다른 객체가 맡는다.

```
[Update() — 매 프레임]                   [클릭 — 사용자 입력 시점]
ButtonInteractableScope (ref struct)     ButtonDisabledClickReceiver (MonoBehaviour)
  Require() x N  → 첫 유효 사유 확정        IPointerClickHandler
  Dispose()      → interactable 반영        if (!button.IsInteractable() && 사유 있음)
                 → Receiver에 사유 위탁 ──→     handler.Handle(reason)
```

- `ButtonInteractableScope`는 프레임마다 사라지는 순수 계산기다. 사유를 재생하지 않는다.
- `ButtonDisabledClickReceiver`는 버튼 GameObject에 상주하는 **사유 보관함 겸 클릭 감지자**다. `IsInteractable()` 검사가 이 컴포넌트 내부에 있으므로 외부 감지 방식의 불편함이 사라진다.

## 3. 타입

### 3.1 `DisabledReason` (readonly struct, 최상위)

기존에는 `ButtonInteractableScope` 내부 중첩 타입이었으나 **최상위로 승격한다.**

- `ButtonDisabledClickReceiver`가 필드로 보관해야 하는데 `ButtonInteractableScope.DisabledReason`은 이름이 장황하다.
- 데이터 타입이 계산기 타입에 종속될 이유가 없다.

```csharp
public readonly struct DisabledReason
{
    public string DisabledMessage { get; }
    public Action DisabledAction { get; }
    public bool IsEmpty => DisabledMessage == null && DisabledAction == null;

    public DisabledReason(string disabledMessage);
    public DisabledReason(Action disabledAction);
}
```

`DisabledMessage`와 `DisabledAction`은 생성자 단위로 상호 배타적이다.

### 3.2 `IButtonDisabledHandler`

단일 메서드로 축소한다. 사유 누적 책임이 스코프로 옮겨갔으므로 수집용 메서드가 필요 없다.

```csharp
public interface IButtonDisabledHandler
{
    void Handle(DisabledReason reason);
}
```

**핸들러는 상태를 갖지 않는다.** 기존 2메서드 형태(`OnDisabled` 수집 + `Handle` 재생)는 구현체가 마지막 사유를 필드에 보관하므로, 여러 버튼이 `DefaultHandler` 하나를 공유하면 버튼 A의 사유가 버튼 B의 것으로 덮어써지는 버그가 발생한다. 단일 메서드는 이 문제를 구조적으로 제거한다.

### 3.3 `ButtonInteractableScope` (ref struct)

```csharp
public ref struct ButtonInteractableScope
{
    public static IButtonDisabledHandler DefaultHandler { get; set; }

    public ButtonInteractableScope(Button button, IButtonDisabledHandler handler = null);

    public void Require(bool condition, string disabledMessage = null);
    public void Require(bool condition, Action disabledAction);

    public void Dispose();
}
```

`DefaultHandler`의 기본값은 아무 것도 하지 않는 `NullHandler` 싱글턴이다. `handler` 인자가 `null`이면 생성 시점의 `DefaultHandler`를 사용한다.

### 3.4 `ButtonDisabledClickReceiver` (MonoBehaviour)

```csharp
[DisallowMultipleComponent]
public sealed class ButtonDisabledClickReceiver : MonoBehaviour, IPointerClickHandler
{
    internal void Set(Button button, DisabledReason reason, IButtonDisabledHandler handler);
    internal void Clear();
    public void OnPointerClick(PointerEventData eventData);
}
```

`Set`/`Clear`는 `internal`이다. 이 컴포넌트는 스코프의 구현 세부사항이며 사용자가 직접 조작할 대상이 아니다.

## 4. 동작 규칙

### 4.1 사유 선택 — 사유를 가진 첫 실패가 이긴다

선언 순서가 곧 우선순위다. 단, **사유를 동반하지 않은 실패는 건너뛴다.**

```csharp
scope.Require(_isLoaded);                     // 실패, 사유 없음 → 조용히 비활성
scope.Require(_gold >= Price, "골드 부족");    // ← 이 사유가 표시된다
scope.Require(_hasItem, "재료 부족");          // 실패해도 무시
```

"첫 실패가 무조건 우선"이면 `Require(false)` 한 줄이 뒤따르는 모든 메시지를 삼켜 원인 파악이 어려워진다. 사유 없는 `Require`는 "조용히 비활성화하되 이유는 말하지 않음"이라는 의도로 해석한다.

구현상 사유가 이미 확정되었으면 이후 `Require`는 `DisabledReason` 생성을 건너뛴다. `_interactable &= condition`은 계속 누적한다.

### 4.2 Receiver 확보 — 사유가 있을 때만, 플레이 중에만

`Dispose()` 시점:

- 사유가 **있으면**: `TryGetComponent` → 없으면 `AddComponent` → `Set(...)`
- 사유가 **없으면**: 기존 Receiver가 있을 때만 `Clear()` 호출. 새로 붙이지 않는다.

비활성화될 일이 없는 버튼에는 컴포넌트가 끝까지 붙지 않는다.

`AddComponent`는 `Application.isPlaying`일 때만 수행한다. 에디터 스크립트나 `OnValidate`에서 스코프를 사용할 경우 컴포넌트가 씬/프리팹에 저장되는 것을 막는다. 플레이 중이 아니면 `Dispose()`는 `interactable` 반영까지만 수행한다.

### 4.3 클릭 처리

`OnPointerClick`에서 다음을 모두 만족할 때만 `handler.Handle(reason)`를 호출한다.

1. `eventData.button == PointerEventData.InputButton.Left`
2. 보관된 `Button`이 살아 있고 `!button.IsInteractable()`
   - `interactable` 필드가 아니라 `IsInteractable()`을 쓴다. 상위 `CanvasGroup`에 의한 비활성까지 반영된다.
3. 보관된 `reason.IsEmpty == false`
4. 보관된 `handler != null`

`DisabledAction`이 있으면 그것을 호출하고, 없으면 `DisabledMessage`를 처리하는 것은 **핸들러 구현체의 책임**이다. 스코프와 Receiver는 관여하지 않는다.

### 4.4 수명

Receiver는 버튼과 같은 GameObject에 있으므로 버튼이 파괴되면 함께 파괴된다. 별도 정리 로직이나 정적 저장소가 필요 없다.

`Dispose()`는 멱등이다(`_disposed` 가드 유지). `_button`이 파괴된 경우 `Dispose()`는 아무것도 하지 않는다.

한 버튼을 여러 스코프가 같은 프레임에 다루면 **마지막 `Dispose()`가 이긴다.** Receiver의 보관 상태는 누적되지 않고 매번 덮어써진다. 한 버튼의 `interactable`은 한 곳에서 결정하는 것이 전제다.

## 5. 호출부

공개 API는 변경되지 않는다. 동작만 "Dispose 시 재생" → "클릭 시 재생"으로 바뀐다.

```csharp
private void Update()
{
    using var scope = new ButtonInteractableScope(_purchaseButton);
    scope.Require(_gold >= Price, "골드가 부족합니다.");
    scope.Require(_itemCount >= RequiredItems, "재료가 부족합니다.");
    scope.Require(IsShopOpen(), _openShopHoursPopup);
}
```

### 할당 주의

`Require(cond, OpenShopHoursPopup)`처럼 **메서드 그룹을 매 프레임 넘기면 프레임마다 델리게이트가 할당된다.** 인자 평가가 `Require` 호출 이전에 끝나므로 스코프 쪽에서 막을 수 없다.

대응:

- 샘플에서 `Action` 필드로 캐싱하는 형태를 보여준다 (`_openShopHoursPopup = OpenShopHoursPopup;`를 `Awake`에서 1회).
- `Require(bool, Action)` 오버로드의 XML 문서에 명시한다.

## 6. 파일 배치

```
Packages/com.bun3.ui/Runtime/Buttons/
  ButtonInteractableScope.cs
  DisabledReason.cs                  (신규)
  IButtonDisabledHandler.cs          (수정)
  ButtonDisabledClickReceiver.cs     (신규)

Packages/com.bun3.ui/Samples/ButtonInteractableScope/
  ButtonInteractableScopeSample.cs   (수정)
```

`bun3.ui.asmdef`는 엔진 참조를 그대로 쓰므로(`noEngineReferences: false`) `UnityEngine.UI` / `UnityEngine.EventSystems` 사용에 추가 참조가 필요 없다. `ButtonInteractableScope.cs`의 미사용 `using UnityEngine.EventSystems;`는 제거한다.

## 7. 검증

Unity 프로젝트이고 이 패키지에 테스트 어셈블리가 없다. 검증은 샘플 씬 수동 확인으로 한다.

| # | 시나리오 | 기대 결과 |
|---|---|---|
| 1 | 모든 조건 충족 | 버튼 활성. Receiver 미부착. 클릭 시 `onClick` 정상 발화. |
| 2 | 골드 부족 | 버튼 비활성. 클릭 시 "골드가 부족합니다." 1회. `onClick` 미발화. |
| 3 | 골드·재료 동시 부족 | "골드가 부족합니다."만 표시 (선언 순서 우선). |
| 4 | 사유 없는 `Require(false)`가 앞에 있고 뒤에 메시지 조건 실패 | 뒤쪽 메시지가 표시됨 (§4.1). |
| 5 | 비활성 상태에서 클릭하지 않고 대기 | 매 프레임 Update가 돌아도 토스트가 뜨지 않음. |
| 6 | 골드 부족 → 골드 충전으로 조건 충족 | 버튼 활성 전환. 클릭 시 사유 미재생, `onClick` 발화. |
| 7 | `Action` 사유(팝업) 조건 실패 후 클릭 | 팝업 1회 오픈. |
| 8 | 여러 버튼이 `DefaultHandler` 공유 | 각 버튼이 자기 사유를 표시 (교차 오염 없음). |
| 9 | 비활성 상태에서 버튼 GameObject 파괴 | 예외 없음. |
