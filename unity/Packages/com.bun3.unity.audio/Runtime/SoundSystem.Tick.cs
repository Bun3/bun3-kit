// Per-frame mirror: advances VoiceTable and applies volume/position/stop to AudioSources.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        private float _lastTimeScale = 1f;

        private static void TickAll()
        {
            // Continuations run inline (UniTask's TrySetResult invokes them synchronously) and
            // may Dispose() a SoundSystem, which removes it from Live. Iterate backwards so a
            // shrinking Live cannot skip another system's tick this frame.
            for (var i = Live.Count - 1; i >= 0; i--)
            {
                // dt stays real time (unscaled): fades and occlusion smoothing are
                // control-plane. Completion is scaled separately via VoiceSlot.PlaybackRate
                // inside VoiceTable.Tick, so pitched/frozen voices expire with their audio.
                Live[i].Tick(Time.unscaledDeltaTime);
            }
        }

        internal void Tick(float dt)
        {
            if (_voiceRecoveryPending && !AudioListener.pause)
            {
                _voiceRecoveryPending = false;
                var lost = CollectLostVoices();
                if (lost != null)
                {
                    NotifyCompletedVoices(lost);
                    if (_disposed) return; // a callback disposed this system
                }
            }
            _completedScratch.Clear();
            EvaluateOcclusion();
            PollVoiceOutputCompletion();
            Table.Tick(dt, _completedScratch);
            RetireCompletedVoices();
            NotifyCompletedVoices(_completedScratch);
            UpdateActiveSources();
            UpdateTimeScalePitch();
            TickMusic(dt);
#if UNITY_EDITOR
            CheckEditedDefinitions();
#endif
        }

        private void PollVoiceOutputCompletion()
        {
            for (var i = 0; i < _outputs.Length; i++)
            {
                if (_outputActive[i] && Table.Slots[i].State != VoiceState.Idle)
                {
                    Table.Slots[i].OutputComplete = _outputs[i].IsComplete;
                }
            }
        }

        private void RetireCompletedVoices()
        {
            // Retire the whole batch before callbacks can reuse a completed slot.
            for (var i = 0; i < _completedScratch.Count; i++)
            {
                var slot = _completedScratch[i].Slot;
                RetireVoiceOutput(slot);
                _sources[slot].Stop();
            }
        }

        private void NotifyCompletedVoices(List<(int Slot, uint Generation, Cysharp.Threading.Tasks.AutoResetUniTaskCompletionSource Completion, Action<SoundHandle> Callback)> completed)
        {
            for (var i = 0; i < completed.Count; i++)
            {
                var entry = completed[i];
                entry.Completion?.TrySetResult();
                entry.Callback?.Invoke(new SoundHandle(this, entry.Slot, entry.Generation));
            }

        }

        private void UpdateActiveSources()
        {
            for (var i = 0; i < Table.Slots.Length; i++)
            {
                ref var voice = ref Table.Slots[i];
                if (voice.State == VoiceState.Idle)
                {
                    continue;
                }
                _sources[i].volume = Table.CurrentVolume(i) * GroupGain(voice.Def) * OcclusionVolumeMultiplier(i);
                ApplyOcclusionFilter(i);
                if (voice.Follow != null)
                {
                    _sources[i].transform.position = voice.Follow.position;
                }
            }

        }

        private void UpdateTimeScalePitch()
        {
            if (!_config.PitchWithTimescale)
            {
                return;
            }

            var scale = Time.timeScale;
            if (Mathf.Approximately(scale, _lastTimeScale))
            {
                return;
            }

            _lastTimeScale = scale;
            for (var i = 0; i < Table.Slots.Length; i++)
            {
                if (Table.Slots[i].State != VoiceState.Idle)
                {
                    SetSourcePitch(i, Table.Slots[i].Pitch);
                }
            }
        }
    }
}
