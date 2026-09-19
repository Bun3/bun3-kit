using System;
using System.Threading;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Copied native path-rendering scalars. Gain excludes bus volume and SH distance attenuation.</summary>
    public readonly struct PathRenderSettings
    {
        /// <summary>Gets the native-space orthonormal listener coordinates.</summary>
        public readonly SA.CoordinateSpace3 Listener;
        /// <summary>Gets the low-band native path EQ coefficient.</summary>
        public readonly float EqLow;
        /// <summary>Gets the middle-band native path EQ coefficient.</summary>
        public readonly float EqMid;
        /// <summary>Gets the high-band native path EQ coefficient.</summary>
        public readonly float EqHigh;
        /// <summary>Gets additional path mix gain, excluding existing bus volume and SH distance attenuation.</summary>
        public readonly float Gain;
        /// <summary>Gets whether the native effect normalizes path EQ.</summary>
        public readonly bool NormalizeEq;
        /// <summary>Gets stereo width: zero gives identical channels, one retains full native binaural output.</summary>
        public readonly float SpatialBlend;
        /// <summary>Creates a value snapshot; publication validates all numbers and listener axes.</summary>
        public PathRenderSettings(SA.CoordinateSpace3 listener, float eqLow = 1, float eqMid = 1,
            float eqHigh = 1, float gain = 1, bool normalizeEq = false, float spatialBlend = 1)
        { Listener = listener; EqLow = eqLow; EqMid = eqMid; EqHigh = eqHigh; Gain = gain; NormalizeEq = normalizeEq; SpatialBlend = spatialBlend; }
    }

    /// <summary>Immutable producer token; reused storage never grants old producers a new generation.</summary>
    public readonly struct PathParameterLease
    {
        private readonly PathParameterMailbox _owner;
        internal PathParameterLease(PathParameterMailbox owner, long generation) { _owner = owner; Generation = generation; }
        /// <summary>Gets the immutable generation captured by this producer token.</summary>
        public long Generation { get; }
        /// <summary>Gets whether this token still owns the storage's active generation.</summary>
        public bool IsValid => _owner != null && _owner.IsCurrent(Generation);
        /// <summary>Gets the immediate blocked state; default, stale and retired tokens always report blocked.</summary>
        public bool IsBlocked => _owner == null || _owner.IsBlockedFor(Generation);
        /// <summary>Copies one coherent snapshot from the single control writer; stale tokens return false.</summary>
        public bool TryPublish(float[] coefficients, PathRenderSettings settings) => _owner != null && _owner.TryPublish(Generation, coefficients, settings);
        /// <summary>Changes the current generation's immediate output gate without changing its snapshot.</summary>
        public bool TrySetBlocked(bool blocked) => _owner != null && _owner.TrySetBlocked(Generation, blocked);
    }

    /// <summary>Preallocated single-control-writer, single-processing-reader storage. Reuse requires retirement and reader quiescence.</summary>
    public sealed class PathParameterMailbox
    {
        private sealed class Slot
        {
            internal readonly float[] Coefficients;
            internal PathRenderSettings Settings;
            internal Slot(int count) { Coefficients = new float[count]; }
        }
        private readonly Slot[] _slots;
        private readonly int _count;
        private int _published = -1;
        private int _pinned = -1;
        private int _reading;
        // Bit zero blocks output; bit one permanently retires this generation.
        private int _gate = 3;
        private long _generation;
#if UNITY_EDITOR
        internal Action BeforeReaderClaimForTests;
#endif

        /// <summary>Allocates three snapshots once. Output starts blocked until explicitly allowed.</summary>
        public PathParameterMailbox(int coefficientCount)
        {
            if (coefficientCount <= 0 || coefficientCount > 16) throw new ArgumentOutOfRangeException(nameof(coefficientCount));
            _count = coefficientCount;
            _slots = new[] { new Slot(coefficientCount), new Slot(coefficientCount), new Slot(coefficientCount) };
        }
        /// <summary>Gets the current storage generation. Producers should retain their immutable lease token.</summary>
        public long Generation => Interlocked.Read(ref _generation);
        /// <summary>Gets the independent immediate output gate, including permanent retirement.</summary>
        public bool IsBlocked => Volatile.Read(ref _gate) != 0;

        /// <summary>
        /// Copies and publishes all fields atomically for one control-thread writer. Never pass retained native pointers.
        /// Returns false for a stale or retired owner. Publication does not change the immediate blocked gate.
        /// Gain excludes SDK user/bus volume and any distance attenuation already represented by SH coefficients.
        /// </summary>
        public bool TryPublish(long generation, float[] coefficients, PathRenderSettings settings)
        {
            if (generation != Generation || (Volatile.Read(ref _gate) & 2) != 0) return false;
            ValidateBuffer(coefficients);
            for (int i = 0; i < coefficients.Length; i++) Finite(coefficients[i]);
            Nonnegative(settings.EqLow); Nonnegative(settings.EqMid); Nonnegative(settings.EqHigh); Nonnegative(settings.Gain);
            Finite(settings.SpatialBlend);
            if (settings.SpatialBlend < 0 || settings.SpatialBlend > 1) throw new ArgumentOutOfRangeException(nameof(settings));
            var listener = settings.Listener;
            Finite(listener.origin.x); Finite(listener.origin.y); Finite(listener.origin.z);
            if (!(Math.Abs(Dot(listener.right, listener.right) - 1) < .001 && Math.Abs(Dot(listener.up, listener.up) - 1) < .001 &&
                Math.Abs(Dot(listener.ahead, listener.ahead) - 1) < .001 && Math.Abs(Dot(listener.right, listener.up)) < .001 &&
                Math.Abs(Dot(listener.right, listener.ahead)) < .001 && Math.Abs(Dot(listener.up, listener.ahead)) < .001))
                throw new ArgumentException("Listener axes must be finite and orthonormal.", nameof(settings));
            int published = Volatile.Read(ref _published);
            int pinned = Volatile.Read(ref _pinned);
            int target = 0;
            while (target == published || target == pinned) target++;
            var slot = _slots[target];
            Array.Copy(coefficients, slot.Coefficients, _count);
            slot.Settings = settings;
            Volatile.Write(ref _published, target);
            return (Volatile.Read(ref _gate) & 2) == 0;
        }

        /// <summary>
        /// Copies a coherent snapshot into caller-owned fixed storage. Exactly one processing owner is supported;
        /// concurrent callers return false. A racing publication may return false without waiting; retain the last
        /// successful audio-owned snapshot. No returned array aliases publication storage.
        /// </summary>
        public bool TryRead(long generation, float[] coefficients, out PathRenderSettings settings)
        {
            settings = default;
            ValidateBuffer(coefficients);
            if (generation != Generation || (Volatile.Read(ref _gate) & 2) != 0) return false;
#if UNITY_EDITOR
            BeforeReaderClaimForTests?.Invoke();
#endif
            if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0) return false;
            try
            {
                // The owner may have changed while a reader was delayed before its claim.
                if (generation != Generation || (Volatile.Read(ref _gate) & 2) != 0) return false;
                int published = Volatile.Read(ref _published);
                if (published < 0) return false;
                Interlocked.Exchange(ref _pinned, published);
                if (published != Volatile.Read(ref _published)) return false;
                var slot = _slots[published];
                Array.Copy(slot.Coefficients, coefficients, _count);
                settings = slot.Settings;
                return (Volatile.Read(ref _gate) & 2) == 0;
            }
            finally { Volatile.Write(ref _pinned, -1); Volatile.Write(ref _reading, 0); }
        }

        /// <summary>Updates the final output gate independently of snapshot publication. Retired owners cannot reopen it.</summary>
        public bool TrySetBlocked(long generation, bool blocked)
        {
            if (generation != Generation) return false;
            int state;
            do
            {
                state = Volatile.Read(ref _gate);
                if ((state & 2) != 0) return false;
            } while (Interlocked.CompareExchange(ref _gate, blocked ? 1 : 0, state) != state);
            return true;
        }
        /// <summary>Permanently blocks output and rejects future reads/publications for this generation.</summary>
        public void Retire(long generation)
        {
            if (generation == Generation) Interlocked.Exchange(ref _gate, 3);
        }

        /// <summary>Begins a strictly newer generation after retirement and processing-reader release. Control thread only.</summary>
        public PathParameterLease BeginGeneration(long generation)
        {
            if (generation <= Generation) throw new ArgumentOutOfRangeException(nameof(generation));
            if ((Volatile.Read(ref _gate) & 2) == 0 || Interlocked.CompareExchange(ref _reading, -1, 0) != 0)
                throw new InvalidOperationException("Retire and release the reader before reusing parameter storage.");
            try
            {
                // Reserve admission, rather than observing an idle reader and then racing its CAS.
                Volatile.Write(ref _published, -1);
                Volatile.Write(ref _pinned, -1);
                Interlocked.Exchange(ref _generation, generation);
                Volatile.Write(ref _gate, 1);
                return new PathParameterLease(this, generation);
            }
            finally { Volatile.Write(ref _reading, 0); }
        }

        internal bool IsCurrent(long generation) => generation == Generation && (Volatile.Read(ref _gate) & 2) == 0;
        internal bool IsBlockedFor(long generation) => generation != Generation || IsBlocked;

        private void ValidateBuffer(float[] coefficients)
        {
            if (coefficients == null) throw new ArgumentNullException(nameof(coefficients));
            if (coefficients.Length != _count) throw new ArgumentException("Coefficient count must match the mailbox.", nameof(coefficients));
        }
        private static void Finite(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        }
        private static void Nonnegative(float value) { Finite(value); if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
        private static double Dot(SA.Vector3 a, SA.Vector3 b) => (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
    }
}
