using System;
using System.Collections.Generic;
using global::Dissonance;
using System.Threading;
using Bun3.Unity.Audio.SteamAudio;
using global::Dissonance.Audio.Playback;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio
{
    /// <summary>
    /// One prepared SDK speech session rendered as native binaural stereo. Construction and reclamation
    /// belong to one control thread; ReadStereo belongs to one audio consumer. Never reset the SDK
    /// session or reuse its decoder until this generation has been retired and reclaimed.
    /// </summary>
    public sealed class DissonancePathPlayback
    {
        private readonly DissonancePlaybackResources _resources;
        private readonly SteamAudioPathRenderer _renderer;
        private readonly float[] _mono;
        private readonly float[] _stereo;
        private readonly float[] _coefficients;
        private PathPlaybackSettings _settings;
        private SpeechSession _session;
        private int _offset;
        private bool _inputComplete;
        private int _complete;
        // 0: open, 1: reader, 2: retired, 3: retired reader, 4: reclaimed.
        private int _state;
        private int _metadataState;
        private int _requiredChannelCapacity;
        private Action<List<RemoteChannel>> _captureChannels;
        private Exception _fault;
        private float _amplitude;
        private int _priority;
        private int _allowPositional = 1;
        private bool _lastPositional = true;
        private long _readCalls;
        private int _largestCallback;
        private float _peakDecoded;
        private float _peakStereoDifference;
        private long _requestedFrames;
        private long _completionFrame = -1;
        private long _deliveredFrames;
        private int _largestOutputBlock;

        /// <summary>
        /// Creates an immutable generation, copying coefficients and taking renderer ownership on success.
        /// The renderer must be unused and match the prepared session's sample rate.
        /// Path EQ and additional gain are unity in this initial fixed-parameter seam.
        /// </summary>
        public DissonancePathPlayback(SpeechSession session, SteamAudioPathRenderer renderer, long generation,
            float[] coefficients, SA.CoordinateSpace3 listener)
            : this(session, new DissonancePlaybackResources(renderer), generation, coefficients, listener) { }

        internal DissonancePathPlayback(SpeechSession session, DissonancePlaybackResources resources, long generation,
            float[] coefficients, SA.CoordinateSpace3 listener)
        {
            var renderer = resources.Renderer;
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (coefficients == null || coefficients.Length != renderer.CoefficientCount)
                throw new ArgumentException("Coefficient count must match the renderer.", nameof(coefficients));
            if (session.OutputWaveFormat.SampleRate != renderer.SampleRate)
                throw new ArgumentException("Prepared session and renderer sample rates must match.", nameof(session));
            _resources = resources;
            resources.Channels.Clear();
            _mono = resources.Mono;
            _stereo = resources.Stereo;
            Array.Clear(_mono, 0, _mono.Length);
            Array.Clear(_stereo, 0, _stereo.Length);
            _coefficients = (float[])coefficients.Clone();
            // Validate native parameters and warm processing outside the audio callback.
            if (!resources.Warmed)
            {
                renderer.Render(_mono, _stereo, _coefficients, 1, 1, 1, listener);
                resources.Warmed = true;
            }
            renderer.Reset();
            _renderer = renderer;
            _session = session;
            _priority = (int)session.PlaybackOptions.Priority;
            _settings = new PathPlaybackSettings(listener);
            Parameters = new DissonancePathMailbox(generation, renderer.CoefficientCount);
            Parameters.TryPublish(generation, _coefficients, _settings);
            Parameters.TrySetBlocked(generation, false);
            _offset = _stereo.Length;
            Generation = generation;
        }

        /// <summary>Gets the caller-assigned immutable session generation.</summary>
        public long Generation { get; }

        /// <summary>Gets the live parameters and immediate output gate owned by this generation.</summary>
        public DissonancePathMailbox Parameters { get; }

        /// <summary>Gets whether decoded input and all rendered tail frames have been consumed.</summary>
        public bool IsComplete => Volatile.Read(ref _complete) != 0;

        /// <summary>Gets whether the SDK decoder has completed and can no longer be queried.</summary>
        public bool IsInputComplete => Volatile.Read(ref _inputComplete);

        /// <summary>Gets the last safely observed SDK session priority.</summary>
        public global::Dissonance.ChannelPriority Priority => (global::Dissonance.ChannelPriority)Volatile.Read(ref _priority);

        /// <summary>Enables native path processing when the SDK packet also requests positional playback.</summary>
        public bool AllowPositionalPlayback
        {
            get => Volatile.Read(ref _allowPositional) != 0;
            set => Volatile.Write(ref _allowPositional, value ? 1 : 0);
        }

        /// <summary>Gets a captured processing failure for reporting from the control thread.</summary>
        public Exception Fault => Volatile.Read(ref _fault);

        /// <summary>Gets the last decoded frame's average rectified amplitude.</summary>
        public float Amplitude => Volatile.Read(ref _amplitude);

        /// <summary>Gets callback entries, including silent or retired reads. This is not an audibility measure.</summary>
        public long ReadCalls => Interlocked.Read(ref _readCalls);
        /// <summary>Gets the largest stereo callback array length observed by the processing owner.</summary>
        public int LargestCallbackSampleCount => Volatile.Read(ref _largestCallback);
        /// <summary>Gets peak decoded frame amplitude, retaining evidence across later silent frames.</summary>
        public float PeakDecodedAmplitude => Volatile.Read(ref _peakDecoded);
        /// <summary>Gets peak processed left/right difference. This does not prove device output or packet transport.</summary>
        public float PeakStereoDifference => Volatile.Read(ref _peakStereoDifference);

        /// <summary>Gets the submitted PCM stream frame index after the final rendered tail, or -1 before completion.</summary>
        public long CompletionFrameIndex => Interlocked.Read(ref _completionFrame);
        /// <summary>Gets frames observed by this generation's downstream source monitor.</summary>
        public long DeliveredFrames => Interlocked.Read(ref _deliveredFrames);
        internal bool HasOutputDrained => IsComplete && CompletionFrameIndex >= 0 &&
            DeliveredFrames >= CompletionFrameIndex + Math.Max(_mono.Length, Volatile.Read(ref _largestOutputBlock));

        /// <summary>
        /// Writes interleaved stereo, adapting even callback lengths to fixed native frames without allocation.
        /// Retired, faulted or concurrently claimed generations write silence. Unexpected processing failures
        /// retire this generation and publish Fault rather than escaping the audio callback.
        /// </summary>
        public void ReadStereo(float[] output)
            => ReadInterleaved(output, 2);

        internal void ReadInterleaved(float[] output, int channels)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (channels <= 0 || output.Length % channels != 0) throw new ArgumentException("Output must contain complete frames.", nameof(channels));
            Array.Clear(output, 0, output.Length);
            Interlocked.Increment(ref _readCalls);
            long firstFrame = Interlocked.Add(ref _requestedFrames, output.Length / channels) - output.Length / channels;
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;
            try
            {
                if (output.Length > _largestCallback) Volatile.Write(ref _largestCallback, output.Length);
                int written = 0;
                while (written < output.Length && Volatile.Read(ref _state) == 1)
                {
                    if (_offset == _stereo.Length)
                    {
                        if (Parameters.TryRead(_coefficients, out var settings)) _settings = settings;
                        if (_inputComplete)
                        {
                            if (_renderer.TailSamplesRemaining == 0)
                            {
                                Interlocked.CompareExchange(ref _completionFrame, firstFrame + written / channels, -1);
                                Volatile.Write(ref _complete, 1);
                                Volatile.Write(ref _amplitude, 0);
                                break;
                            }
                            _renderer.RenderTail(_stereo, _settings.Gain);
                        }
                        else
                        {
                            bool positional = AllowPositionalPlayback && _session.PlaybackOptions.IsPositional;
                            CaptureChannels();
                            // SDK Read returns true on completion and immediately recycles its decoder.
                            bool complete = _session.Read(new ArraySegment<float>(_mono));
                            Volatile.Write(ref _inputComplete, complete);
                            if (complete) _session = default;
                            else Volatile.Write(ref _priority, (int)_session.PlaybackOptions.Priority);
                            float sum = 0;
                            for (int i = 0; i < _mono.Length; i++) sum += Math.Abs(_mono[i]);
                            Volatile.Write(ref _amplitude, sum / _mono.Length);
                            if (_amplitude > _peakDecoded) Volatile.Write(ref _peakDecoded, _amplitude);
                            if (positional != _lastPositional) _renderer.Reset();
                            _lastPositional = positional;
                            if (positional) _renderer.RenderSpatial(_mono, _stereo, _coefficients, _settings.EqLow, _settings.EqMid,
                                _settings.EqHigh, _settings.Listener, _settings.Gain, _settings.NormalizeEq, _settings.SpatialBlend);
                            else for (int i = 0; i < _mono.Length; i++) _stereo[2 * i] = _stereo[2 * i + 1] = _mono[i];
                            float difference = _peakStereoDifference;
                            for (int i = 0; i < _stereo.Length; i += 2) difference = Math.Max(difference, Math.Abs(_stereo[i] - _stereo[i + 1]));
                            Volatile.Write(ref _peakStereoDifference, difference);
                        }
                        _offset = 0;
                    }
                    int frames = Math.Min((output.Length - written) / channels, (_stereo.Length - _offset) / 2);
                    if (channels == 2) Array.Copy(_stereo, _offset, output, written, frames * 2);
                    else for (int i = 0; i < frames; i++)
                    {
                        float left = _stereo[_offset + i * 2], right = _stereo[_offset + i * 2 + 1];
                        output[written + i * channels] = channels == 1 ? (left + right) * 0.5f : left;
                        if (channels > 1) output[written + i * channels + 1] = right;
                    }
                    _offset += frames * 2;
                    written += frames * channels;
                }
            }
            catch (Exception error)
            {
                Volatile.Write(ref _fault, error);
                RequestRetirement();
            }
            finally
            {
                if (Parameters.IsBlocked) Array.Clear(output, 0, output.Length);
                if (Interlocked.CompareExchange(ref _state, 0, 1) == 3)
                {
                    Array.Clear(output, 0, output.Length);
                    Volatile.Write(ref _amplitude, 0);
                    Volatile.Write(ref _state, 2);
                }
            }
        }

        /// <summary>Atomically prevents new readers. Does not wait for a current read or release resources.</summary>
        public void RequestRetirement()
        {
            Parameters.Retire();
            RetireMetadata();
            int state;
            do
            {
                state = Volatile.Read(ref _state);
                if (state >= 2) return;
            } while (Interlocked.CompareExchange(ref _state, state | 2, state) != state);
        }

        /// <summary>
        /// Disposes the owned renderer if retired and no reader is active. Returns false without waiting otherwise.
        /// Repeated success is harmless. A successful return is the barrier permitting SDK reset or reassignment.
        /// No subsequent audio callback is needed to make an idle retired generation reclaimable.
        /// </summary>
        public bool TryDisposeRetired() => TryReclaimRetired(true);

        internal bool TryReclaimRetired(bool disposeResources)
        {
            if (Volatile.Read(ref _metadataState) != 2) return false;
            int state = Interlocked.CompareExchange(ref _state, 4, 2);
            if (state == 4) return true;
            if (state != 2) return false;
            _session = default;
            if (disposeResources) _resources.Dispose();
            return true;
        }

        internal void SetChannelCapture(Action<List<RemoteChannel>> capture, int capacity)
        {
            _captureChannels = capture;
            ReserveChannelCapacity(capacity);
            CaptureChannels();
        }

        internal void ReserveChannelCapacity(int capacity)
        {
            Volatile.Write(ref _requiredChannelCapacity, Math.Max(capacity, _requiredChannelCapacity));
            if (_resources.Channels.Capacity >= _requiredChannelCapacity) return;
            if (!TryClaimMetadata()) return;
            try
            {
                if (_resources.Channels.Capacity < _requiredChannelCapacity)
                    _resources.Channels.Capacity = _requiredChannelCapacity;
            }
            finally { ReleaseMetadata(); }
        }

        private void CaptureChannels()
        {
            if (_captureChannels == null || !TryClaimMetadata()) return;
            try
            {
                // Packet receipt reserves capacity on the control thread. Skip a snapshot until that reservation succeeds.
                if (_resources.Channels.Capacity >= Volatile.Read(ref _requiredChannelCapacity))
                    _captureChannels(_resources.Channels);
            }
            finally { ReleaseMetadata(); }
        }

        internal bool TryCopyChannels(List<RemoteChannel> output)
        {
            if (!TryClaimMetadata()) return false;
            try
            {
                output.Clear();
                if (!IsInputComplete) output.AddRange(_resources.Channels);
                return true;
            }
            finally { ReleaseMetadata(); }
        }

        internal bool TryClaimMetadata() => Interlocked.CompareExchange(ref _metadataState, 1, 0) == 0;
        internal void ReleaseMetadata()
        {
            if (Interlocked.CompareExchange(ref _metadataState, 0, 1) == 3) Volatile.Write(ref _metadataState, 2);
        }

        private void RetireMetadata()
        {
            int state;
            do
            {
                state = Volatile.Read(ref _metadataState);
                if (state >= 2) return;
            } while (Interlocked.CompareExchange(ref _metadataState, state | 2, state) != state);
        }

        internal void RecordDeliveredFrames(int frames)
        {
            Interlocked.Add(ref _deliveredFrames, frames);
            int observed;
            do
            {
                observed = Volatile.Read(ref _largestOutputBlock);
                if (frames <= observed) return;
            } while (Interlocked.CompareExchange(ref _largestOutputBlock, frames, observed) != observed);
        }
    }

    internal sealed class DissonancePlaybackResources : IDisposable
    {
        internal readonly SteamAudioPathRenderer Renderer;
        internal readonly float[] Mono;
        internal readonly float[] Stereo;
        internal bool Warmed;
        internal readonly List<RemoteChannel> Channels = new();

        internal DissonancePlaybackResources(SteamAudioPathRenderer renderer)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Mono = new float[renderer.FrameSize];
            Stereo = new float[renderer.FrameSize * 2];
        }

        /// <inheritdoc/>
        public void Dispose() => Renderer.Dispose();
    }

}
