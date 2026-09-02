using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
        private readonly System.Random _rng;
        private readonly float _occlusionSmoothing;
        private float _time;

        public VoiceTable(int capacity, System.Random rng = null, float occlusionSmoothingSeconds = 0.15f)
        {
            _rng = rng ?? new System.Random();
            _occlusionSmoothing = Mathf.Max(occlusionSmoothingSeconds, 0.0001f);
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
        /// was cut short (-1 if none); <paramref name="stolenCompletion"/> is that voice's
        /// awaiter (captured before the slot is overwritten) so the caller can signal it.
        /// </summary>
        public bool TryAllocate(
            SoundDef def, float clipLength, out int slotIndex, out int stolenSlot,
            out AutoResetUniTaskCompletionSource stolenCompletion)
        {
            stolenSlot = -1;
            slotIndex = -1;
            stolenCompletion = null;
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
            if (stolenSlot >= 0)
            {
                stolenCompletion = Slots[stolenSlot].Completion;
            }
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
            slot.BaseVolume = def.Volume.Roll(_rng);
            slot.VolumeScale = 1f;
            slot.Pitch = def.Pitch.Roll(_rng);
            slot.PlaybackRate = Mathf.Max(0f, slot.Pitch);
            slot.StartTime = _time;
            slot.Follow = null;
            slot.Completion = null;
            slot.OcclusionCurrent = 0f;
            slot.OcclusionTarget = 0f;
            if (def.Cooldown > 0f)
            {
                _lastPlayTime[def] = _time;
            }
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

        /// <summary>Starts a fade from the current factor to full volume.</summary>
        public void BeginFadeIn(int slot, float duration)
        {
            ref var s = ref Slots[slot];
            if (duration <= 0f)
            {
                s.FadeDuration = 0f;
                s.FadeFactor = 1f;
                s.State = VoiceState.Playing;
                return;
            }
            s.FadeFrom = 0f;
            s.FadeTo = 1f;
            s.FadeFactor = 0f;
            s.FadeElapsed = 0f;
            s.FadeDuration = duration;
            s.State = VoiceState.FadingIn;
        }

        /// <summary>
        /// Starts a fade from the current factor to silence; the voice completes and is
        /// released on the tick the fade finishes (immediately next tick when duration ≤ 0).
        /// </summary>
        public void BeginFadeOut(int slot, float duration)
        {
            ref var s = ref Slots[slot];
            s.FadeFrom = s.FadeFactor;
            s.FadeTo = 0f;
            s.FadeElapsed = 0f;
            s.FadeDuration = Mathf.Max(duration, float.Epsilon);
            s.State = VoiceState.FadingOut;
        }

        /// <summary>
        /// Advances all active voices: fade interpolation (real-time — a fade-out must finish
        /// even on a frozen voice) and playback-rate-scaled completion (never
        /// AudioSource.isPlaying — pause would misread). Completion tracks
        /// <see cref="VoiceSlot.PlaybackRate"/>, not real time: a pitch-0 voice never expires,
        /// a 2x voice completes in half the real time. For each completed slot, the slot index
        /// and its Completion (captured before Release nulls it) are appended to
        /// <paramref name="completed"/>.
        /// </summary>
        public void Tick(float dt, List<(int Slot, AutoResetUniTaskCompletionSource Completion)> completed)
        {
            _time += dt;
            for (var i = 0; i < Slots.Length; i++)
            {
                ref var s = ref Slots[i];
                if (s.State == VoiceState.Idle)
                {
                    continue;
                }

                s.Elapsed += dt * s.PlaybackRate;

                if (s.OcclusionCurrent != s.OcclusionTarget)
                {
                    s.OcclusionCurrent = Mathf.MoveTowards(
                        s.OcclusionCurrent, s.OcclusionTarget, dt / _occlusionSmoothing);
                }

                if (s.FadeDuration > 0f)
                {
                    s.FadeElapsed += dt;
                    var t = Mathf.Clamp01(s.FadeElapsed / s.FadeDuration);
                    s.FadeFactor = Mathf.Lerp(s.FadeFrom, s.FadeTo, t);
                    if (t >= 1f)
                    {
                        s.FadeDuration = 0f;
                        if (s.State == VoiceState.FadingOut)
                        {
                            var completion = s.Completion;
                            Release(i);
                            completed.Add((i, completion));
                            continue;
                        }
                        s.State = VoiceState.Playing;
                    }
                }

                if (!s.Loop && s.Elapsed >= s.ClipLength)
                {
                    var completion = s.Completion;
                    Release(i);
                    completed.Add((i, completion));
                }
            }
        }

        private int FindSlot(SoundDef def, ref int stolenSlot)
        {
            if (def.MaxInstances > 0)
            {
                var count = 0;
                var oldestOfDef = -1;
                var oldestTime = float.MaxValue;
                for (var i = 0; i < Slots.Length; i++)
                {
                    ref var s = ref Slots[i];
                    if (s.State == VoiceState.Idle || !ReferenceEquals(s.Def, def))
                    {
                        continue;
                    }
                    count++;
                    if (s.StartTime < oldestTime)
                    {
                        oldestTime = s.StartTime;
                        oldestOfDef = i;
                    }
                }
                if (count >= def.MaxInstances)
                {
                    stolenSlot = oldestOfDef;
                    return oldestOfDef;
                }
            }

            var globalOldest = 0;
            var globalOldestTime = float.MaxValue;
            for (var i = 0; i < Slots.Length; i++)
            {
                ref var s = ref Slots[i];
                if (s.State == VoiceState.Idle)
                {
                    return i;
                }
                if (s.StartTime < globalOldestTime)
                {
                    globalOldestTime = s.StartTime;
                    globalOldest = i;
                }
            }
            stolenSlot = globalOldest;
            return globalOldest;
        }
    }
}
