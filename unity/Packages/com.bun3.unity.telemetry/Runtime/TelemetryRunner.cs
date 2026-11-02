using UnityEngine;

namespace Bun3.Unity.Telemetry
{
    /// <summary>Gives the hub a tick every frame.</summary>
    sealed class TelemetryRunner : MonoBehaviour
    {
        void Update() => Telemetry.Hub?.Tick(Time.realtimeSinceStartupAsDouble);
    }
}
