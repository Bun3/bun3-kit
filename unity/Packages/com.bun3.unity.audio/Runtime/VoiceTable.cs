using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Pure voice-slot state machine: allocation, stealing, cooldowns, fades, completion.
    /// Holds no AudioSource references so EditMode tests can drive it with injected delta time.
    /// </summary>
    internal sealed class VoiceTable
    {
        public readonly VoiceSlot[] Slots;

        private readonly Dictionary<SoundDef, float> _lastPlayTime = new();
        private float _time;

        public VoiceTable(int capacity)
        {
            Slots = new VoiceSlot[capacity];
            for (var i = 0; i < Slots.Length; i++)
            {
                Slots[i].VolumeScale = 1f;
                Slots[i].FadeFactor = 1f;
            }
        }

        /// <summary>
        /// Reserves a slot for <paramref name="def"/>. Returns false when blocked by cooldown
        /// (or zero capacity). <paramref name="stolenSlot"/> is the slot whose previous voice
        /// was cut short (-1 if none) so the caller can complete its awaiter.
        /// </summary>
        public bool TryAllocate(SoundDef def, float clipLength, out int slotIndex, out int stolenSlot)
        {
            stolenSlot = -1;
            slotIndex = -1;
            if (Slots.Length == 0)
            {
                return false;
            }
            if (def.Cooldown > 0f
                && _lastPlayTime.TryGetValue(def, out var last)
                && _time - last < def.Cooldown)
            {
                return false;
            }

            slotIndex = FindSlot(def, ref stolenSlot);
            ref var slot = ref Slots[slotIndex];
            slot.Generation++;
            slot.State = VoiceState.Playing;
            slot.Def = def;
            slot.Elapsed = 0f;
            slot.ClipLength = clipLength;
            slot.Loop = def.Loop;
            slot.FadeElapsed = 0f;
            slot.FadeDuration = 0f;
            slot.FadeFactor = 1f;
            slot.BaseVolume = def.Volume.Roll();
            slot.VolumeScale = 1f;
            slot.Pitch = def.Pitch.Roll();
            slot.StartTime = _time;
            slot.Follow = null;
            slot.Completion = null;
            _lastPlayTime[def] = _time;
            return true;
        }

        /// <summary>True when the slot is active and its generation matches.</summary>
        public bool IsValid(int slot, uint generation)
            => Slots[slot].State != VoiceState.Idle && Slots[slot].Generation == generation;

        /// <summary>Frees the slot and invalidates all outstanding handles to it.</summary>
        public void Release(int slot)
        {
            ref var s = ref Slots[slot];
            s.State = VoiceState.Idle;
            s.Generation++;
            s.Def = null;
            s.Follow = null;
            s.Completion = null;
        }

        /// <summary>Effective playback volume for the slot (base × handle scale × fade).</summary>
        public float CurrentVolume(int slot)
        {
            ref var s = ref Slots[slot];
            return s.BaseVolume * s.VolumeScale * s.FadeFactor;
        }

        private int FindSlot(SoundDef def, ref int stolenSlot)
        {
            // Task 4 replaces this body with maxInstances + global-oldest stealing.
            for (var i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].State == VoiceState.Idle)
                {
                    return i;
                }
            }
            stolenSlot = 0;
            return 0;
        }
    }
}
