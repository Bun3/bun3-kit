using System;
using System.Threading;
using UnityEngine;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio
{
    // A callback captures one immutable generation before touching its processing admission.
    internal sealed class DissonanceOutputMonitor : MonoBehaviour
    {
        private DissonancePathPlayback _record;
        private int _started;
        internal void Bind(DissonancePathPlayback record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (Interlocked.CompareExchange(ref _record, record, null) != null)
                throw new InvalidOperationException("A source monitor cannot be rebound to another generation.");
        }
        internal void StartOutput() => Volatile.Write(ref _started, 1);
        internal void Unbind()
        {
            Volatile.Write(ref _started, 0);
            Volatile.Write(ref _record, null);
        }

        private void OnAudioFilterRead(float[] samples, int channels)
        {
            var record = Volatile.Read(ref _record);
            if (record == null || Volatile.Read(ref _started) == 0 || channels <= 0) { Array.Clear(samples, 0, samples.Length); return; }
            record.ReadInterleaved(samples, channels);
            if (record.Parameters.IsBlocked) Array.Clear(samples, 0, samples.Length);
            if (channels > 0) record.RecordDeliveredFrames(samples.Length / channels);
        }
    }
}
