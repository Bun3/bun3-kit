using System;
using System.Runtime.InteropServices;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>
    /// Renders caller-supplied path coefficients from mono PCM to binaural stereo using Steam Audio 4.8.1.
    /// Create outside the audio callback. Render and reset have one owner; quiesce that owner before disposal.
    /// This renderer does not simulate paths or retrieve simulator outputs.
    /// </summary>
    public sealed class SteamAudioPathRenderer : IDisposable
    {
        /// <summary>Creates a renderer with its own retained default native context and matching HRTF.</summary>
        public static SteamAudioPathRenderer CreateDefault(int sampleRate, int frameSize, int order = 1)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (frameSize <= 0 || frameSize > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(frameSize));
            if (order < 0 || order > 3) throw new ArgumentOutOfRangeException(nameof(order));
            var context = new SA.Context();
            SA.HRTF hrtf = null;
            try
            {
                hrtf = new SA.HRTF(context, new SA.AudioSettings { samplingRate = sampleRate, frameSize = frameSize },
                    null, null, 0, SA.HRTFNormType.None);
                return new SteamAudioPathRenderer(context, hrtf, sampleRate, frameSize, order);
            }
            finally { hrtf?.Release(); context.Release(); }
        }

        private IntPtr _context;
        private IntPtr _hrtf;
        private IntPtr _effect;
        private IntPtr _coefficients;
        private IntPtr _monoSamples;
        private IntPtr _leftSamples;
        private IntPtr _rightSamples;
        private NativePathAudio.AudioBuffer _input;
        private NativePathAudio.AudioBuffer _output;
        private readonly float[] _left;
        private readonly float[] _right;
        private readonly float[] _silence;
        private NativePathAudio.Parameters _lastParameters;
        private bool _needsInputFlush;
        private float _spatialBlend = 1, _targetSpatialBlend = 1;
        private bool _hasSpatialBlend;
        private readonly int _order;
        private bool _disposed;

        /// <summary>
        /// Creates fixed processing storage and retains the context and HRTF. The HRTF must have matching audio settings.
        /// Supported Ambisonics orders are zero through three. The native SDK must use its standard three-band ABI.
        /// </summary>
        public SteamAudioPathRenderer(SA.Context context, SA.HRTF hrtf, int sampleRate, int frameSize, int order = 1)
        {
            if (context == null || context.Get() == IntPtr.Zero) throw new ArgumentException("A live context is required.", nameof(context));
            if (hrtf == null || hrtf.Get() == IntPtr.Zero) throw new ArgumentException("A live HRTF is required.", nameof(hrtf));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (frameSize <= 0 || frameSize > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(frameSize));
            if (order < 0 || order > 3) throw new ArgumentOutOfRangeException(nameof(order));
            FrameSize = frameSize;
            SampleRate = sampleRate;
            CoefficientCount = (order + 1) * (order + 1);
            _order = order;
            _left = new float[frameSize];
            _right = new float[frameSize];
            _silence = new float[frameSize];
            try
            {
                _context = SA.API.iplContextRetain(context.Get());
                _hrtf = SA.API.iplHRTFRetain(hrtf.Get());
                var audio = new SA.AudioSettings { samplingRate = sampleRate, frameSize = frameSize };
                var settings = new NativePathAudio.Settings
                {
                    MaxOrder = order, Spatialize = 1,
                    SpeakerLayout = new NativePathAudio.SpeakerLayout { Type = 1 },
                    Hrtf = _hrtf
                };
                Check(NativePathAudio.Create(_context, ref audio, ref settings, out _effect), "create path effect");
                Check(NativePathAudio.AllocateBuffer(_context, 1, frameSize, out _input), "allocate input buffer");
                Check(NativePathAudio.AllocateBuffer(_context, 2, frameSize, out _output), "allocate output buffer");
                _monoSamples = Marshal.ReadIntPtr(_input.Data);
                _leftSamples = Marshal.ReadIntPtr(_output.Data);
                _rightSamples = Marshal.ReadIntPtr(_output.Data, IntPtr.Size);
                _coefficients = Marshal.AllocHGlobal(CoefficientCount * sizeof(float));
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Gets the exact number of mono samples consumed per call.</summary>
        public int FrameSize { get; }

        /// <summary>Gets the sample rate fixed at construction.</summary>
        public int SampleRate { get; }

        /// <summary>Gets the required number of world-space spherical harmonic coefficients.</summary>
        public int CoefficientCount { get; }

        /// <summary>
        /// Renders exactly one frame into left/right interleaved stereo without managed allocation.
        /// Arrays must exactly match the configured sizes. SH values use Steam Audio's native convention.
        /// Listener coordinates must be finite and orthonormal in Steam Audio space. Gain and EQ must be finite and nonnegative.
        /// Gain is applied once after native processing; zero gain writes silence while advancing effect state.
        /// </summary>
        public void Render(float[] mono, float[] interleavedStereo, float[] shCoefficients,
            float eqLow, float eqMid, float eqHigh, SA.CoordinateSpace3 listener, float gain = 1, bool normalizeEq = false)
            => RenderSpatial(mono, interleavedStereo, shCoefficients, eqLow, eqMid, eqHigh, listener, gain, normalizeEq, 1);

        /// <summary>
        /// Renders native path audio with stereo width from zero (identical channels) to one (full binaural).
        /// Collapses the processed mid/side signal, preserving path EQ and attenuation without dry bypass.
        /// Width changes slew over at most 20 ms; the first frame and reset start at the requested width.
        /// </summary>
        public void RenderSpatial(float[] mono, float[] interleavedStereo, float[] shCoefficients,
            float eqLow, float eqMid, float eqHigh, SA.CoordinateSpace3 listener, float gain, bool normalizeEq, float spatialBlend)
        {
            EnsureAlive();
            ValidateFinite(spatialBlend, nameof(spatialBlend));
            if (spatialBlend < 0 || spatialBlend > 1) throw new ArgumentOutOfRangeException(nameof(spatialBlend));
            ValidateArray(mono, FrameSize, nameof(mono));
            ValidateArray(interleavedStereo, FrameSize * 2, nameof(interleavedStereo));
            ValidateArray(shCoefficients, CoefficientCount, nameof(shCoefficients));
            ValidateNonnegative(eqLow, nameof(eqLow)); ValidateNonnegative(eqMid, nameof(eqMid));
            ValidateNonnegative(eqHigh, nameof(eqHigh)); ValidateNonnegative(gain, nameof(gain));
            ValidateCoordinates(listener);
            for (int i = 0; i < mono.Length; i++) ValidateFinite(mono[i], nameof(mono));
            for (int i = 0; i < shCoefficients.Length; i++) ValidateFinite(shCoefficients[i], nameof(shCoefficients));
            Marshal.Copy(mono, 0, _monoSamples, FrameSize);
            Marshal.Copy(shCoefficients, 0, _coefficients, CoefficientCount);
            var parameters = new NativePathAudio.Parameters
            {
                EqLow = eqLow, EqMid = eqMid, EqHigh = eqHigh,
                ShCoefficients = _coefficients, Order = _order, Binaural = 1,
                Hrtf = _hrtf, Listener = listener, NormalizeEq = normalizeEq ? 1 : 0
            };
            NativePathAudio.Apply(_effect, ref parameters, ref _input, ref _output);
            _lastParameters = parameters;
            _targetSpatialBlend = spatialBlend;
            if (!_hasSpatialBlend) { _spatialBlend = spatialBlend; _hasSpatialBlend = true; }
            _needsInputFlush = true;
            CopyStereo(interleavedStereo, gain);
        }

        /// <summary>Gets remaining processing samples, including a pending terminal input-overlap flush.</summary>
        public int TailSamplesRemaining
        {
            get { EnsureAlive(); return NativePathAudio.GetTailSize(_effect) + (_needsInputFlush ? FrameSize : 0); }
        }

        /// <summary>
        /// Flushes terminal input overlap once, then drains native tail frames. Returns true when no tail remains.
        /// The returned frame must still be consumed even when true is returned.
        /// </summary>
        public bool RenderTail(float[] interleavedStereo, float gain = 1)
        {
            EnsureAlive();
            ValidateArray(interleavedStereo, FrameSize * 2, nameof(interleavedStereo));
            ValidateNonnegative(gain, nameof(gain));
            // Steam Audio 4.8.1 keeps frameSize/4 dry samples outside the GetTail overlap buffer.
            // One final zero-input Apply renders those samples before GetTail drains the convolution.
            if (_needsInputFlush)
            {
                Marshal.Copy(_silence, 0, _monoSamples, FrameSize);
                NativePathAudio.Apply(_effect, ref _lastParameters, ref _input, ref _output);
                _needsInputFlush = false;
                CopyStereo(interleavedStereo, gain);
                return NativePathAudio.GetTailSize(_effect) == 0;
            }
            if (NativePathAudio.GetTailSize(_effect) == 0)
            {
                Array.Clear(interleavedStereo, 0, interleavedStereo.Length);
                return true;
            }
            int state = NativePathAudio.GetTail(_effect, ref _output);
            CopyStereo(interleavedStereo, gain);
            return state == 1;
        }

        private void CopyStereo(float[] interleavedStereo, float gain)
        {
            Marshal.Copy(_leftSamples, _left, 0, FrameSize);
            Marshal.Copy(_rightSamples, _right, 0, FrameSize);
            for (int i = 0; i < FrameSize; i++)
            {
                float step = 1f / Math.Max(1, SampleRate * .02f);
                if (_spatialBlend < _targetSpatialBlend) _spatialBlend = Math.Min(_targetSpatialBlend, _spatialBlend + step);
                else if (_spatialBlend > _targetSpatialBlend) _spatialBlend = Math.Max(_targetSpatialBlend, _spatialBlend - step);
                float left = _left[i], right = _right[i];
                if (_spatialBlend != 1)
                {
                    float mid = (left + right) * .5f;
                    float side = (left - right) * (.5f * _spatialBlend);
                    left = mid + side; right = mid - side;
                }
                interleavedStereo[2 * i] = gain == 0 ? 0 : left * gain;
                interleavedStereo[2 * i + 1] = gain == 0 ? 0 : right * gain;
            }
        }

        /// <summary>Clears native filter and convolution history. Call only from the processing owner.</summary>
        public void Reset()
        {
            EnsureAlive();
            NativePathAudio.Reset(_effect);
            _needsInputFlush = false;
            _hasSpatialBlend = false;
            _spatialBlend = _targetSpatialBlend = 1;
        }

        /// <summary>Releases all retained native resources. The caller must first stop all processing calls.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_effect != IntPtr.Zero) NativePathAudio.Release(ref _effect);
            if (_input.Data != IntPtr.Zero) NativePathAudio.FreeBuffer(_context, ref _input);
            if (_output.Data != IntPtr.Zero) NativePathAudio.FreeBuffer(_context, ref _output);
            if (_coefficients != IntPtr.Zero) { Marshal.FreeHGlobal(_coefficients); _coefficients = IntPtr.Zero; }
            if (_hrtf != IntPtr.Zero) SA.API.iplHRTFRelease(ref _hrtf);
            if (_context != IntPtr.Zero) SA.API.iplContextRelease(ref _context);
        }

        private static void Check(SA.Error error, string operation)
        {
            if (error != SA.Error.Success) throw new InvalidOperationException("Steam Audio could not " + operation + ": " + error);
        }

        private void EnsureAlive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SteamAudioPathRenderer));
        }

        private static void ValidateArray(float[] values, int count, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            if (values.Length != count) throw new ArgumentException("Buffer length must match the configured frame or coefficient count.", name);
        }

        private static void ValidateFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(name);
        }

        private static void ValidateNonnegative(float value, string name)
        {
            ValidateFinite(value, name);
            if (value < 0) throw new ArgumentOutOfRangeException(name);
        }

        private static double Dot(SA.Vector3 a, SA.Vector3 b) => (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;

        private static void ValidateCoordinates(SA.CoordinateSpace3 space)
        {
            ValidateFinite(space.origin.x, nameof(space)); ValidateFinite(space.origin.y, nameof(space)); ValidateFinite(space.origin.z, nameof(space));
            double rightLength = Dot(space.right, space.right);
            double upLength = Dot(space.up, space.up);
            double aheadLength = Dot(space.ahead, space.ahead);
            if (!(Math.Abs(rightLength - 1) < 0.001 && Math.Abs(upLength - 1) < 0.001 && Math.Abs(aheadLength - 1) < 0.001 &&
                  Math.Abs(Dot(space.right, space.up)) < 0.001 && Math.Abs(Dot(space.right, space.ahead)) < 0.001 &&
                  Math.Abs(Dot(space.up, space.ahead)) < 0.001))
                throw new ArgumentException("Listener axes must be finite and orthonormal.", nameof(space));
        }
    }
}
