using System;
using System.Collections;
using System.Collections.Generic;
using Bun3.UI.Buttons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
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
        public void AllConditionsMet_NoReceiverIsAdded_AndOnClickFires()
        {
            // 이전에는 이 시나리오(모든 조건 통과)와 "비활성 버튼을 클릭했을 때 재생하지
            // 않는다"는 별개의 테스트로 나뉘어 있었다. 후자는 조건을 항상 통과시켜
            // 리시버 자체가 붙지 않으므로, 클릭 가드(IsInteractable 검사)를 전혀
            // 실행하지 않고도 통과했다 — 이름이 약속하는 커버리지가 없었다.
            // 그 가드는 아래 ClickAfterInteractableRestoredDirectly_DoesNotReplay /
            // ClickWhileParentCanvasGroupBlocksInteraction_... 이 대신 검증한다.
            var button = NewButton();
            var handler = new SpyHandler();
            var clicked = 0;
            button.onClick.AddListener(() => clicked++);

            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(true, "never shown");
            }

            Assert.IsFalse(button.TryGetComponent(out ButtonDisabledClickReceiver _),
                "비활성화될 일이 없는 버튼에는 컴포넌트가 붙지 않아야 한다.");

            Click(button);

            Assert.AreEqual(0, handler.CallCount);
            Assert.AreEqual(1, clicked);
        }

        [Test]
        public void ClickAfterInteractableRestoredDirectly_DoesNotReplay()
        {
            // 리시버가 실제로 존재하고(사유 보관 중) 버튼이 진짜로 상호작용 가능한
            // 상태를 만든다. OnPointerClick의 `IsInteractable()` 가드를 통째로
            // 지워도 기존 테스트는 모두 통과했다 — 이 테스트가 그 뮤테이션을 잡는다.
            var button = NewButton();
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");

            // 스코프를 거치지 않고 raw 필드를 직접 되돌린다.
            // 리시버는 이전 사유를 여전히 들고 있다.
            button.interactable = true;

            Click(button);

            Assert.AreEqual(0, handler.CallCount,
                "버튼이 실제로 상호작용 가능하면, 리시버에 남은 사유가 있어도 재생하면 안 된다.");
        }

        [Test]
        public void ClickWhileParentCanvasGroupBlocksInteraction_ReplaysReason_EvenThoughButtonFieldIsTrue()
        {
            // interactable 필드 자체는 true이지만 부모 CanvasGroup이 상호작용을
            // 막고 있어 IsInteractable()은 false인 상태를 만든다. 이 테스트는
            // `_button.IsInteractable()`을 `_button.interactable`로 바꿔치기하는
            // 뮤테이션에서만 실패한다.
            var groupGo = new GameObject("BlockingGroup", typeof(CanvasGroup));
            Track(groupGo);
            groupGo.GetComponent<CanvasGroup>().interactable = false;

            var button = NewButton("GroupedButton");
            var handler = new SpyHandler();
            Disable(button, handler, "not enough gold");
            button.interactable = true; // raw 필드는 true로 되돌린다.

            // SetParent는 OnTransformParentChanged -> OnCanvasGroupChanged를 동기
            // 호출해 Selectable의 그룹 상호작용 캐시(m_GroupsAllowInteraction)를
            // 즉시 갱신시킨다. 프레임을 기다릴 필요가 없다.
            button.transform.SetParent(groupGo.transform, false);

            Click(button);

            Assert.AreEqual(1, handler.CallCount,
                "IsInteractable()은 부모 CanvasGroup의 차단을 반영해야 한다.");
            Assert.AreEqual("not enough gold", handler.Last.DisabledMessage);
        }

        [Test]
        public void RedisablingWithNewReason_ReusesReceiver_AndReplaysLatestReason()
        {
            var button = NewButton();
            var handler = new SpyHandler();

            Disable(button, handler, "reason A");
            Disable(button, handler, "reason B");

            Click(button);

            Assert.AreEqual(1, handler.CallCount);
            Assert.AreEqual("reason B", handler.Last.DisabledMessage,
                "두 번째 비활성화의 사유로 덮어써야 한다.");
            Assert.AreEqual(1, button.GetComponents<ButtonDisabledClickReceiver>().Length,
                "리시버는 재사용해야 하며, 추가로 붙으면 안 된다.");
        }

        [UnityTest]
        public IEnumerator GraphicRaycastOnDisabledButton_DispatchesToReceiver_AndReplaysReason()
        {
            // 이 기능 전체의 전제: Selectable.interactable = false여도 레이캐스트는
            // 막히지 않아, 같은 GameObject의 다른 IPointerClickHandler(리시버)가
            // 클릭을 받을 수 있다. 다른 테스트들은 ExecuteEvents.Execute로 이미 알고
            // 있는 GameObject에 직접 디스패치했을 뿐, 실제 레이캐스트 경로는 검증하지
            // 않았다. 여기서는 진짜 Canvas + GraphicRaycaster + EventSystem을 구성해
            // RaycastAll이 비활성 버튼을 히트하는지, 그리고 그 히트 결과로 디스패치한
            // 클릭이 실제로 리시버까지 도달해 사유를 재생하는지를 한 흐름으로 검증한다.
            var canvasGo = new GameObject(
                "RaycastCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Track(canvasGo);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            // 입력 모듈은 일부러 붙이지 않는다: EventSystem.RaycastAll은 등록된
            // Raycaster를 순회할 뿐 입력 모듈에 의존하지 않고, 이 프로젝트는 새
            // Input System을 사용하므로 StandaloneInputModule(레거시 Input 사용)을
            // 붙이면 EventSystem.Update()에서 UnityEngine.Input 접근 예외가 난다.
            var eventSystemGo = new GameObject("RaycastEventSystem", typeof(EventSystem));
            Track(eventSystemGo);

            var buttonGo = new GameObject(
                "RaycastButton", typeof(RectTransform), typeof(Image), typeof(Button));
            Track(buttonGo);
            buttonGo.transform.SetParent(canvasGo.transform, false);

            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var button = buttonGo.GetComponent<Button>();
            var handler = new SpyHandler();
            Disable(button, handler, "raycast reason");

            yield return null;

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) / 2f;
            var screenPoint = RectTransformUtility.WorldToScreenPoint(null, center);

            var pointerData = new PointerEventData(EventSystem.current)
            {
                position = screenPoint,
                button = PointerEventData.InputButton.Left,
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            var hitIndex = results.FindIndex(r => r.gameObject == buttonGo);
            Assert.GreaterOrEqual(hitIndex, 0,
                "Selectable.interactable = false여도 GraphicRaycaster는 레이캐스트를 막지 않아야 한다.");

            // 레이캐스트가 돌려준 GameObject를 그대로 디스패치 대상으로 쓴다.
            // 미리 들고 있던 참조를 쓰면 두 반쪽(히트 → 디스패치)이 만나지 않는다.
            var hit = results[hitIndex];
            pointerData.pointerCurrentRaycast = hit;
            pointerData.pointerPressRaycast = hit;

            ExecuteEvents.Execute(hit.gameObject, pointerData, ExecuteEvents.pointerClickHandler);

            Assert.AreEqual(1, handler.CallCount,
                "실제 레이캐스트 히트로 디스패치한 클릭이 리시버까지 도달해야 한다.");
            Assert.AreEqual("raycast reason", handler.Last.DisabledMessage);
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

        [Test]
        public void ClickAfterHandlerObjectDestroyed_DoesNotReplay_AndDoesNotThrow()
        {
            // 리시버는 버튼과 함께 살지만 핸들러는 더 짧게 살 수 있다. _handler는
            // 인터페이스 타입이라 `_handler == null`은 UnityEngine.Object의 오버로드된
            // == 연산자를 타지 않는다. 파괴된 MonoBehaviour 핸들러는 그 검사를 그냥
            // 통과하므로, 별도 가드가 없으면 Handle()이 죽은 오브젝트 위에서 실행된다.
            var handlerGo = new GameObject("MonoHandler", typeof(MonoSpyHandler));
            Track(handlerGo);
            var handler = handlerGo.GetComponent<MonoSpyHandler>();

            var button = NewButton();
            using (var scope = new ButtonInteractableScope(button, handler))
            {
                scope.Require(false, "not enough gold");
            }

            UnityEngine.Object.DestroyImmediate(handlerGo);

            Assert.DoesNotThrow(() => Click(button));
            Assert.AreEqual(0, handler.CallCount,
                "파괴된 UnityEngine.Object 핸들러에는 사유를 전달하면 안 된다.");
        }
    }

    /// <summary>
    /// <see cref="MonoBehaviour"/>로 구현한 핸들러(전형적인 토스트 매니저 형태).
    /// 파괴된 뒤 <see cref="Handle"/>이 호출되면 첫 Unity API 접근에서
    /// MissingReferenceException이 나도록 일부러 <c>gameObject</c>를 만진다.
    /// </summary>
    internal sealed class MonoSpyHandler : MonoBehaviour, IButtonDisabledHandler
    {
        public int CallCount { get; private set; }
        public DisabledReason Last { get; private set; }

        public void Handle(DisabledReason reason)
        {
            CallCount++;
            Last = reason;

            _ = gameObject.name;
        }
    }
}
