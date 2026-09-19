namespace Bun3.Unity.Audio
{
    /// <summary>A generation-safe registration reference. Stale and default handles do nothing.</summary>
    public readonly struct ExternalAudioHandle
    {
        private readonly ExternalAudioRegistry _owner;
        private readonly int _slot;
        private readonly ulong _generation;

        internal ExternalAudioHandle(ExternalAudioRegistry owner, int slot, ulong generation)
        {
            _owner = owner;
            _slot = slot;
            _generation = generation;
        }

        /// <summary>Whether the original registration and source are still alive.</summary>
        public bool IsValid => _owner != null && _owner.IsValid(_slot, _generation);
        /// <summary>Sets explicitly owned source gain in [0,1]. Unowned controls throw.</summary>
        public void SetGain(float gain) => _owner?.SetGain(_slot, _generation, gain);
        /// <summary>Sets explicitly owned low-pass cutoff in [10,22000] hertz. Unowned controls throw.</summary>
        public void SetLowPassCutoff(float cutoff) => _owner?.SetLowPassCutoff(_slot, _generation, cutoff);
        /// <summary>Restores owned properties and releases the registration. Safe to repeat.</summary>
        public void Release() => _owner?.Release(_slot, _generation);
    }
}
