using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Optional helper that keeps popups' sibling indices matching stack order on every order
    /// change (open/close/Focus). (Order notification and dim toggling are done by the stack —
    /// only transforms here.)
    /// </summary>
    /// <remarks>
    /// Assumes a popup-only parent — no index guarantee if non-popup children share the parent.
    /// Popups under different parents keep their relative order per parent.
    /// The game calls <see cref="Dispose"/> to tie its lifetime to the stack.
    /// </remarks>
    public sealed class PopupSiblingArranger : IDisposable
    {
        private readonly PopupStack _stack;
        private readonly Dictionary<Transform, int> _siblingCounters = new();
        private readonly Action<Popup> _onStackChanged;

        /// <param name="stack">Stack to arrange for. Subscribes to open/close/Focus events.</param>
        public PopupSiblingArranger(PopupStack stack)
        {
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));

            _onStackChanged = OnStackChanged;
            _stack.Opened += _onStackChanged;
            _stack.Closed += _onStackChanged;
            _stack.Focused += _onStackChanged;
        }

        /// <summary>Unsubscribes from stack events. No rearranging happens afterward.</summary>
        public void Dispose()
        {
            _stack.Opened -= _onStackChanged;
            _stack.Closed -= _onStackChanged;
            _stack.Focused -= _onStackChanged;
        }

        private void OnStackChanged(Popup popup) => Arrange();

        /// <summary>Rearranges immediately. For manual refresh, e.g. right after the game reparents a popup.</summary>
        public void Arrange()
        {
            _siblingCounters.Clear();

            var popups = _stack.Popups;

            for (int i = 0; i < popups.Count; i++)
            {
                var parent = popups[i].transform.parent;
                if (parent == null)
                    continue;

                _siblingCounters.TryGetValue(parent, out var siblingIndex);
                popups[i].transform.SetSiblingIndex(siblingIndex);
                _siblingCounters[parent] = siblingIndex + 1;
            }
        }
    }
}
