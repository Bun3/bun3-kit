using System;
using System.Threading;
using UnityEngine;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Optional positional SoundSystem output. Prepare clips and native resources before playing.</summary>
    public sealed class SteamAudioSoundOutput : ISpatialSoundVoiceOutput, IResolvedSoundAcousticSettings
    {
        private readonly AudioSource _source;
        private readonly SteamAudioClipCache _clips;
        private readonly SteamAudioPathRenderer _renderer;
        private readonly PathParameterMailbox _mailbox;
        private readonly float[] _mono, _stereo, _coefficients;
        private readonly int _rate, _frame;
        private AudioClip _driverClip;
        private SteamAudioSoundDriver _driver;
        private SteamAudioSoundRetirementHost _retirement;
        private SteamAudioClipCache.Pcm _pcm;
        private PathParameterLease _lease;
        private PathRenderSettings _settings;
        private long _generation;
        private double _position;
        private int _offset;
        private float _pitch = 1;
        private float _maxDistance, _minDistance;
        private bool _loop, _inputEnded, _terminal;
        private volatile bool _passthrough = true;
        // 0 idle, 1 active, 2 active reader, 3 retired, 4 retired reader, 5 disposed.
        private int _state;
        private int _complete;
        private int _disposeRequested;
        private int _failure;
        private Exception _fault;

        /// <summary>Prewarms all per-source managed/native storage and the looping driver. Main thread only.</summary>
        public SteamAudioSoundOutput(AudioSource source, SteamAudioClipCache clips,
            Func<int, int, SteamAudioPathRenderer> createRenderer = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _clips = clips ?? throw new ArgumentNullException(nameof(clips));
            _source = source;
            _rate = AudioSettings.outputSampleRate;
            AudioSettings.GetDSPBufferSize(out _frame, out _);
            try
            {
                _renderer = createRenderer != null ? createRenderer(_rate, _frame) : SteamAudioPathRenderer.CreateDefault(_rate, _frame);
                if (_renderer == null || _renderer.SampleRate != _rate || _renderer.FrameSize != _frame)
                    throw new ArgumentException("The factory must provide a matching live renderer.", nameof(createRenderer));
                _mono = new float[_frame];
                _stereo = new float[_frame * 2];
                _coefficients = new float[_renderer.CoefficientCount];
                _coefficients[0] = .2820948f;
                _settings = new PathRenderSettings(new SA.CoordinateSpace3
                {
                    right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
                });
                _renderer.Render(_mono, _stereo, _coefficients, 1, 1, 1, _settings.Listener);
                _renderer.Reset();
                _mailbox = new PathParameterMailbox(_coefficients.Length);
                _driverClip = AudioClip.Create("Bun3 native SFX driver", _frame, 2, _rate, false);
                Array.Fill(_stereo, 1f);
                _driverClip.SetData(_stereo, 0);
                Array.Clear(_stereo, 0, _stereo.Length);
                _driver = source.gameObject.AddComponent<SteamAudioSoundDriver>();
                _driver.Bind(this);
                var host = new GameObject("Bun3 native sound retirement");
                host.hideFlags = HideFlags.HideInHierarchy;
                UnityEngine.Object.DontDestroyOnLoad(host);
                _retirement = host.AddComponent<SteamAudioSoundRetirementHost>();
                _retirement.Bind(this);
            }
            catch
            {
                _renderer?.Dispose();
                if (_driverClip != null) UnityEngine.Object.Destroy(_driverClip);
                if (_driver != null) UnityEngine.Object.Destroy(_driver);
                throw;
            }
        }

        SoundDef _definition;
        /// <inheritdoc/>
        public bool IsAvailable => _definition != null;
        /// <inheritdoc/>
        public SoundAcousticSettings Acoustics => _definition != null
            ? _definition.EffectiveAcoustics
            : SoundAcousticSettings.Default;
        /// <summary>Gets the active sound's distance-attenuation option.</summary>
        public bool DistanceAttenuation => _definition != null && Acoustics.DistanceAttenuation;
        /// <summary>Gets the active sound's native distance profile.</summary>
        public DistanceAttenuationProfile AttenuationProfile => _definition != null ? Acoustics.AttenuationProfile : null;

        /// <summary>Whether this sound follows the owning acoustic world's stereo-width defaults.</summary>
        public bool InheritSpatialBlend => _definition == null || Acoustics.InheritSpatialBlend;
        /// <summary>Live sound-specific stereo-width profile, used when inheritance is disabled.</summary>
        public SpatialBlendProfile SpatialBlendProfile => _definition != null ? Acoustics.SpatialBlendProfile : null;
        /// <summary>Sound-specific inline mono distance when no profile is selected.</summary>
        public float MonoDistance => _definition != null ? Acoustics.MonoDistance : 0;
        /// <summary>Sound-specific inline full spatial distance when no profile is selected.</summary>
        public float FullSpatialDistance => _definition != null ? Acoustics.FullSpatialDistance : 0;

        /// <summary>Blocks each new positional generation before the first source-filter invocation.</summary>
        public bool StartBlocked { get; set; } = true;
        /// <summary>Gets an immutable producer token only while this source owns an active positional request.</summary>
        public PathParameterLease CurrentParameters => Volatile.Read(ref _state) is 1 or 2 ? _lease : default;
        /// <summary>Gets the active request's authored range; native SH already supplies path-distance attenuation.</summary>
        public float MaxDistance => CurrentParameters.IsValid ? _maxDistance : 0;
        /// <summary>Gets the active request's nonnegative minimum for the native simulation distance model; inactive requests return zero.</summary>
        public float MinDistance => CurrentParameters.IsValid ? _minDistance : 0;
        /// <summary>Gets completion after native tails and one subsequent filter block, or after an output failure.</summary>
        public bool IsComplete => Volatile.Read(ref _complete) != 0 || Volatile.Read(ref _failure) != 0;
        /// <summary>Gets a processing failure caught on the audio owner, if any.</summary>
        public Exception Fault => Volatile.Read(ref _fault);
        /// <summary>Gets the last input/format/processing failure. Reader retirement alone does not set a failure.</summary>
        public SteamAudioSoundFailure Failure => (SteamAudioSoundFailure)Volatile.Read(ref _failure);

        /// <inheritdoc/>
        public VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch)
            => TryStart(definition, selectedClip, logicalPitch, definition != null ? definition.EffectiveSpatial : SpatialMode.None);

        /// <inheritdoc/>
        public VoiceOutputStartResult TryStart(SoundDef definition, AudioClip selectedClip, float logicalPitch, SpatialMode spatial)
        {
            Retire();
            if (Volatile.Read(ref _disposeRequested) != 0 || Volatile.Read(ref _state) == 5)
                return VoiceOutputStartResult.Unavailable;
            if (!TryReclaim()) return VoiceOutputStartResult.Unavailable;
            Volatile.Write(ref _failure, 0);
            _fault = null;
            if (definition == null) return Fail(SteamAudioSoundFailure.InvalidRequest);
            if (spatial == SpatialMode.None)
            {
                _passthrough = true;
                return VoiceOutputStartResult.Unsupported;
            }
            _passthrough = false;
            if (!FormatMatches()) return Fail(SteamAudioSoundFailure.OutputFormatChanged);
            if (!_clips.TryGet(selectedClip, out var pcm)) return Fail(SteamAudioSoundFailure.ClipNotPrepared);
            if (!Finite(logicalPitch)) return Fail(SteamAudioSoundFailure.InvalidPitch);
            var acoustics = definition.EffectiveAcoustics;
            if (!Finite(acoustics.MinDistance)) return Fail(SteamAudioSoundFailure.InvalidRequest);
            _definition = definition;
            _pcm = pcm;
            _loop = definition.EffectiveLoop;
            _maxDistance = acoustics.MaxDistance;
            _minDistance = Math.Max(0, acoustics.MinDistance);
            _position = 0;
            _offset = _stereo.Length;
            _inputEnded = false;
            _terminal = false;
            Volatile.Write(ref _complete, 0);
            Volatile.Write(ref _pitch, Mathf.Clamp(logicalPitch, 0, 3));
            _lease = _mailbox.BeginGeneration(checked(++_generation));
            Array.Clear(_coefficients, 0, _coefficients.Length);
            _coefficients[0] = .2820948f;
            _settings = new PathRenderSettings(new SA.CoordinateSpace3
            {
                right = new SA.Vector3 { x = 1 }, up = new SA.Vector3 { y = 1 }, ahead = new SA.Vector3 { z = -1 }
            });
            _lease.TryPublish(_coefficients, _settings);
            _lease.TrySetBlocked(StartBlocked);
            _source.clip = _driverClip;
            _source.loop = true;
            _source.pitch = 1;
            _source.spatialBlend = 0;
            _source.spatialize = false;
            _source.dopplerLevel = 0;
            _source.panStereo = 0;
            Volatile.Write(ref _state, 1);
            return VoiceOutputStartResult.Started;
        }

        /// <summary>Changes forward dry-PCM pitch before HRTF processing. Zero freezes progress; finite values clamp to 0..3.</summary>
        public void SetPitch(float logicalPitch)
        {
            if (!Finite(logicalPitch)) { Fail(SteamAudioSoundFailure.InvalidPitch); Retire(); return; }
            Volatile.Write(ref _pitch, Mathf.Clamp(logicalPitch, 0, 3));
        }

        /// <summary>Closes reader admission and the output gate without waiting. Call before source stop/reconfiguration.</summary>
        public void Retire()
        {
            _mailbox?.Retire(_generation);
            int state;
            do
            {
                state = Volatile.Read(ref _state);
                if (state >= 3) return;
            } while (Interlocked.CompareExchange(ref _state, state == 2 ? 4 : 3, state) != state);
        }

        private bool TryReclaim()
        {
            int state = Volatile.Read(ref _state);
            if (state == 0) return true;
            if (Interlocked.CompareExchange(ref _state, 0, 3) != 3) return false;
            _renderer.Reset();
            _pcm = null;
            return true;
        }

        // The same claim is used by the source callback and deterministic ownership-protocol tests.
        internal bool TryClaimReader() => Interlocked.CompareExchange(ref _state, 2, 1) == 1;
        internal bool ReleaseReader()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 2) != 4) return true;
            Volatile.Write(ref _state, 3);
            return false;
        }

        internal void Process(float[] samples, int channels)
        {
            if (!TryClaimReader())
            {
                if (!_passthrough) Array.Clear(samples, 0, samples.Length);
                return;
            }
            try
            {
                if (channels != 2 || (samples.Length & 1) != 0)
                {
                    Fail(SteamAudioSoundFailure.UnsupportedOutputChannels);
                    Retire();
                    Array.Clear(samples, 0, samples.Length);
                    return;
                }
                if (Volatile.Read(ref _pitch) == 0)
                {
                    Array.Clear(samples, 0, samples.Length);
                    return;
                }
                if (_terminal)
                {
                    Array.Clear(samples, 0, samples.Length);
                    Volatile.Write(ref _complete, 1);
                    return;
                }
                MixRenderedFrames(samples);
            }
            catch (Exception error)
            {
                Volatile.Write(ref _fault, error);
                Fail(SteamAudioSoundFailure.ProcessingFailed);
                Retire();
            }
            finally
            {
                if (_lease.IsBlocked) Array.Clear(samples, 0, samples.Length);
                if (!ReleaseReader()) Array.Clear(samples, 0, samples.Length);
            }
        }

        private void MixRenderedFrames(float[] samples)
        {
            int written = 0;
            while (written < samples.Length && Volatile.Read(ref _state) == 2)
            {
                if (_offset == _stereo.Length)
                {
                    if (!RenderNextFrame())
                    {
                        _terminal = true;
                        Array.Clear(samples, written, samples.Length - written);
                        break;
                    }
                    _offset = 0;
                }
                int count = Math.Min(samples.Length - written, _stereo.Length - _offset);
                // Preserve the incoming flatline driver envelope (Unity source/mixer gain) exactly once.
                for (int i = 0; i < count; i++) samples[written + i] *= _stereo[_offset + i];
                written += count;
                _offset += count;
            }
        }

        private bool RenderNextFrame()
        {
            if (_mailbox.TryRead(_generation, _coefficients, out var settings)) _settings = settings;
            if (_inputEnded)
            {
                if (_renderer.TailSamplesRemaining == 0) return false;
                _renderer.RenderTail(_stereo, _settings.Gain);
                return true;
            }
            FillPitchedMonoFrame();
            _renderer.RenderSpatial(_mono, _stereo, _coefficients, _settings.EqLow, _settings.EqMid, _settings.EqHigh,
                _settings.Listener, _settings.Gain, _settings.NormalizeEq, _settings.SpatialBlend);
            return true;
        }

        private void FillPitchedMonoFrame()
        {
            double step = (double)_pcm.Rate / _rate * Volatile.Read(ref _pitch);
            for (int i = 0; i < _mono.Length; i++)
            {
                if (_position >= _pcm.Frames)
                {
                    if (_loop) _position %= _pcm.Frames;
                    else
                    {
                        Array.Clear(_mono, i, _mono.Length - i);
                        _inputEnded = true;
                        break;
                    }
                }
                int frame = (int)_position;
                int next = frame + 1;
                float currentSample = ReadMono(frame);
                float nextSample = ReadNextMonoSample(next);
                float fraction = (float)(_position - frame);
                _mono[i] = currentSample + (nextSample - currentSample) * fraction;
                _position += step;
            }
            if (!_loop && _position >= _pcm.Frames) _inputEnded = true;
        }

        private float ReadNextMonoSample(int nextFrame)
        {
            if (nextFrame < _pcm.Frames) return ReadMono(nextFrame);
            return _loop ? ReadMono(0) : 0;
        }

        private float ReadMono(int frame)
        {
            int index = frame * _pcm.Channels;
            return _pcm.Channels == 1 ? _pcm.Samples[index] : .5f * (_pcm.Samples[index] + _pcm.Samples[index + 1]);
        }

        private VoiceOutputStartResult Fail(SteamAudioSoundFailure failure)
        {
            Volatile.Write(ref _failure, (int)failure);
            return VoiceOutputStartResult.Unavailable;
        }

        private bool FormatMatches()
        {
            AudioSettings.GetDSPBufferSize(out int frame, out _);
            return AudioSettings.outputSampleRate == _rate && frame == _frame;
        }

        internal void ControlUpdate()
        {
            if (Volatile.Read(ref _disposeRequested) != 0) { TryFinalizeDisposal(); return; }
            if (Volatile.Read(ref _state) is 1 or 2 && !FormatMatches())
            {
                Fail(SteamAudioSoundFailure.OutputFormatChanged);
                Retire();
            }
        }

        /// <summary>Retires immediately; native release is deferred to the independent main-thread host while a reader is held.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeRequested, 1) != 0) return;
            Retire();
            if (_source != null && _source.clip == _driverClip) { _source.Stop(); _source.clip = null; }
            TryFinalizeDisposal();
        }

        private void TryFinalizeDisposal()
        {
            int state = Volatile.Read(ref _state);
            if (state is not (0 or 3) || Interlocked.CompareExchange(ref _state, 5, state) != state) return;
            _renderer.Dispose();
            _pcm = null;
            if (_driverClip != null) UnityEngine.Object.Destroy(_driverClip);
            _driverClip = null;
            if (_retirement != null) UnityEngine.Object.Destroy(_retirement.gameObject);
            _retirement = null;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Nonallocating native output failure reasons; unavailable positional requests never use dry fallback.</summary>
    public enum SteamAudioSoundFailure
    {
        /// <summary>No classified input, format or processing failure.</summary>
        None,
        /// <summary>The requested sound definition is unavailable.</summary>
        InvalidRequest,
        /// <summary>The selected positional clip was not prepared in the cold PCM cache.</summary>
        ClipNotPrepared,
        /// <summary>The requested pitch was NaN or infinite.</summary>
        InvalidPitch,
        /// <summary>The output rate or DSP frame size changed; construct a matching replacement pool.</summary>
        OutputFormatChanged,
        /// <summary>The source filter received a callback format other than complete stereo sample pairs.</summary>
        UnsupportedOutputChannels,
        /// <summary>The processing owner captured an unexpected exception in Fault.</summary>
        ProcessingFailed
    }
}
