using System;
using System.Threading;
using UnityEngine;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio
{
    // Bound once: a late old source callback can update only its old managed generation record.
    internal sealed class DissonanceOutputMonitor : MonoBehaviour
    {
        private DissonancePathPlayback _record;
        internal void Bind(DissonancePathPlayback record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (Interlocked.CompareExchange(ref _record, record, null) != null)
                throw new InvalidOperationException("A source monitor cannot be rebound to another generation.");
        }
        private void OnAudioFilterRead(float[] samples, int channels)
        {
            var record = Volatile.Read(ref _record);
            if (record == null || channels <= 0) { Array.Clear(samples, 0, samples.Length); return; }
            record.ReadInterleaved(samples, channels);
            if (record.Parameters.IsBlocked) Array.Clear(samples, 0, samples.Length);
            if (channels > 0) record.RecordDeliveredFrames(samples.Length / channels);
        }
    }
}
