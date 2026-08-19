using System;
using Bun3.Unity.UI.Buttons;
using UnityEngine;
using UnityEngine.UI;

namespace Bun3.Unity.UI.Samples
{
    /// <summary>
    /// <see cref="ButtonInteractableScope"/> usage example.
    /// Combines multiple conditions to drive a button's interactable state, and replays the
    /// reason via <see cref="IButtonDisabledHandler"/> when the disabled button is clicked.
    /// </summary>
    public class ButtonInteractableScopeSample : MonoBehaviour
    {
        [SerializeField] private Button _purchaseButton;
        [SerializeField] private int _gold;
        [SerializeField] private int _itemCount;

        private const int Price = 100;
        private const int RequiredItems = 1;

        // Cached once so no delegate is allocated per frame.
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
