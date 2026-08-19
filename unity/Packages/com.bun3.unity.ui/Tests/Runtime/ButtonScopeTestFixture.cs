using System.Collections.Generic;
using Bun3.Unity.UI.Buttons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.Unity.UI.Tests
{
    /// <summary>
    /// Provides test-button creation/cleanup and click dispatch.
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
        /// Registers a GameObject created outside <see cref="NewButton"/> (parent CanvasGroup,
        /// Canvas, EventSystem, etc.) for cleanup at test end.
        /// </summary>
        protected void Track(GameObject go)
        {
            _spawned.Add(go);
        }

        /// <summary>
        /// Uses the EventSystem's real dispatch path. The event reaches every
        /// IPointerClickHandler on the button GameObject, so Button and Receiver handling of the
        /// same event can be verified together.
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

            // Prevents static state from leaking between tests; assigning null restores the NullHandler.
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
