using System;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Close-lock scope returned by <see cref="Popup.BlockClose"/>.
    /// Dispose releases one lock (ref-count). Use with <c>using</c>.
    /// </summary>
    /// <remarks>
    /// Do not copy — each copy's Dispose releases a lock.
    /// Not a ref struct, so it works in <c>using</c> blocks that span awaits.
    /// </remarks>
    public struct PopupCloseScope : IDisposable
    {
        private Popup _popup;
        private readonly int _version;

        internal PopupCloseScope(Popup popup, int version)
        {
            _popup = popup;
            _version = version;
        }

        /// <summary>Releases one lock. Ignored if the popup closed in the meantime (generation mismatch).</summary>
        public void Dispose()
        {
            var popup = _popup;
            _popup = null;

            if (popup != null)
                popup.ReleaseCloseScope(_version);
        }
    }
}
