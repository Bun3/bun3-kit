using System;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio
{
    /// <summary>One frame range of the ring clip to fill with speech or silence.</summary>
    public readonly struct RingWrite
    {
        /// <summary>First ring frame.</summary>
        public readonly int Start;

        /// <summary>Frame count, a whole number of blocks.</summary>
        public readonly int Count;

        /// <summary>Whether the range is filled with silence instead of speech.</summary>
        public readonly bool Silence;

        /// <summary>Creates a range.</summary>
        public RingWrite(int start, int count, bool silence) { Start = start; Count = count; Silence = silence; }
    }

    /// <summary>
    /// Plans main-thread writes into a looping clip so the audio thread only plays native data. Speech is written up to a
    /// fixed lead ahead of the play position; everything after it, up to one block before the play position wraps around,
    /// is kept silent, so a stalled main thread plays silence rather than stale speech. Positions are whole 10 ms blocks.
    /// </summary>
    public sealed class VoiceClipRing
    {
        private long _played, _written, _silenced;
        private int _lastPlay;
        private bool _started;

        /// <summary>Creates a ring for a sample rate.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A rate below 100 Hz, a nonpositive lead, or a length not above the lead.</exception>
        public VoiceClipRing(int sampleRate, float leadSeconds = .06f, float lengthSeconds = 1f)
        {
            if (sampleRate < 100) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (!(leadSeconds > 0)) throw new ArgumentOutOfRangeException(nameof(leadSeconds));
            BlockFrames = sampleRate / 100;
            LeadFrames = Blocks(leadSeconds * sampleRate) * BlockFrames;
            LengthFrames = Blocks(lengthSeconds * sampleRate) * BlockFrames;
            if (LengthFrames < LeadFrames + 2 * BlockFrames) throw new ArgumentOutOfRangeException(nameof(lengthSeconds));
        }

        /// <summary>Frames per block (10 ms).</summary>
        public int BlockFrames { get; }

        /// <summary>Speech written ahead of the play position, in frames.</summary>
        public int LeadFrames { get; }

        /// <summary>Ring clip length in frames.</summary>
        public int LengthFrames { get; }

        /// <summary>Frames played between the last two plans.</summary>
        public int AdvancedFrames { get; private set; }

        /// <summary>Times the play position overtook the written speech.</summary>
        public int Underruns { get; private set; }

        /// <summary>
        /// Plans the writes for the clip's current play frame. Returns how many entries of <paramref name="writes"/> were
        /// filled: speech first, then silence, each split where it wraps. Four entries always suffice.
        /// </summary>
        public int Plan(int playFrame, Span<RingWrite> writes)
        {
            if (playFrame < 0 || playFrame >= LengthFrames) throw new ArgumentOutOfRangeException(nameof(playFrame));
            if (!_started)
            {
                _started = true;
                _lastPlay = playFrame;
                _played = _written = _silenced = playFrame;
            }
            AdvancedFrames = (playFrame - _lastPlay + LengthFrames) % LengthFrames;
            _lastPlay = playFrame;
            _played += AdvancedFrames;
            if (_written < _played)
            {
                Underruns++;
                _written = CeilBlock(_played);
            }
            int count = 0;
            long target = CeilBlock(_played + LeadFrames);
            if (target > _written) count = Emit(writes, count, _written, target, false);
            _written = Math.Max(_written, target);
            long silenceStart = Math.Max(_silenced, _written);
            long silenceEnd = FloorBlock(_played + LengthFrames - BlockFrames);
            if (silenceEnd > silenceStart) count = Emit(writes, count, silenceStart, silenceEnd, true);
            _silenced = Math.Max(_silenced, silenceEnd);
            return count;
        }

        private int Emit(Span<RingWrite> writes, int count, long from, long to, bool silence)
        {
            int start = (int)(from % LengthFrames);
            int total = (int)(to - from);
            int first = Math.Min(total, LengthFrames - start);
            writes[count++] = new RingWrite(start, first, silence);
            if (total > first) writes[count++] = new RingWrite(0, total - first, silence);
            return count;
        }

        private int Blocks(float frames) => (int)Math.Ceiling(frames / BlockFrames);
        private long CeilBlock(long frame) => (frame + BlockFrames - 1) / BlockFrames * BlockFrames;
        private long FloorBlock(long frame) => frame / BlockFrames * BlockFrames;
    }
}
