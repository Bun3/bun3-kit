namespace Bun3.Unity.Telemetry
{
    /// <summary>An automatic record source that is installed and removed together with the hub.</summary>
    public interface ITelemetryCapture
    {
        /// <summary>Starts recording.</summary>
        void Install();

        /// <summary>Stops recording.</summary>
        void Uninstall();
    }
}
