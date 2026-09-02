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
            Table.Tick(dt, _completedScratch);

            // Two-pass: stop every completed source before firing any continuation. A
            // continuation may call Play() synchronously (TrySetResult invokes it inline) and
            // be handed a freshly (re)allocated slot — that new voice must never be stopped by
            // a later iteration of this same batch's stop pass.
            for (var i = 0; i < _completedScratch.Count; i++)
            {
                _sources[_completedScratch[i].Slot].Stop();
            }
            for (var i = 0; i < _completedScratch.Count; i++)
            {
                _completedScratch[i].Completion?.TrySetResult();
            }

            for (var i = 0; i < Table.Slots.Length; i++)
            {
                ref var voice = ref Table.Slots[i];
                if (voice.State == VoiceState.Idle)
                {
                    continue;
                }
                _sources[i].volume = Table.CurrentVolume(i) * OcclusionVolumeMultiplier(i);
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
                            _sources[i].pitch = Table.Slots[i].Pitch * scale;
                            Table.Slots[i].PlaybackRate = _sources[i].pitch;
                        }
                    }
                }
            }

            TickMusic(dt);
        }
    }
}
