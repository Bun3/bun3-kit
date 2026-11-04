using UnityEngine;
using UnityEngine.UI;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Attaches a UiControlTracker to new Selectables every 0.5 seconds and finishes click burst summaries.</summary>
    sealed class UiInteractionCapture : MonoBehaviour
    {
        const float ScanInterval = 0.5f;

        Selectable[] buffer = new Selectable[64];
        float nextScan;

        void Update()
        {
            UiControlTracker.TickBurst(Time.realtimeSinceStartupAsDouble);
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + ScanInterval;
            ScanNow();
        }

        internal void ScanNow()
        {
            if (buffer.Length < Selectable.allSelectableCount)
                buffer = new Selectable[Selectable.allSelectableCount * 2];
            var n = Selectable.AllSelectablesNoAlloc(buffer);
            for (var i = 0; i < n; i++)
            {
                var s = buffer[i];
                buffer[i] = null;
                // Scrollbars are skipped: the tracker's IPointerClickHandler would swallow clicks meant for the parent.
                if (s != null && s is not Scrollbar && !s.TryGetComponent<UiControlTracker>(out _)) s.gameObject.AddComponent<UiControlTracker>();
            }
        }
    }
}
