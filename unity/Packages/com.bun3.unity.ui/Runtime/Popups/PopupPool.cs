using System;
using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.Core.Utils;
using Cysharp.Threading.Tasks;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Popup instance pool + preloading. The game supplies only the actual loading delegate;
    /// <see cref="RentAsync"/>/<see cref="Return"/> match the <see cref="PopupFactory"/>/<see cref="PopupReleaser"/>
    /// signatures and plug straight into <see cref="PopupStack"/>.
    /// </summary>
    /// <example><code>
    /// var pool = new PopupPool(LoadPopupAsync);
    /// var stack = new PopupStack(pool.RentAsync, pool.Return);
    /// await pool.PreloadAsync(PopupId.Shop);       // Preload on lobby entry + register for pooling.
    /// </code></example>
    /// <remarks>
    /// Pooling is opt-in: only keys registered via <see cref="PreloadAsync"/> or
    /// <see cref="MarkPooled"/> are returned to the pool (deactivated) on <see cref="Return"/>;
    /// everything else is destroyed. Return only deactivates — transform/state restoration
    /// belongs to the game's <see cref="Popup"/> (since <see cref="IPopupArg{TArg}"/> is
    /// delivered again on every open, re-initialization naturally lives there).
    /// </remarks>
    public sealed class PopupPool : IDisposable
    {
        private readonly PopupFactory _loader;
        private readonly Dictionary<PopupKey, Queue<Popup>> _pooled = new();
        private readonly Dictionary<Popup, PopupKey> _rented = new();
        private bool _disposed;

        /// <param name="loader">Key to new instance. Loading strategy (Resources/Addressables) is the game's choice.</param>
        public PopupPool(PopupFactory loader)
            => _loader = loader ?? throw new ArgumentNullException(nameof(loader));

        /// <summary>Registers a key for pooling (without preloading instances). Unregistered keys are destroyed on return.</summary>
        public void MarkPooled(PopupKey key)
        {
            if (!_pooled.ContainsKey(key))
                _pooled[key] = new Queue<Popup>();
        }

        /// <summary>Registers a type key for pooling.</summary>
        public void MarkPooled<TPopup>(string popupName = null) where TPopup : Popup
            => MarkPooled(PopupKey.Of<TPopup>(popupName));

        /// <summary>Preloads by type key.</summary>
        public UniTask PreloadAsync<TPopup>(int count = 1, string popupName = null,
            CancellationToken cancellationToken = default) where TPopup : Popup
            => PreloadAsync(PopupKey.Of<TPopup>(popupName), count, cancellationToken);

        /// <summary>Registers the key for pooling and pre-creates instances, stored deactivated.</summary>
        public async UniTask PreloadAsync(PopupKey key, int count = 1,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            MarkPooled(key);

            for (int i = 0; i < count; i++)
            {
                var popup = await _loader(key, cancellationToken);
                if (popup == null)
                    return;

                popup.gameObject.SetActive(false);
                _pooled[key].Enqueue(popup);
            }
        }

        /// <summary>
        /// Takes from the pool (activating), or creates via the loader when empty.
        /// Plugs into the stack as its <see cref="PopupFactory"/>.
        /// </summary>
        public async UniTask<Popup> RentAsync(PopupKey key, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_pooled.TryGetValue(key, out var queue))
            {
                while (queue.Count > 0)
                {
                    var pooled = queue.Dequeue();
                    if (!pooled)
                        continue; // Skip and drop entries destroyed externally.

                    pooled.gameObject.SetActive(true);
                    _rented[pooled] = key;
                    return pooled;
                }
            }

            var popup = await _loader(key, cancellationToken);
            if (popup != null)
                _rented[popup] = key;

            return popup;
        }

        /// <summary>
        /// Takes an instance back: pooled keys are deactivated and stored, others destroyed.
        /// Plugs into the stack as its <see cref="PopupReleaser"/>.
        /// </summary>
        public void Return(Popup popup)
        {
            if (popup == null)
                return;

            bool wasRented = _rented.Remove(popup, out var key);

            if (!popup)
                return; // Already destroyed on the Unity side.

            if (!_disposed && wasRented && _pooled.TryGetValue(key, out var queue))
            {
                popup.gameObject.SetActive(false);
                queue.Enqueue(popup);
                return;
            }

            Destroy(popup);
        }

        /// <summary>Destroys all instances held in the pool. Rented instances are untouched.</summary>
        public void Clear()
        {
            foreach (var queue in _pooled.Values)
            {
                while (queue.Count > 0)
                    Destroy(queue.Dequeue());
            }
        }

        /// <summary>Runs <see cref="Clear"/> and makes the pool unusable. Instances returned afterward are destroyed.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Clear();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PopupPool));
        }

        private static void Destroy(Popup popup)
        {
            if (popup)
                popup.gameObject.SafeDestroy();
        }
    }
}
