using UnityEngine;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Authoritative payload supplied by the activity owner, without remotely supplied event identity.</summary>
    public readonly struct SoundActivityData
    {
        /// <summary>Creates authoritative activity data.</summary>
        public SoundActivityData(int kindId, Vector3 position, float intensity, float radius, ulong hostTick)
        { KindId = kindId; Position = position; Intensity = intensity; Radius = radius; HostTick = hostTick; }
        /// <summary>Gets the caller-defined kind.</summary>
        public int KindId { get; }
        /// <summary>Gets the authoritative position.</summary>
        public Vector3 Position { get; }
        /// <summary>Gets the authoritative intensity.</summary>
        public float Intensity { get; }
        /// <summary>Gets the authoritative radius.</summary>
        public float Radius { get; }
        /// <summary>Gets the authoritative tick.</summary>
        public ulong HostTick { get; }
    }
}
