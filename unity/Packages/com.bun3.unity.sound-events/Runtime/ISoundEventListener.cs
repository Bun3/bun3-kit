namespace Bun3.Unity.SoundEvents
{
    /// <summary>Receives synchronous event notifications. World mutation is forbidden during callbacks.</summary>
    public interface ISoundEventListener
    {
        /// <summary>Receives one event transition. Queue desired mutations until the world operation returns.</summary>
        void OnSoundEvent(in SoundEventSnapshot snapshot, SoundEventPhase phase);
    }
}
