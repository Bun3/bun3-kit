using System;
using System.Collections.Generic;
using System.Threading;
using Bun3.Unity.Audio.SteamAudio;
using global::Dissonance;
using global::Dissonance.Audio.Playback;
using global::Dissonance.Networking;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio
{
    /// <summary>
    /// Custom SDK pool playback using a DSP-driven stereo generator. Configure each instance before voice starts.
    /// The application publishes copied live path parameters and gates unavailable coverage explicitly.
    /// Commands arriving during retirement are dropped; a fresh StartPlayback is required afterward.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DissonanceSteamAudioPlayback : MonoBehaviour, IVoicePlaybackInternal
    {
        private DissonanceDecoderHost _decoder;
        private AudioSource _source;
        private AudioClip _clip;
        private Func<int, int, SteamAudioPathRenderer> _factory;
        private float[] _coefficients;
        private SA.CoordinateSpace3 _listener;
        private string _playerName;
        private CodecSettings _codec;
        private bool _muted;
        private bool _positional;
        private float _volume = 1;
        private int _rate;
        private int _frame;
        private long _generation;
        private Exception _fault;
        private DissonancePathPlayback _lastPlayback;
        private VoiceClipRing _ring;
        private float[] _block;
        private readonly RingWrite[] _writes = new RingWrite[4];
        private float _leadSeconds = .06f;
        private long _speechReported;
        private bool _startPending;
        private bool _gateClosed;

        /// <summary>Starts each speech generation blocked before its first PCM callback. Applies to future generations.</summary>
        public bool StartBlocked { get; set; }

        /// <summary>Speech written ahead of the play position, in seconds (default 0.06). Applies from the next speech.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
        public float LeadSeconds
        {
            get => _leadSeconds;
            set => _leadSeconds = value > 0 && !float.IsInfinity(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        /// <summary>Times the current speech's play position overtook the written speech (main-thread stalls).</summary>
        public int Underruns => _ring?.Underruns ?? 0;

        /// <summary>Gets the current generation's publication mailbox, or null between speech sessions.</summary>
        public DissonancePathMailbox Parameters => _decoder?.Pump?.Parameters;

        /// <summary>Configures the main-thread renderer factory and copied fixed path parameters.</summary>
        public void Configure(Func<int, int, SteamAudioPathRenderer> createRenderer, float[] coefficients, SA.CoordinateSpace3 listener)
        {
            if (createRenderer == null) throw new ArgumentNullException(nameof(createRenderer));
            if (coefficients == null || coefficients.Length == 0) throw new ArgumentException("Path coefficients are required.", nameof(coefficients));
            RetireOutput();
            _factory = createRenderer;
            _coefficients = (float[])coefficients.Clone();
            _listener = listener;
        }

        /// <summary>Gets whether a retired reader still prevents SDK reset.</summary>
        public bool IsRetiring => _decoder != null && _decoder.IsRetiring;
        /// <summary>Gets the last processing or renderer-construction failure.</summary>
        public Exception Fault => _fault ?? _decoder?.Pump?.Fault;
        /// <summary>Gets dropped SDK commands during unavailable or retiring input.</summary>
        public long DroppedCommands => _decoder != null ? _decoder.DroppedCommands : 0;
        /// <summary>Gets callback entries for the most recent generation, including silent callbacks.</summary>
        public long CallbackCount => _lastPlayback?.ReadCalls ?? 0;
        /// <summary>Gets the largest interleaved callback length of the most recent generation.</summary>
        public int LargestCallbackSampleCount => _lastPlayback?.LargestCallbackSampleCount ?? 0;
        /// <summary>Gets peak SDK-decoded amplitude in the most recent generation; not a device audibility measure.</summary>
        public float PeakDecodedAmplitude => _lastPlayback?.PeakDecodedAmplitude ?? 0;
        /// <summary>Gets peak native left/right difference in the most recent generation.</summary>
        public float PeakStereoDifference => _lastPlayback?.PeakStereoDifference ?? 0;
        /// <inheritdoc/>
        public bool IsActive => this != null && isActiveAndEnabled;
        /// <inheritdoc/>
        public bool IsSpeaking
        {
            get { var pump = _decoder?.Pump; return IsActive && pump != null && !pump.HasOutputDrained && !IsRetiring; }
        }
        /// <inheritdoc/>
        public float Amplitude => IsActive && _decoder != null ? _decoder.Amplitude : 0;
        /// <inheritdoc/>
        public float Jitter => _decoder != null ? _decoder.Jitter : 0;
        /// <summary>Packet loss reporting is not provided by this initial output seam.</summary>
        public float? PacketLoss => null;
        /// <inheritdoc/>
        public ChannelPriority Priority
        {
            get { var pump = _decoder?.Pump; return IsActive && pump != null && !pump.HasOutputDrained && !IsRetiring ? pump.Priority : ChannelPriority.None; }
        }
        /// <inheritdoc/>
        public string PlayerName
        {
            get => _playerName;
            set
            {
                if (_playerName == value) return;
                RetireOutput();
                _playerName = value;
                ApplyIdentity();
            }
        }
        /// <inheritdoc/>
        public CodecSettings CodecSettings
        {
            get => _codec;
            set
            {
                if (_codec.Codec == value.Codec && _codec.FrameSize == value.FrameSize && _codec.SampleRate == value.SampleRate) return;
                RetireOutput();
                _codec = value;
                ApplyIdentity();
            }
        }
        /// <inheritdoc/>
        public bool IsMuted
        {
            get => _muted;
            set { _muted = value; if (_decoder != null) _decoder.IsMuted = value; }
        }
        /// <inheritdoc/>
        public float PlaybackVolume
        {
            get => _volume;
            set { _volume = value; if (_decoder != null) _decoder.PlaybackVolume = value; }
        }
        /// <inheritdoc/>
        public bool AllowPositionalPlayback
        {
            get => _positional;
            set { _positional = value; var pump = _decoder?.Pump; if (pump != null) pump.AllowPositionalPlayback = value; }
        }

        /// <inheritdoc/>
        public void Setup(IPriorityManager priority, IVolumeProvider volume)
        {
            if (_decoder == null)
            {
                var host = new GameObject("Bun3 Dissonance decoder");
                host.hideFlags = HideFlags.HideInHierarchy;
                DontDestroyOnLoad(host);
                _decoder = host.AddComponent<DissonanceDecoderHost>();
            }
            _decoder.Setup(priority, volume);
            ConfigureOutputSource();
            ApplyIdentity();
        }

        private void ConfigureOutputSource()
        {
            _source = GetComponent<AudioSource>();
            if (_source == null) _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialize = false;
            _source.spatialBlend = 0;
            _source.pitch = 1;
            _source.dopplerLevel = 0;
            _source.mute = false;
            _source.priority = 0;
            _source.ignoreListenerPause = true;
        }

        private void ApplyIdentity()
        {
            if (_decoder == null || _decoder.IsRetiring) return;
            _decoder.PlayerName = _playerName;
            _decoder.IsMuted = _muted;
            _decoder.PlaybackVolume = _volume;
            if (_codec.SampleRate > 0) ((IVoicePlaybackInternal)_decoder).CodecSettings = _codec;
        }

        /// <inheritdoc/>
        public void StartPlayback()
        {
            ApplyIdentity();
            if (_factory != null && IsActive && _codec.SampleRate > 0) _decoder?.StartInput();
        }
        /// <inheritdoc/>
        public void StopPlayback() => _decoder?.StopInput();
        /// <inheritdoc/>
        public void ReceiveAudioPacket(VoicePacket packet) => _decoder?.Receive(packet);
        /// <inheritdoc/>
        public void Reset() { IsMuted = false; PlaybackVolume = 1; }
        /// <inheritdoc/>
        public void ForceReset() => RetireOutput();
        /// <inheritdoc/>
        public void SetTransform(Vector3 position, Quaternion rotation) => transform.SetPositionAndRotation(position, rotation);
        /// <inheritdoc/>
        public void GetRemoteChannels(List<RemoteChannel> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (_decoder == null) output.Clear(); else _decoder.ReadChannels(output);
        }

        private void Update()
        {
            if (_decoder == null || _factory == null || _decoder.IsRetiring) return;
            ApplyIdentity();
            int rate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out int frame, out _);
            if (_clip != null && (_rate != rate || _frame != frame))
            {
                RetireOutput();
                return;
            }

            var current = _decoder.Pump;
            if (current != null)
            {
                UpdateCurrentOutput(current);
                return;
            }
            BeginNextOutput(rate, frame);
        }

        private void UpdateCurrentOutput(DissonancePathPlayback current)
        {
            if (current.Fault != null)
            {
                _fault = current.Fault;
                RetireOutput();
                return;
            }

            // The previous frame's LateUpdate publishes the initial acoustic parameters.
            if (_startPending)
            {
                _startPending = false;
                Fill(current, 0);
                _source.Play();
            }
            else if (!_source.isPlaying || _clip == null || _clip.samples != _ring.LengthFrames)
            {
                // A device reset stops the source and can empty the clip; resume the same utterance on a fresh ring.
                // A disabled source cannot play again, so it releases the speaker instead of rebuilding every frame.
                if (!_source.isActiveAndEnabled)
                {
                    RetireOutput();
                    return;
                }
                PrepareRing(_rate);
                Fill(current, 0);
                _source.Play();
            }
            else Fill(current, _source.timeSamples);
            if (!current.HasOutputDrained) return;

            _decoder.Retire(false);
            _source.Stop();
            _source.clip = null;
        }

        private void BeginNextOutput(int rate, int frame)
        {
            var session = _decoder.Dequeue(rate);
            if (!session.HasValue) return;
            try
            {
                var pump = _decoder.CreatePump(session.Value, _factory, rate, frame, checked(++_generation), _coefficients, _listener);
                pump.AllowPositionalPlayback = _positional;
                pump.Parameters.TrySetBlocked(pump.Generation, StartBlocked);
                _lastPlayback = pump;
                _decoder.Publish(session.Value, pump);
                _rate = rate;
                _frame = frame;
                PrepareRing(rate);
                _startPending = true;
            }
            catch (Exception error)
            {
                _fault = error;
                RetireOutput();
            }
        }

        // Starts a new ring at play frame 0 and reuses the clip unless a device reset emptied it.
        private void PrepareRing(int rate)
        {
            _ring = new VoiceClipRing(rate, _leadSeconds);
            _speechReported = 0;
            _gateClosed = false;
            if (_block == null || _block.Length != _ring.BlockFrames * 2) _block = new float[_ring.BlockFrames * 2];
            if (_clip != null && _clip.samples != _ring.LengthFrames) { Destroy(_clip); _clip = null; }
            if (_clip == null) _clip = AudioClip.Create("Bun3 speech ring", _ring.LengthFrames, 2, rate, false);
            _source.clip = _clip;
        }

        private void RetireOutput()
        {
            _startPending = false;
            _decoder?.Retire(true);
            if (_source != null)
            {
                _source.Stop();
                _source.clip = null;
            }
            if (_clip != null) Destroy(_clip);
            _clip = null;
            _ring = null;
        }

        // Main thread: renders the planned blocks ahead of the play position into the looping clip, so the audio thread
        // only plays native data. A stalled main thread plays the silence written after the lead.
        private void Fill(DissonancePathPlayback pump, int playFrame)
        {
            if (_ring == null || _clip == null || _clip.samples != _ring.LengthFrames) return;
            int count = _ring.Plan(playFrame, _writes);
            int block = _ring.BlockFrames;
            // Report only played speech, in blocks, so underrun gaps neither end the speech early nor widen the drain margin.
            for (long played = _ring.SpeechFramesPlayed; _speechReported < played;)
            {
                int frames = (int)Math.Min(block, played - _speechReported);
                pump.RecordDeliveredFrames(frames);
                _speechReported += frames;
            }
            Write(pump, count, block);
            // Rendering applies the gate only to new writes; when it closes, also silence speech already written past
            // the DSP buffer the mixer may hold, so rejected speech stops within about a frame like the old audio-thread gate.
            bool blocked = pump.Parameters.IsBlocked;
            if (blocked && !_gateClosed) Write(pump, _ring.SilenceAhead(_frame, _writes), block);
            _gateClosed = blocked;
        }

        private void Write(DissonancePathPlayback pump, int count, int block)
        {
            for (int i = 0; i < count; i++)
            {
                var write = _writes[i];
                if (write.Silence) Array.Clear(_block, 0, _block.Length);
                for (int offset = 0; offset < write.Count; offset += block)
                {
                    if (!write.Silence) pump.ReadStereo(_block);
                    _clip.SetData(_block, write.Start + offset);
                }
            }
        }

        private void OnDisable() => RetireOutput();
        private void OnDestroy()
        {
            RetireOutput();
            var decoder = _decoder;
            Volatile.Write(ref _decoder, null);
            if (decoder != null) decoder.Retire(true, true);
        }
    }
}
