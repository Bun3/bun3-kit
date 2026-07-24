using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bun3.UI.Buttons
{
    public ref struct ButtonInteractableScope
    {
        public readonly struct DisabledReason
        {
            private readonly string _disabledMessage;
            public string DisabledMessage => _disabledMessage;

            private readonly Action _disabledAction;
            public Action DisabledAction => _disabledAction;

            public DisabledReason(string disabledMessage, Action disabledAction)
            {
                _disabledMessage = disabledMessage;
                _disabledAction = disabledAction;
            }
        }

        private sealed class NullHandler : IButtonDisabledHandler
        {
            public void OnDisabled(DisabledReason reason) { }
            public void Handle() { }
        }

        public static IButtonDisabledHandler DefaultHandler { get; set; } = new NullHandler();

        private readonly Button _button;
        private readonly IButtonDisabledHandler _handler;

        private bool _interactable;
        private bool _disposed;

        public ButtonInteractableScope(Button button, IButtonDisabledHandler handler = null)
        {
            _button = button;
            _handler = handler ?? DefaultHandler;

            _interactable = true;
            _disposed = false;
        }

        public void Require(bool condition, string disabledMessage = null)
        {
            _interactable &= condition;

            if (!condition)
                _handler.OnDisabled(new DisabledReason(disabledMessage, null));
        }

        public void Require(bool condition, Action disabledAction)
        {
            _interactable &= condition;

            if (!condition && disabledAction != null)
                _handler.OnDisabled(new DisabledReason(null, disabledAction));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_button)
                _button.interactable = _interactable;

            _handler.Handle();
        }
    }
}
