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
    /// is kept silent, so a stalled main thread plays silence rather than stale speech. Speech the play position passed
    /// over during a stall is consumed without being written (<see cref="SkipFrames"/>), so the speech stream stays level
    /// with the play clock. Positions are whole 10 ms blocks.
    /// </summary>
    public sealed class VoiceClipRing
    {
        private long _played, _written, _silenced, _origin;
        private long _late;
        private int _lastPlay;
        private bool _started;

        /// <summary>Creates a ring for a sample rate.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A rate below 100 Hz, a nonpositive lead, or a length not above the lead.</exception>
        public VoiceClipRing(int sampleRate, float leadSeconds = .06f, float lengthSeconds = 4f)
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
        /// Speech frames to consume and discard before this plan's writes: the late start and any stall the play position
        /// passed over. Set by each <see cref="Plan"/>; a whole number of blocks.
        /// </summary>
        public int SkipFrames { get; private set; }

        /// <summary>
        /// Speech frames the play position has passed, including skipped ones. Can step back by under one block at an
        /// underrun; callers report only increases.
        /// </summary>
        public long SpeechFramesPlayed => Math.Max(0, Math.Min(_played, _written) - _origin);

        /// <summary>
        /// Starts the ring at a play frame with <paramref name="lateFrames"/> of speech already due, which the first plan
        /// skips. Optional; the first <see cref="Plan"/> otherwise starts on time.
        /// </summary>
        /// <exception cref="InvalidOperationException">The ring has already started.</exception>
        public void Start(int playFrame, int lateFrames)
        {
            if (playFrame < 0 || playFrame >= LengthFrames) throw new ArgumentOutOfRangeException(nameof(playFrame));
            if (lateFrames < 0) throw new ArgumentOutOfRangeException(nameof(lateFrames));
            if (_started) throw new InvalidOperationException("The ring has already started.");
            _started = true;
            _lastPlay = playFrame;
            _played = _written = _silenced = playFrame;
            _origin = playFrame - lateFrames / BlockFrames * BlockFrames;
            _late = playFrame - _origin;
        }

        /// <summary>
        /// Plans the writes for the clip's current play frame. Returns how many entries of <paramref name="writes"/> were
        /// filled: speech first, then silence, each split where it wraps. Four entries always suffice. Consume
        /// <see cref="SkipFrames"/> of speech before writing.
        /// </summary>
        public int Plan(int playFrame, Span<RingWrite> writes)
        {
            if (playFrame < 0 || playFrame >= LengthFrames) throw new ArgumentOutOfRangeException(nameof(playFrame));
            if (!_started) Start(playFrame, 0);
            SkipFrames = (int)_late;
            _late = 0;
            AdvancedFrames = (playFrame - _lastPlay + LengthFrames) % LengthFrames;
            _lastPlay = playFrame;
            _played += AdvancedFrames;
            if (_written < _played)
            {
                Underruns++;
                long resync = CeilBlock(_played);
                SkipFrames += (int)(resync - _written);
                _written = resync;
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

        /// <summary>
        /// Plans silence over speech already written more than <paramref name="marginFrames"/> past the last planned play
        /// position, so a closed gate takes effect before the written lead plays out. Call after <see cref="Plan"/>.
        /// Returns the filled entry count (at most two).
        /// </summary>
        public int SilenceAhead(int marginFrames, Span<RingWrite> writes)
        {
            long from = CeilBlock(_played + Math.Max(0, marginFrames));
            return from < _written ? Emit(writes, 0, from, _written, true) : 0;
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
