using UnityEngine;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Generation-safe reference to one listener registration.</summary>
    public readonly struct SoundEventListenerHandle
    {
        private readonly SoundEventWorld _world;
        private readonly int _slot;
        private readonly ulong _generation;
        internal SoundEventListenerHandle(SoundEventWorld world, int slot, ulong generation)
        { _world = world; _slot = slot; _generation = generation; }
        /// <summary>Gets whether this listener registration remains active.</summary>
        public bool IsValid => _world != null && _world.IsListenerValid(_slot, _generation);
        /// <summary>Changes reach geometry for the next tick.</summary>
        public bool Update(Vector3 position, float radius) =>
            _world != null && _world.UpdateListener(_slot, _generation, position, radius);
        /// <summary>Removes the listener and emits exit for its active relationships.</summary>
        public bool Release() => _world != null && _world.ReleaseListener(_slot, _generation);
    }
}
