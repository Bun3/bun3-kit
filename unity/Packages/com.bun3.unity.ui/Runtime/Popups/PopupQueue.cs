using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>Initial data carried by a queued entry. Low-frequency path — boxing/allocation OK.</summary>
    internal interface IQueuedPopupArg
    {
        void Deliver(Popup popup);
    }

    internal sealed class QueuedPopupArg<TArg> : IQueuedPopupArg
    {
        private readonly TArg _arg;

        public QueuedPopupArg(TArg arg) => _arg = arg;

        public void Deliver(Popup popup) => PopupStack.DeliverArg(popup, _arg);
    }

    /// <summary>
    /// Channel-style sequential display queue. Unlike <see cref="PopupStack.Enqueue"/> (which
    /// waits for the whole stack to empty), this shows one at a time based only on
    /// <b>whether the popup this queue opened has closed</b> — it appears even above other popups
    /// (mailbox, etc.). For "one at a time within a group" rules, such as item-gain or promotion
    /// notices pushed by multiple sources.
    /// </summary>
    /// <remarks>
    /// Higher <paramref name="priority"/> shows first; equal priorities keep insertion order
    /// (e.g. promotion 2 &gt; special item 1 &gt; normal batch 0). Need multiple channels — create
    /// multiple instances. Remaining entries keep showing after a stack <c>Clear()</c>; to drop
    /// pending entries on scene change, call <see cref="Clear"/> too.
    /// </remarks>
    public sealed class PopupQueue
    {
        private readonly struct Entry
        {
            public readonly PopupKey Key;
            public readonly int Layer;
            public readonly int Priority;
            public readonly IQueuedPopupArg Arg;

            public Entry(PopupKey key, int layer, int priority, IQueuedPopupArg arg)
            {
                Key = key;
                Layer = layer;
                Priority = priority;
                Arg = arg;
            }
        }

        private readonly PopupStack _stack;

        // Sort invariant: (Priority descending, insertion order). Head = next to show.
        private readonly List<Entry> _entries = new();

        private Popup _current;
        private bool _draining;

        /// <param name="stack">Stack this queue opens popups on.</param>
        public PopupQueue(PopupStack stack)
            => _stack = stack ?? throw new ArgumentNullException(nameof(stack));

        /// <summary>Number of entries waiting to be shown.</summary>
        public int Count => _entries.Count;

        /// <summary>Popup currently shown by this queue, or null.</summary>
        public Popup Current => _current;

        /// <summary>Adds to the queue. Opens immediately when displayable.</summary>
        public void Enqueue(PopupKey key, int layer = 0, int priority = 0)
            => EnqueueCore(key, layer, priority, null);

        /// <summary>Adds to the queue by type key.</summary>
        public void Enqueue<TPopup>(string popupName = null, int layer = 0, int priority = 0)
            where TPopup : Popup
            => EnqueueCore(PopupKey.Of<TPopup>(popupName), layer, priority, null);

        /// <summary>Adds to the queue with initial data. The popup must implement <see cref="IPopupArg{TArg}"/>.</summary>
        public void EnqueueWithArg<TArg>(PopupKey key, TArg arg, int layer = 0, int priority = 0)
            => EnqueueCore(key, layer, priority, new QueuedPopupArg<TArg>(arg));

        /// <summary>Adds to the queue by type key, with initial data.</summary>
        public void EnqueueWithArg<TPopup, TArg>(TArg arg, string popupName = null, int layer = 0, int priority = 0)
            where TPopup : Popup
            => EnqueueCore(PopupKey.Of<TPopup>(popupName), layer, priority, new QueuedPopupArg<TArg>(arg));

        /// <summary>Drops all waiting entries. The popup currently shown is untouched.</summary>
        public void Clear() => _entries.Clear();

        private void EnqueueCore(PopupKey key, int layer, int priority, IQueuedPopupArg arg)
        {
            int index = _entries.Count;
            while (index > 0 && _entries[index - 1].Priority < priority)
                index--;

            _entries.Insert(index, new Entry(key, layer, priority, arg));

            if (!_draining)
                DrainAsync().Forget();
        }

        private async UniTask DrainAsync()
        {
            _draining = true;
            try
            {
                while (_entries.Count > 0)
                {
                    var entry = _entries[0];
                    _entries.RemoveAt(0);

                    Popup popup;
                    try
                    {
                        popup = await _stack.OpenQueuedAsync(entry.Key, entry.Layer, entry.Arg);
                    }
                    catch (Exception exception)
                    {
                        // One entry's load failure must not stall the whole queue — log and continue.
                        UnityEngine.Debug.LogException(exception);
                        continue;
                    }

                    if (popup == null)
                        continue; // Load failed/canceled → next entry.

                    _current = popup;

                    if (popup.Phase != PopupPhase.None)
                        await popup.WaitUntilClosedAsync();

                    _current = null; // Do not point at a closed popup while the next entry loads.
                }
            }
            finally
            {
                _draining = false;
                _current = null;
            }
        }
    }
}
