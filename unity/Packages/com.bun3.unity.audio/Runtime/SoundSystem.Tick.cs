// Per-frame mirror: advances VoiceTable and applies volume/position/stop to AudioSources.
using UnityEngine;

namespace Bun3.Unity.Audio
{
    public sealed partial class SoundSystem
    {
        private static void TickAll()
        {
            for (var i = 0; i < Live.Count; i++)
            {
                Live[i].Tick(Time.deltaTime);
            }
        }

        internal void Tick(float dt)
        {
            _completedScratch.Clear();
            Table.Tick(dt, _completedScratch);

            for (var i = 0; i < _completedScratch.Count; i++)
            {
                var (slot, completion) = _completedScratch[i];
                _sources[slot].Stop();
                completion?.TrySetResult();
            }

            for (var i = 0; i < Table.Slots.Length; i++)
            {
                ref var voice = ref Table.Slots[i];
                if (voice.State == VoiceState.Idle)
                {
                    continue;
                }
                _sources[i].volume = Table.CurrentVolume(i);
                if (voice.Follow != null)
                {
                    _sources[i].transform.position = voice.Follow.position;
                }
            }
        }
    }
}
