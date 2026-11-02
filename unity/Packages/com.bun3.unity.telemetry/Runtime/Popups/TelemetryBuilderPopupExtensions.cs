using Bun3.Unity.UI.Popups;

namespace Bun3.Unity.Telemetry.Popups
{
    /// <summary>Builder extension that attaches popup recording.</summary>
    public static class TelemetryBuilderPopupExtensions
    {
        /// <summary>Records every popup opened and closed on <paramref name="stack"/> once the hub is installed.</summary>
        public static TelemetryBuilder CapturePopups(this TelemetryBuilder builder, PopupStack stack) =>
            builder.Capture(new PopupCapture(stack));
    }
}
