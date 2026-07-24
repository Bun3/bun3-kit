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
