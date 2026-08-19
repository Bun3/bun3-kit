using System;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// <see cref="PopupManager"/> assembler. Centralizes the wiring every game repeats
    /// (pool → stack → router → arranger). Games using a DI container may register the pieces
    /// directly instead — they are all constructor-injected POCOs.
    /// </summary>
    /// <example><code>
    /// PopupManager.Instance = new PopupManagerBuilder(LoadPopupAsync)
    ///     .UsePool()
    ///     .UseBackKey(gameObject, onUnhandled: ShowQuitDialog)
    ///     .UseSiblingArranger()
    ///     .Build();
    /// PopupManager.Instance.Push&lt;ShopPopup&gt;();
    /// </code></example>
    public sealed class PopupManagerBuilder
    {
        private readonly PopupFactory _factory;
        private PopupReleaser _releaser;
        private bool _usePool;
        private GameObject _backKeyHost;
        private Action _backUnhandled;
        private bool _useArranger;

        /// <param name="factory">Key to popup instance loader. Becomes the pool's loader when pooling.</param>
        public PopupManagerBuilder(PopupFactory factory)
            => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        /// <summary>Wraps the factory in a <see cref="PopupPool"/>. Register pooled keys on the Pool after Build.</summary>
        public PopupManagerBuilder UsePool()
        {
            _usePool = true;
            return this;
        }

        /// <summary>Custom releaser. Cannot combine with <see cref="UsePool"/> (the pool owns release).</summary>
        public PopupManagerBuilder WithReleaser(PopupReleaser releaser)
        {
            _releaser = releaser;
            return this;
        }

        /// <summary>
        /// Attaches a <see cref="PopupBackKeyRouter"/> to <paramref name="host"/> for automatic
        /// ESC/Android back routing. <paramref name="onUnhandled"/> runs when the stack is empty
        /// and could not consume the key (e.g. quit confirmation dialog).
        /// </summary>
        public PopupManagerBuilder UseBackKey(GameObject host, Action onUnhandled = null)
        {
            _backKeyHost = host ? host : throw new ArgumentNullException(nameof(host));
            _backUnhandled = onUnhandled;
            return this;
        }

        /// <summary>Auto-arranges sibling indices to match stack order (assumes a popup-only parent).</summary>
        public PopupManagerBuilder UseSiblingArranger()
        {
            _useArranger = true;
            return this;
        }

        /// <summary>Creates and wires the pieces as configured, returning the <see cref="PopupManager"/>.</summary>
        public PopupManager Build()
        {
            if (_usePool && _releaser != null)
                throw new InvalidOperationException(
                    "UsePool and WithReleaser cannot be combined — the pool owns instance release.");

            PopupPool pool = null;
            PopupStack stack;

            if (_usePool)
            {
                pool = new PopupPool(_factory);
                stack = new PopupStack(pool.RentAsync, pool.Return);
            }
            else
            {
                stack = new PopupStack(_factory, _releaser);
            }

            var arranger = _useArranger ? new PopupSiblingArranger(stack) : null;

            PopupBackKeyRouter router = null;
            if (_backKeyHost)
            {
                router = _backKeyHost.AddComponent<PopupBackKeyRouter>();
                router.Stack = stack;
                if (_backUnhandled != null)
                    router.BackUnhandled += _backUnhandled;
            }

            return new PopupManager(stack, pool, router, arranger);
        }
    }
}
