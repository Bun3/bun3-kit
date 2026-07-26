using System.Collections.Generic;
using Bun3.Unity.UI.Buttons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.Unity.UI.Tests
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
        /// <see cref="NewButton"/> 밖에서 직접 만든 GameObject(부모 CanvasGroup, Canvas,
        /// EventSystem 등)를 테스트 종료 시 함께 정리하도록 등록한다.
        /// </summary>
        protected void Track(GameObject go)
        {
            _spawned.Add(go);
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
