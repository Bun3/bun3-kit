using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Generation-validated reference to a playing voice. Safe to keep after the voice
    /// ends or its slot is reused: every member silently no-ops (or reports false) on a
    /// stale handle — playing sound is fire-and-forget, staleness is not an error.
    /// </summary>
    public readonly struct SoundHandle
    {
        internal readonly SoundSystem Owner;
        internal readonly int SlotIndex;
        internal readonly uint Generation;

        internal SoundHandle(SoundSystem owner, int slot, uint generation)
        {
            Owner = owner;
            SlotIndex = slot;
            Generation = generation;
        }

        /// <summary>A handle that never refers to a voice.</summary>
        public static SoundHandle Invalid => default;

        /// <summary>True while this handle still refers to its original voice.</summary>
        public bool IsValid => Owner != null && Owner.TryGetSlot(this, out _);

        /// <summary>True while the voice is audible (fading counts as playing).</summary>
        public bool IsPlaying => IsValid;

        /// <summary>Stops the voice, optionally fading out over <paramref name="fadeOut"/> seconds.</summary>
        public void Stop(float fadeOut = 0f) => Owner?.Stop(this, fadeOut);

        /// <summary>Scales the voice's rolled base volume (1 = as rolled).</summary>
        public void SetVolume(float volume)
        {
            if (Owner != null && Owner.TryGetSlot(this, out var slot))
            {
                Owner.Table.Slots[slot].VolumeScale = volume;
            }
        }

        /// <summary>Overrides the voice's rolled pitch.</summary>
        public void SetPitch(float pitch)
        {
            if (Owner != null && Owner.TryGetSlot(this, out var slot))
            {
                Owner.Table.Slots[slot].Pitch = pitch;
                Owner.SetSourcePitch(slot, pitch);
            }
        }

        /// <summary>Moves the voice to a fixed world position and stops following.</summary>
        public void SetPosition(Vector3 position)
        {
            if (Owner != null && Owner.TryGetSlot(this, out var slot))
            {
                Owner.Table.Slots[slot].Follow = null;
                Owner.SetSourcePosition(slot, position);
            }
        }

        /// <summary>Makes the voice track <paramref name="target"/> every frame.</summary>
        public void Follow(Transform target)
        {
            if (Owner != null && Owner.TryGetSlot(this, out var slot))
            {
                Owner.Table.Slots[slot].Follow = target;
            }
        }
    }
}
