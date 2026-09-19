namespace Bun3.Unity.SoundEvents
{
    /// <summary>Generation-safe reference to one event registration.</summary>
    public readonly struct SoundEventHandle
    {
        private readonly SoundEventWorld _world;
        private readonly int _slot;
        private readonly ulong _generation;
        internal SoundEventHandle(SoundEventWorld world, int slot, ulong generation)
        { _world = world; _slot = slot; _generation = generation; }
        /// <summary>Gets whether this registration remains active.</summary>
        public bool IsValid => _world != null && _world.IsEventValid(_slot, _generation);
        /// <summary>Ends this event. Returns false for an already-ended registration.</summary>
        public bool Stop() => _world != null && _world.Stop(_slot, _generation);
        /// <summary>Updates a sustained event's payload and absolute expiry. Its identity must stay unchanged.</summary>
        public bool Update(SoundEventData data, double expiresAt) =>
            _world != null && _world.UpdateEvent(_slot, _generation, data, expiresAt);
    }
}
