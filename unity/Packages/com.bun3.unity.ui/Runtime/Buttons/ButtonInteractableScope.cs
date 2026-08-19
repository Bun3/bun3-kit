using System;
using Bun3.Unity.Core.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Bun3.Unity.UI.Buttons
{
    /// <summary>
    /// Combines multiple conditions to decide a button's <see cref="Selectable.interactable"/>.
    /// When a condition fails, its reason is stored and replayed when the user clicks that button.
    /// </summary>
    /// <remarks>
    /// When a failure carries a reason, <see cref="Dispose"/> (play mode only) auto-attaches a
    /// <see cref="ButtonDisabledClickReceiver"/> to the button GameObject and hands it the reason.
    /// Seeing the component in the inspector is normal; never add or remove it manually.
    /// Once attached, the component is reused rather than removed.
    /// <br/>
    /// Assumes a button's <see cref="Selectable.interactable"/> is decided <b>in one place,
    /// every frame</b>. If multiple scopes touch the same button, the last
    /// <see cref="Dispose"/> wins.
    /// </remarks>
    public ref struct ButtonInteractableScope
    {
        private sealed class NullHandler : IButtonDisabledHandler
        {
            public static readonly NullHandler Instance = new();

            public void Handle(DisabledReason reason) { }
        }

        private static IButtonDisabledHandler _defaultHandler = NullHandler.Instance;

        /// <summary>
        /// Handler used when the constructor receives none.
        /// Assigning null reverts to the do-nothing default handler.
        /// </summary>
        public static IButtonDisabledHandler DefaultHandler
        {
            get => _defaultHandler;
            set => _defaultHandler = value ?? NullHandler.Instance;
        }

        // With domain reload disabled (Enter Play Mode Options), prevents a handler assigned in a
        // previous play session (possibly pointing at destroyed objects) from surviving into the next.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefaultHandler() => _defaultHandler = NullHandler.Instance;

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
        /// Accumulates a condition. Any failure disables the button.
        /// </summary>
        /// <param name="condition">interactable condition</param>
        /// <param name="disabledMessage">
        /// Failure reason message. Null or empty disables silently, without a reason.
        /// </param>
        /// <remarks>
        /// When multiple conditions fail together, <b>the first failure carrying a reason</b> wins;
        /// declaration order is the priority. A bare <c>Require(false)</c> disables the button but
        /// leaves the reason slot empty, so a later condition's reason is adopted.
        /// <br/>
        /// Passing an interpolated string from a per-frame call site
        /// (<c>Require(gold &gt;= price, $"...")</c>) allocates a string every frame. Argument
        /// evaluation happens before <c>Require</c> runs, so the scope cannot prevent it — use a
        /// constant string, or build and cache only when the value changes.
        /// </remarks>
        public void Require(bool condition, string disabledMessage = null)
        {
            _interactable &= condition;

            if (!condition && _reason.IsEmpty && !string.IsNullOrEmpty(disabledMessage))
                _reason = new DisabledReason(disabledMessage);
        }

        /// <summary>
        /// Accumulates a condition. Any failure disables the button.
        /// </summary>
        /// <param name="condition">interactable condition</param>
        /// <param name="disabledAction">
        /// Action to run when the disabled button is clicked.
        /// </param>
        /// <remarks>
        /// When multiple conditions fail together, the first failure carrying a reason wins.
        /// <br/>
        /// Passing a method group from a per-frame call site (<c>Require(cond, OpenPopup)</c>)
        /// allocates a delegate every frame. Cache it once in an <see cref="Action"/> field.
        /// </remarks>
        public void Require(bool condition, Action disabledAction)
        {
            _interactable &= condition;

            if (!condition && _reason.IsEmpty && disabledAction != null)
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

            // Prevents the component from being saved into scenes/prefabs in the editor.
            if (!Application.isPlaying)
                return;

            if (_reason.IsEmpty)
            {
                if (_button.TryGetComponent(out ButtonDisabledClickReceiver existing))
                    existing.Clear();

                return;
            }

            var receiver = _button.gameObject.GetOrAdd<ButtonDisabledClickReceiver>();
            receiver.Set(_button, _reason, _handler);
        }
    }
}
