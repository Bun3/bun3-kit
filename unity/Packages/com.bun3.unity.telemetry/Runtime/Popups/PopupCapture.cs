using System;
using System.Collections.Generic;
using Bun3.Unity.UI.Popups;
using UnityEngine;

namespace Bun3.Unity.Telemetry.Popups
{
    /// <summary>Records popup opens and closes of one <see cref="PopupStack"/>.</summary>
    public sealed class PopupCapture : ITelemetryCapture
    {
        // Popups are few; entries of popups destroyed without closing pile up, so the map is cleared at the cap.
        const int MaxTracked = 64;

        readonly PopupStack stack;
        readonly Func<double> clock;
        readonly Dictionary<Popup, double> openedAt = new();

        /// <summary>Creates a capture for <paramref name="stack"/>.</summary>
        public PopupCapture(PopupStack stack) : this(stack, () => Time.realtimeSinceStartupAsDouble) { }

        internal PopupCapture(PopupStack stack, Func<double> clock)
        {
            this.stack = stack ?? throw new ArgumentNullException(nameof(stack));
            this.clock = clock;
        }

        /// <inheritdoc/>
        public void Install()
        {
            Uninstall();
            stack.Opened += HandleOpened;
            stack.Closed += HandleClosed;
        }

        /// <inheritdoc/>
        public void Uninstall()
        {
            stack.Opened -= HandleOpened;
            stack.Closed -= HandleClosed;
            openedAt.Clear();
        }

        internal void HandleOpened(Popup popup)
        {
            try
            {
                var now = clock();
                if (openedAt.Count >= MaxTracked) openedAt.Clear();
                openedAt[popup] = now;
                Telemetry.Track(TelemetryCategory.Popup, "popup_opened", ("popup", popup.Key.Name));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // A closed popup may already be destroyed, so only Key is read.
        internal void HandleClosed(Popup popup)
        {
            try
            {
                var name = popup.Key.Name;
                var now = clock();
                if (openedAt.Remove(popup, out var start))
                    Telemetry.Track(TelemetryCategory.Popup, "popup_closed", ("popup", name), ("seconds_open", now - start));
                else
                    Telemetry.Track(TelemetryCategory.Popup, "popup_closed", ("popup", name));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
