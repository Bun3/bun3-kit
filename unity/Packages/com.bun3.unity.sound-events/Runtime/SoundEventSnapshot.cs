namespace Bun3.Unity.SoundEvents
{
    /// <summary>Immutable event state delivered to listeners and reach policies.</summary>
    public readonly struct SoundEventSnapshot
    {
        internal SoundEventSnapshot(SoundEventData data, bool sustained, double expiresAt)
        { Data = data; IsSustained = sustained; ExpiresAt = expiresAt; }
        /// <summary>Gets the event payload.</summary>
        public SoundEventData Data { get; }
        /// <summary>Gets whether this event has a sustained lifetime.</summary>
        public bool IsSustained { get; }
        /// <summary>Gets the absolute expiry time, or zero for pulses.</summary>
        public double ExpiresAt { get; }
    }

    /// <summary>Describes a listener's relationship with an event.</summary>
    public enum SoundEventPhase
    {
        /// <summary>A pulse reached the listener once.</summary>
        Pulse,
        /// <summary>A sustained event entered reach.</summary>
        Enter,
        /// <summary>A sustained event remains in reach this tick.</summary>
        Update,
        /// <summary>A sustained event left reach or the listener was removed.</summary>
        Exit,
        /// <summary>A sustained event ended while in reach.</summary>
        End
    }
}
