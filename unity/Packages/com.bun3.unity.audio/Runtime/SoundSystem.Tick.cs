// Per-frame mirror: advances VoiceTable and applies volume/position/stop to AudioSources.
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
            _completedScratch.Clear();
            EvaluateOcclusion();
            for (var i = 0; i < _outputs.Length; i++)
                if (_outputActive[i] && Table.Slots[i].State != VoiceState.Idle)
                    Table.Slots[i].OutputComplete = _outputs[i].IsComplete;
            Table.Tick(dt, _completedScratch);

            // Two-pass: stop every completed source before firing any continuation. A
            // continuation may call Play() synchronously (TrySetResult invokes it inline) and
            // be handed a freshly (re)allocated slot — that new voice must never be stopped by
            // a later iteration of this same batch's stop pass.
            for (var i = 0; i < _completedScratch.Count; i++)
            {
                var slot = _completedScratch[i].Slot;
                RetireVoiceOutput(slot);
                _sources[slot].Stop();
            }
            for (var i = 0; i < _completedScratch.Count; i++)
            {
                var entry = _completedScratch[i];
                entry.Completion?.TrySetResult();
                entry.Callback?.Invoke(new SoundHandle(this, entry.Slot, entry.Generation));
            }

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

            if (_config.PitchWithTimescale)
            {
                var scale = Time.timeScale;
                if (!Mathf.Approximately(scale, _lastTimeScale))
                {
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

            TickMusic(dt);
        }
    }
}
