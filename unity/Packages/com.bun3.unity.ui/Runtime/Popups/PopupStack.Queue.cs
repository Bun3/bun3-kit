// PopupStack partial — sequential queue.
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    // Sequential queue path: shows one item at a time once the stack is fully empty.
    // (For the channel-style variant, see PopupQueue.)
    public sealed partial class PopupStack
    {
        private readonly struct QueuedPopup
        {
            public readonly PopupKey Key;
            public readonly int Layer;
            public readonly IQueuedPopupArg Arg;

            public QueuedPopup(PopupKey key, int layer, IQueuedPopupArg arg)
            {
                Key = key;
                Layer = layer;
                Arg = arg;
            }
        }

        private readonly Queue<QueuedPopup> _queue = new();

        /// <summary>Number of items waiting in the sequential queue.</summary>
        public int QueuedCount => _queue.Count;

        /// <summary>
        /// Adds to the sequential queue. When the stack is fully empty (including loads), items
        /// show one at a time from the head; the next shows when the current closes. For popups
        /// that must appear in sequence without overlapping, such as reward presentations.
        /// </summary>
        public void Enqueue(PopupKey key, int layer = 0)
        {
            ThrowIfDisposed();
            EnqueueCore(key, layer, null);
        }

        /// <summary>Adds to the sequential queue with initial data. (Low-frequency path — carrier allocation OK.)</summary>
        public void EnqueueWithArg<TArg>(PopupKey key, TArg arg, int layer = 0)
        {
            ThrowIfDisposed();
            EnqueueCore(key, layer, new QueuedPopupArg<TArg>(arg));
        }

        /// <summary>Entry point for <see cref="PopupQueue"/> only — opens directly, bypassing duplicate policy.</summary>
        internal UniTask<Popup> OpenQueuedAsync(PopupKey key, int layer, IQueuedPopupArg arg)
            => arg == null
                ? OpenAsync(key, layer, (byte)0, ArgMode.None)
                : OpenAsync(key, layer, arg, ArgMode.Queued);

        private void EnqueueCore(PopupKey key, int layer, IQueuedPopupArg arg)
        {
            _queue.Enqueue(new QueuedPopup(key, layer, arg));
            TryDrainQueue();
        }

        private void TryDrainQueue()
        {
            if (_stack.Count > 0 || _loading.Count > 0 || _queue.Count == 0)
                return;

            var next = _queue.Dequeue();
            OpenQueuedAsync(next.Key, next.Layer, next.Arg).Forget();
        }
    }
}
