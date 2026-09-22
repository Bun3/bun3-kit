using System;
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
                Slots[i].Fade.Factor = 1f;
            }
        }

        /// <summary>
        /// Reserves a slot for <paramref name="def"/>. Returns false when blocked by cooldown
        /// (or zero capacity). <paramref name="stolenSlot"/> is the slot whose previous voice
        /// was cut short (-1 if none); <paramref name="stolenSignal"/> carries that voice's
        /// pre-overwrite generation, awaiter, and completion callback so the caller can signal it.
        /// </summary>
        public bool TryAllocate(
            SoundDef def, float clipLength, out int slotIndex, out int stolenSlot,
            out (uint Generation, AutoResetUniTaskCompletionSource Completion, Action<SoundHandle> Callback) stolenSignal)
        {
            stolenSlot = -1;
            slotIndex = -1;
            stolenSignal = default;
            if (Slots.Length == 0)
            {
                return false;
            }
            if (def.EffectiveCooldown > 0f
                && _lastPlayTime.TryGetValue(def, out var last)
                && _time - last < def.EffectiveCooldown)
            {
                return false;
            }

            slotIndex = FindSlot(def, ref stolenSlot);
            if (stolenSlot >= 0)
            {
                ref var stolenRef = ref Slots[stolenSlot];
                stolenSignal = (stolenRef.Generation, stolenRef.Completion, stolenRef.CompletionCallback);
            }
            ref var slot = ref Slots[slotIndex];
            slot.Generation++;
            slot.State = VoiceState.Playing;
            slot.Def = def;
            slot.Elapsed = 0f;
            slot.ClipLength = clipLength;
            slot.Loop = def.EffectiveLoop;
            slot.ExternalCompletion = false;
            slot.OutputComplete = false;
            slot.Fade.SetInstant(1f);
            slot.BaseVolume = def.EffectiveVolume.Roll(_rng);
            slot.VolumeScale = 1f;
            slot.Pitch = def.EffectivePitch.Roll(_rng);
            slot.PlaybackRate = Mathf.Max(0f, slot.Pitch);
            slot.StartTime = _time;
            slot.Follow = null;
            slot.Completion = null;
            slot.CompletionCallback = null;
            slot.OcclusionCurrent = 0f;
            slot.OcclusionTarget = 0f;
            if (def.EffectiveCooldown > 0f)
            {
                _lastPlayTime[def] = _time;
            }
            return true;
        }

        internal void Prepare(SoundDef def)
        {
            if (!_lastPlayTime.ContainsKey(def)) _lastPlayTime.Add(def, float.NegativeInfinity);
        }

        /// <summary>True when the slot is active and its generation matches.</summary>
        public bool IsValid(int slot, uint generation)
            => Slots[slot].State != VoiceState.Idle && Slots[slot].Generation == generation;

        /// <summary>Frees the slot and invalidates all outstanding handles to it.</summary>
        public void Release(int slot)
        {
            ref var voice = ref Slots[slot];
            voice.State = VoiceState.Idle;
            voice.ExternalCompletion = false;
            voice.OutputComplete = false;
            voice.Generation++;
            voice.Def = null;
            voice.Follow = null;
            voice.Completion = null;
            voice.CompletionCallback = null;
        }

        /// <summary>Effective playback volume for the slot (base × handle scale × fade).</summary>
        public float CurrentVolume(int slot)
        {
            ref var voice = ref Slots[slot];
            return voice.BaseVolume * voice.VolumeScale * voice.Fade.Factor;
        }

        /// <summary>Starts a fade from the current factor to full volume.</summary>
        public void BeginFadeIn(int slot, float duration)
        {
            ref var voice = ref Slots[slot];
            if (duration <= 0f)
            {
                voice.Fade.SetInstant(1f);
                voice.State = VoiceState.Playing;
                return;
            }
            voice.Fade.Begin(0f, 1f, duration);
            voice.State = VoiceState.FadingIn;
        }

        /// <summary>
        /// Starts a fade from the current factor to silence; the voice completes and is
        /// released on the tick the fade finishes (immediately next tick when duration ≤ 0).
        /// </summary>
        public void BeginFadeOut(int slot, float duration)
        {
            ref var voice = ref Slots[slot];
            voice.Fade.Begin(voice.Fade.Factor, 0f, Mathf.Max(duration, float.Epsilon));
            voice.State = VoiceState.FadingOut;
        }

        /// <summary>
        /// Advances all active voices: fade interpolation (real-time — a fade-out must finish
        /// even on a frozen voice) and playback-rate-scaled completion (never
        /// AudioSource.isPlaying — pause would misread). Completion tracks
        /// <see cref="VoiceSlot.PlaybackRate"/>, not real time: a pitch-0 voice never expires,
        /// a 2x voice completes in half the real time. For each completed slot, the slot index,
        /// its pre-release Generation, Completion, and CompletionCallback (all captured before
        /// Release clears them) are appended to <paramref name="completed"/>.
        /// </summary>
        public void Tick(
            float dt,
            List<(int Slot, uint Generation, AutoResetUniTaskCompletionSource Completion, Action<SoundHandle> Callback)> completed)
        {
            _time += dt;
            for (var i = 0; i < Slots.Length; i++)
            {
                ref var voice = ref Slots[i];
                if (voice.State == VoiceState.Idle)
                {
                    continue;
                }

                if (!AdvanceVoiceAndCheckCompletion(ref voice, dt))
                {
                    continue;
                }

                var generation = voice.Generation;
                var completion = voice.Completion;
                var callback = voice.CompletionCallback;
                Release(i);
                completed.Add((i, generation, completion, callback));
            }
        }

        // Mutates one slot's playback state; completion notification belongs to the caller.
        private bool AdvanceVoiceAndCheckCompletion(ref VoiceSlot voice, float deltaTime)
        {
            voice.Elapsed += deltaTime * voice.PlaybackRate;
            if (voice.OcclusionCurrent != voice.OcclusionTarget)
            {
                voice.OcclusionCurrent = Mathf.MoveTowards(
                    voice.OcclusionCurrent, voice.OcclusionTarget, deltaTime / _occlusionSmoothing);
            }

            if (voice.Fade.Advance(deltaTime))
            {
                if (voice.State == VoiceState.FadingOut)
                {
                    return true;
                }
                voice.State = VoiceState.Playing;
            }
            return HasPlaybackCompleted(in voice);
        }

        private static bool HasPlaybackCompleted(in VoiceSlot voice)
        {
            if (voice.ExternalCompletion)
            {
                return voice.OutputComplete;
            }
            return !voice.Loop && voice.Elapsed >= voice.ClipLength;
        }

        private int FindSlot(SoundDef def, ref int stolenSlot)
        {
            if (def.EffectiveMaxInstances > 0)
            {
                var count = 0;
                var oldestOfDef = -1;
                var oldestTime = float.MaxValue;
                for (var i = 0; i < Slots.Length; i++)
                {
                    ref var voice = ref Slots[i];
                    if (voice.State == VoiceState.Idle || !ReferenceEquals(voice.Def, def))
                    {
                        continue;
                    }
                    count++;
                    if (voice.StartTime < oldestTime)
                    {
                        oldestTime = voice.StartTime;
                        oldestOfDef = i;
                    }
                }
                if (count >= def.EffectiveMaxInstances)
                {
                    stolenSlot = oldestOfDef;
                    return oldestOfDef;
                }
            }

            var globalOldest = 0;
            var globalOldestTime = float.MaxValue;
            for (var i = 0; i < Slots.Length; i++)
            {
                ref var voice = ref Slots[i];
                if (voice.State == VoiceState.Idle)
                {
                    return i;
                }
                if (voice.StartTime < globalOldestTime)
                {
                    globalOldestTime = voice.StartTime;
                    globalOldest = i;
                }
            }
            stolenSlot = globalOldest;
            return globalOldest;
        }
    }
}
