using System;
using System.Threading;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.Dissonance.SteamAudio
{
    /// <summary>One coherent path-rendering parameter value; gain is additional path mix gain.</summary>
    public readonly struct PathPlaybackSettings
    {
        /// <summary>Gets the native-space orthonormal listener coordinates.</summary>
        public readonly SA.CoordinateSpace3 Listener;
        /// <summary>Gets the low-band native path EQ coefficient.</summary>
        public readonly float EqLow;
        /// <summary>Gets the middle-band native path EQ coefficient.</summary>
        public readonly float EqMid;
        /// <summary>Gets the high-band native path EQ coefficient.</summary>
        public readonly float EqHigh;
        /// <summary>Gets additional path mix gain, excluding SDK/bus volume and SH distance attenuation.</summary>
        public readonly float Gain;
        /// <summary>Gets whether the native effect normalizes path EQ.</summary>
        public readonly bool NormalizeEq;
        /// <summary>Gets stereo width: zero gives identical channels, one retains full native binaural output.</summary>
        public readonly float SpatialBlend;
        /// <summary>Creates a value snapshot; publication validates all numbers and listener axes.</summary>
        public PathPlaybackSettings(SA.CoordinateSpace3 listener, float eqLow = 1, float eqMid = 1,
            float eqHigh = 1, float gain = 1, bool normalizeEq = false, float spatialBlend = 1)
        { Listener = listener; EqLow = eqLow; EqMid = eqMid; EqHigh = eqHigh; Gain = gain; NormalizeEq = normalizeEq; SpatialBlend = spatialBlend; }
    }

    /// <summary>One immutable speech generation over the shared native parameter mailbox implementation.</summary>
    public sealed class DissonancePathMailbox
    {
        private readonly Bun3.Unity.Audio.SteamAudio.PathParameterMailbox _storage;
        private readonly Bun3.Unity.Audio.SteamAudio.PathParameterLease _lease;
        /// <summary>Allocates the shared fixed-capacity storage for one immutable speech generation, initially blocked.</summary>
        public DissonancePathMailbox(long generation, int coefficientCount)
        {
            _storage = new Bun3.Unity.Audio.SteamAudio.PathParameterMailbox(coefficientCount);
            _lease = _storage.BeginGeneration(generation);
        }
        /// <summary>Gets the immutable speech generation.</summary>
        public long Generation => _lease.Generation;
        /// <summary>Gets the immediate output gate, including permanent retirement.</summary>
        public bool IsBlocked => _lease.IsBlocked;
        /// <summary>Copies a coherent snapshot from the single control writer; stale or retired owners return false.</summary>
        public bool TryPublish(long generation, float[] coefficients, PathPlaybackSettings settings)
        {
            if (generation != Generation) return false;
            return _lease.TryPublish(coefficients, new Bun3.Unity.Audio.SteamAudio.PathRenderSettings(settings.Listener,
                settings.EqLow, settings.EqMid, settings.EqHigh, settings.Gain, settings.NormalizeEq, settings.SpatialBlend));
        }
        /// <summary>Copies into fixed reader-owned storage without waiting. Exactly one audio owner is supported.</summary>
        public bool TryRead(float[] coefficients, out PathPlaybackSettings settings)
        {
            bool read = _storage.TryRead(Generation, coefficients, out var value);
            settings = new PathPlaybackSettings(value.Listener, value.EqLow, value.EqMid, value.EqHigh, value.Gain, value.NormalizeEq, value.SpatialBlend);
            return read;
        }
        /// <summary>Changes the immediate output gate independently of path publication; stale owners cannot unblock.</summary>
        public bool TrySetBlocked(long generation, bool blocked) => generation == Generation && _lease.TrySetBlocked(blocked);
        /// <summary>Permanently closes this speech generation's reader/publication admission and output gate.</summary>
        public void Retire() => _storage.Retire(Generation);
    }
}
