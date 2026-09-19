using UnityEngine;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Supplies a live listener position and hooks around one complete event delivery tick.</summary>
    public interface ISoundEventSessionListener : ISoundEventListener
    {
        /// <summary>Gets the listener position used by the next reach calculation.</summary>
        Vector3 SoundPosition { get; }

        /// <summary>Begins a complete event delivery tick.</summary>
        void BeginSoundTick();

        /// <summary>Ends a complete event delivery tick.</summary>
        void EndSoundTick();
    }
}
